using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Audit;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Quartz;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Callu.Tests;

/// <summary>Archiving closed days of the audit trail, and the rule that retention waits for it.</summary>
[Collection(PostgresCollection.Name)]
public class AuditArchiveTests(PostgresFixture fixture) : IDisposable
{
    private static readonly DateTime Now = new(2026, 7, 20, 9, 0, 0, DateTimeKind.Utc);

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"callu-archive-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { /* best effort */ }
    }

    private static AuditLog Row(DateTime at) => new()
    {
        Id = Guid.NewGuid(),
        CreatedAt = at,
        UpdatedAt = at,
        ActorId = "u1",
        ActorDisplayName = "Ada",
        Action = AuditAction.Login,
        ResourceType = "User",
        ResourceId = Guid.NewGuid(),
        Summary = $"at {at:O}",
    };

    private AuditArchiveService Archive(ApplicationDbContext ctx, string? path = null, int afterDays = 2, int maxDays = 31) =>
        new(ctx,
            Options.Create(new AuditArchiveOptions
            {
                Path = path ?? _directory,
                AfterDays = afterDays,
                MaxDaysPerRun = maxDays,
            }),
            NullLogger<AuditArchiveService>.Instance);

    private static AuditChainService Chain(ApplicationDbContext ctx) =>
        new(ctx, DataProtectionProvider.Create(nameof(AuditArchiveTests)), NullLogger<AuditChainService>.Instance);

    /// <summary>Three closed days plus today, all chained.</summary>
    private async Task<ApplicationDbContext> SeededAsync(bool seal = true)
    {
        var cs = await fixture.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var ctx = PostgresFixture.Context(cs);
        ctx.AddRange(
            Row(Now.AddDays(-5)), Row(Now.AddDays(-5).AddHours(3)),
            Row(Now.AddDays(-4)),
            Row(Now.AddDays(-3)), Row(Now.AddDays(-3).AddHours(1)), Row(Now.AddDays(-3).AddHours(2)),
            Row(Now.AddHours(-1)));
        await ctx.SaveChangesAsync();

        if (seal) await Chain(ctx).SealAsync();
        return ctx;
    }

    private string[] Files() =>
        Directory.Exists(_directory)
            ? [.. Directory.EnumerateFiles(_directory).Select(Path.GetFileName).OrderBy(f => f)!]
            : [];

    private static List<JsonElement> ReadArchive(string path)
    {
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);

        List<JsonElement> lines = [];
        while (reader.ReadLine() is { } line)
            if (!string.IsNullOrWhiteSpace(line))
                lines.Add(JsonDocument.Parse(line).RootElement.Clone());
        return lines;
    }

    // ---- what gets written -------------------------------------------------

    [PostgresFact]
    public async Task WritesOneCompressedFilePerClosedDay()
    {
        var ctx = await SeededAsync();

        var result = await Archive(ctx).ArchiveAsync(Now);

        Assert.Equal(3, result.DaysArchived);
        Assert.Equal(6, result.RowsArchived);
        Assert.Equal(
            ["audit-2026-07-15.jsonl.gz", "audit-2026-07-16.jsonl.gz", "audit-2026-07-17.jsonl.gz", "manifest.json"],
            Files());
    }

    /// <summary>A later run leaves an archived day's file and its committed hash exactly as they were.</summary>
    // This is the sequential case, and it passes on the pre-check alone: the concurrent one, where two
    // runs both read the day as pending, is held off by the claim row's unique index rather than by
    // anything this test reaches.
    [PostgresFact]
    public async Task ALaterRunOverAnArchivedDay_LeavesTheCommittedFileAlone()
    {
        var ctx = await SeededAsync();
        await Archive(ctx).ArchiveAsync(Now);

        var archived = Path.Combine(_directory, "audit-2026-07-15.jsonl.gz");
        var committed = await ctx.AuditArchiveRuns
            .AsNoTracking()
            .FirstAsync(r => r.Day == new DateOnly(2026, 7, 15));
        var bytesBefore = await File.ReadAllBytesAsync(archived);

        var second = await Archive(ctx).ArchiveAsync(Now);

        Assert.Equal(0, second.DaysArchived);
        Assert.Equal(bytesBefore, await File.ReadAllBytesAsync(archived));

        var runs = await ctx.AuditArchiveRuns
            .AsNoTracking()
            .Where(r => r.Day == new DateOnly(2026, 7, 15))
            .ToListAsync();

        Assert.Single(runs);
        Assert.Equal(committed.Sha256, runs[0].Sha256);
    }

    /// <summary>Today is still being written to, so it is not archived and retention cannot reach it.</summary>
    [PostgresFact]
    public async Task LeavesTheDaysThatAreStillOpen()
    {
        var ctx = await SeededAsync();

        await Archive(ctx).ArchiveAsync(Now);

        Assert.DoesNotContain("audit-2026-07-20.jsonl.gz", Files());
        Assert.Equal(new DateOnly(2026, 7, 17), await Archive(ctx).ArchivedThroughAsync());
    }

    [PostgresFact]
    public async Task TheArchiveCarriesTheEntriesAndTheirChainFields()
    {
        var ctx = await SeededAsync();
        await Archive(ctx).ArchiveAsync(Now);

        var lines = ReadArchive(Path.Combine(_directory, "audit-2026-07-17.jsonl.gz"));

        Assert.Equal(3, lines.Count);
        Assert.All(lines, l =>
        {
            Assert.True(l.GetProperty("Sequence").GetInt64() > 0);
            Assert.False(string.IsNullOrWhiteSpace(l.GetProperty("RowHash").GetString()));
            Assert.Equal("Login", l.GetProperty("Action").GetString());
        });
    }

    /// <summary>The manifest is the point of the exercise: it has to match the bytes on disk.</summary>
    [PostgresFact]
    public async Task TheManifestHashMatchesTheFileOnDisk()
    {
        var ctx = await SeededAsync();
        await Archive(ctx).ArchiveAsync(Now);

        var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_directory, "manifest.json")));
        var archives = manifest.RootElement.GetProperty("archives").EnumerateArray().ToList();

        Assert.Equal(3, archives.Count);

        foreach (var entry in archives)
        {
            var path = Path.Combine(_directory, entry.GetProperty("file").GetString()!);
            var actual = Convert.ToBase64String(SHA256.HashData(await File.ReadAllBytesAsync(path)));

            Assert.Equal(entry.GetProperty("sha256").GetString(), actual);
            Assert.Equal(new FileInfo(path).Length, entry.GetProperty("bytes").GetInt64());
        }
    }

    [PostgresFact]
    public async Task ASecondRunDoesNotRedoADayAlreadyOnDisk()
    {
        var ctx = await SeededAsync();
        await Archive(ctx).ArchiveAsync(Now);

        var again = await Archive(ctx).ArchiveAsync(Now);

        Assert.Equal(0, again.DaysArchived);
        Assert.Equal(3, await ctx.AuditArchiveRuns.CountAsync());
    }

    [PostgresFact]
    public async Task PicksUpTheNextDayOnceItCloses()
    {
        var ctx = await SeededAsync();
        await Archive(ctx).ArchiveAsync(Now);

        var later = await Archive(ctx).ArchiveAsync(Now.AddDays(3));

        Assert.Equal(1, later.DaysArchived);
        Assert.Contains("audit-2026-07-20.jsonl.gz", Files());
    }

    /// <summary>An entry outside the chain could not be verified from the archive, and archiving
    /// past it would let retention delete it.</summary>
    [PostgresFact]
    public async Task StopsAtADayThatIsNotChainedYet()
    {
        var ctx = await SeededAsync(seal: false);

        var result = await Archive(ctx).ArchiveAsync(Now);

        Assert.Equal(0, result.DaysArchived);
        Assert.Empty(Files());
    }

    [PostgresFact]
    public async Task WritesNothingWhenNoPathIsConfigured()
    {
        var ctx = await SeededAsync();

        var result = await Archive(ctx, path: "").ArchiveAsync(Now);

        Assert.Equal(0, result.DaysArchived);
        Assert.Null(await Archive(ctx, path: "").ArchivedThroughAsync());
        Assert.Empty(Files());
    }

    [PostgresFact]
    public async Task StopsAtThePerRunCeiling()
    {
        var ctx = await SeededAsync();

        var result = await Archive(ctx, maxDays: 2).ArchiveAsync(Now);

        Assert.Equal(2, result.DaysArchived);
        Assert.Equal(new DateOnly(2026, 7, 16), result.ArchivedThrough);
    }

    // ---- the rule that makes archiving worth having ------------------------

    /// <summary>With archiving on, retention deletes only what has already reached the archive.</summary>
    [PostgresFact]
    public async Task RetentionStopsAtTheArchiveWatermark()
    {
        var ctx = await SeededAsync();
        await Archive(ctx, maxDays: 2).ArchiveAsync(Now);

        var through = await Archive(ctx).ArchivedThroughAsync();
        var cap = through!.Value.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var deleted = await RetentionPruneQuartzJob.PruneTableAsync(
            ctx.AuditLogs, days: 1, Now, CancellationToken.None, cutoffCap: cap);

        // The 15th and 16th are archived and go; the 17th is not archived yet and stays, even
        // though a one-day window would otherwise have taken it.
        Assert.Equal(3, deleted);
        var left = await ctx.AuditLogs.IgnoreQueryFilters().CountAsync();
        Assert.Equal(4, left);
    }

    /// <summary>Archiving switched on before anything has been written deletes nothing at all.</summary>
    [PostgresFact]
    public async Task RetentionDeletesNothingWhileTheArchiveIsStillEmpty()
    {
        var ctx = await SeededAsync();

        var deleted = await RetentionPruneQuartzJob.PruneTableAsync(
            ctx.AuditLogs, days: 1, Now, CancellationToken.None, cutoffCap: DateTime.MinValue);

        Assert.Equal(0, deleted);
        Assert.Equal(7, await ctx.AuditLogs.IgnoreQueryFilters().CountAsync());
    }

    /// <summary>Archiving off leaves retention exactly as it was.</summary>
    [PostgresFact]
    public async Task RetentionIsUnchangedWhenArchivingIsOff()
    {
        var ctx = await SeededAsync();

        var deleted = await RetentionPruneQuartzJob.PruneTableAsync(
            ctx.AuditLogs, days: 1, Now, CancellationToken.None, cutoffCap: null);

        Assert.Equal(6, deleted);
    }
}
