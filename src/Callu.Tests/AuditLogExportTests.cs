using System.Text;
using System.Text.Json;
using Callu.Api.Controllers;
using Callu.Domain.Enums;
using Callu.Shared.Models.Audit;

namespace Callu.Tests;

/// <summary>An export leaves the product, so it has to be safe to open and cheap to produce.</summary>
public class AuditLogExportTests
{
    private static AuditLogDto Row(string? description = null, string? oldValues = null) => new()
    {
        Id = Guid.NewGuid(),
        CreatedAt = new DateTime(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc),
        ActorId = "u1",
        ActorDisplayName = "Ali Gören",
        ActorType = AuditActorType.User,
        Action = AuditAction.Login,
        EventName = "user.login",
        EventCategory = "identity-and-access-management",
        Outcome = AuditOutcome.Success,
        ResourceType = "User",
        Summary = description,
        ChangeBefore = oldValues,
    };

    private static async IAsyncEnumerable<AuditLogDto> Stream(params AuditLogDto[] rows)
    {
        foreach (var r in rows)
        {
            yield return r;
            await Task.Yield();
        }
    }

    private static string[] HeaderOf(string csv) => csv.Split('\n')[0].TrimEnd('\r').Split(',');

    private static async Task<string> CsvOf(params AuditLogDto[] rows)
    {
        using var output = new MemoryStream();
        await AuditLogExport.WriteCsvAsync(output, Stream(rows), CancellationToken.None);
        return Encoding.UTF8.GetString(output.ToArray());
    }

    [Fact]
    public async Task TheCsvStartsWithItsHeader()
    {
        var csv = await CsvOf(Row());

        Assert.StartsWith("createdAt,actorId,actorDisplayName,actorType,action", csv, StringComparison.Ordinal);
    }

    /// <summary>The sixteen event columns keep their names and their order.</summary>
    // A consumer that parses by column index breaks silently on an upgrade, so a new field is only
    // ever appended. This pins the sixteen that make up the OpenAuditModel-shaped event.
    [Fact]
    public async Task TheSixteenEventColumnsKeepTheirNamesAndOrder()
    {
        var csv = await CsvOf(Row());
        var header = HeaderOf(csv);

        Assert.Equal(
            new[]
            {
                "createdAt", "actorId", "actorDisplayName", "actorType", "action", "eventName",
                "eventCategory", "outcome", "resourceType", "resourceId", "summary", "changeBefore",
                "changeAfter", "requestIpAddress", "requestUserAgent", "requestRoute",
            },
            header.Take(16));
    }

    /// <summary>The chain columns an outside reviewer needs, in the four positions they have always held.</summary>
    // Without them a CSV cannot say whether it is an unbroken range or a filtered selection. Their
    // position is pinned rather than their being last: a later field is appended after them, and
    // inserting one ahead of them to keep them last is the shift this file exists to prevent.
    [Fact]
    public async Task TheChainColumnsKeepPositionsSixteenToNineteen()
    {
        var csv = await CsvOf(Row());
        var header = HeaderOf(csv);

        Assert.Equal(new[] { "id", "sequence", "prevHash", "rowHash" }, header.Skip(16).Take(4));
    }

    /// <summary>Everything the correlation round added, after the columns that came before it.</summary>
    [Fact]
    public async Task TheCorrelationColumnsAreAppendedAfterTheChainColumns()
    {
        var csv = await CsvOf(Row());
        var header = HeaderOf(csv);

        Assert.Equal(
            new[] { "requestId", "correlationId", "traceId", "spanId", "canonicalization" },
            header.Skip(20));
    }

    /// <summary>A row's cells line up with the header it was written under.</summary>
    // A column appended to one and not the other is the failure this whole file is about, and it is
    // invisible until a consumer reads the wrong field under the right name.
    [Fact]
    public async Task EveryRowHasAsManyCellsAsTheHeader()
    {
        var row = Row();
        row.Sequence = 7;
        row.TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";
        row.CorrelationId = "escalation:step-2";

        var csv = await CsvOf(row);
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(HeaderOf(csv).Length, lines[1].Split(',').Length);
    }

    [Fact]
    public async Task AChainedRowCarriesItsSequenceAndBothHashes()
    {
        var row = Row();
        row.Sequence = 42;
        row.PrevHash = "prev-abc";
        row.RowHash = "row-def";

        var csv = await CsvOf(row);

        var line = csv.Split('\n')[1];
        Assert.Contains("42", line, StringComparison.Ordinal);
        Assert.Contains("prev-abc", line, StringComparison.Ordinal);
        Assert.Contains("row-def", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARowLandsOnItsOwnLine()
    {
        var csv = await CsvOf(Row(), Row(), Row());

        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, lines.Length);
    }

    /// <summary>Audited text is operator input, and a spreadsheet runs a leading = as a formula.</summary>
    [Theory]
    [InlineData("=1+1")]
    [InlineData("+1234")]
    [InlineData("-cmd")]
    [InlineData("@SUM")]
    public async Task ACellThatWouldRunAsAFormula_IsNeutralised(string dangerous)
    {
        var cell = AuditLogExport.Cell(dangerous);

        Assert.Equal($"\"'{dangerous}\"", cell);

        var csv = await CsvOf(Row(description: dangerous));
        Assert.Contains($"\"'{dangerous}\"", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOrdinaryCellIsNotDecorated()
    {
        Assert.Equal("\"Ali Gören\"", AuditLogExport.Cell("Ali Gören"));
    }

    [Fact]
    public void AQuoteInsideACellIsDoubled()
    {
        Assert.Equal("\"say \"\"hello\"\"\"", AuditLogExport.Cell("say \"hello\""));
    }

    /// <summary>A comma or a newline inside a value must not become a new column or row.</summary>
    [Fact]
    public async Task SeparatorsInsideAValueStayInsideTheirCell()
    {
        var csv = await CsvOf(Row(description: "a,b", oldValues: "line1\nline2"));

        // The header is the only line with no embedded value, so it is the one that can be counted.
        Assert.Equal(HeaderOf(csv).Length, HeaderOf(csv).Distinct().Count());
        Assert.Contains("\"a,b\"", csv, StringComparison.Ordinal);
        Assert.Contains("\"line1\nline2\"", csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task JsonLinesWritesOneObjectPerLine()
    {
        using var output = new MemoryStream();
        await AuditLogExport.WriteJsonLinesAsync(output, Stream(Row(), Row()), "Test", null, CancellationToken.None);

        var lines = Encoding.UTF8.GetString(output.ToArray())
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, lines.Length);
        foreach (var line in lines)
        {
            using var parsed = JsonDocument.Parse(line);
            Assert.Equal("0.1", parsed.RootElement.GetProperty("specVersion").GetString());
            Assert.Equal("user.login", parsed.RootElement.GetProperty("event").GetProperty("name").GetString());
            Assert.Equal("Callu", parsed.RootElement.GetProperty("application").GetProperty("name").GetString());
        }
    }

    /// <summary>The writer must consume the source lazily, or a long range is held in memory first.</summary>
    [Fact]
    public async Task TheWriterNeverPullsTheWholeRangeBeforeWriting()
    {
        var produced = 0;
        var observedWhileProducing = new List<long>();
        using var output = new MemoryStream();

        async IAsyncEnumerable<AuditLogDto> Counting()
        {
            for (var i = 0; i < 50; i++)
            {
                produced++;
                observedWhileProducing.Add(output.Length);
                yield return Row();
                await Task.Yield();
            }
        }

        await AuditLogExport.WriteCsvAsync(output, Counting(), CancellationToken.None);

        Assert.Equal(50, produced);
        // Bytes were already on the way out before the source was exhausted.
        Assert.Contains(observedWhileProducing, written => written > 0);
    }

    [Fact]
    public async Task AnEmptyRangeStillProducesAReadableFile()
    {
        var csv = await CsvOf();

        Assert.Single(csv.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }
}
