using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Callu.Infrastructure.Audit.OpenAuditModel;
using Callu.Shared.Audit;
using Callu.Shared.Models.Audit;

namespace Callu.Api.Controllers;

/// <summary>Writes an audit export straight to the response, one row at a time.</summary>
// Nothing here builds the whole file first: a five-year range would not fit in the memory a small
// self-hosted box has.
public static class AuditLogExport
{
    public const string CsvContentType = "text/csv; charset=utf-8";
    public const string JsonLinesContentType = "application/x-ndjson; charset=utf-8";

    // Appended, never inserted: a consumer parsing by column index must survive an upgrade.
    private static readonly string[] CsvHeader =
    {
        "createdAt", "actorId", "actorDisplayName", "actorType", "action", "eventName",
        "eventCategory", "outcome", "resourceType", "resourceId", "summary", "changeBefore",
        "changeAfter", "requestIpAddress", "requestUserAgent", "requestRoute",
        "id", "sequence", "prevHash", "rowHash",
        "requestId", "correlationId", "traceId", "spanId", "canonicalization",
    };

    public static async Task WriteCsvAsync(
        Stream output, IAsyncEnumerable<AuditLogDto> rows, CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true);

        await writer.WriteLineAsync(string.Join(',', CsvHeader));

        await foreach (var row in rows.WithCancellation(cancellationToken))
        {
            var line = string.Join(',', new[]
            {
                Cell(row.CreatedAt.ToString("O", CultureInfo.InvariantCulture)),
                Cell(row.ActorId),
                Cell(row.ActorDisplayName),
                Cell(row.ActorType.ToString()),
                Cell(row.Action.ToString()),
                Cell(row.EventName),
                Cell(row.EventCategory),
                Cell(row.Outcome.ToString()),
                Cell(row.ResourceType),
                Cell(row.ResourceId?.ToString()),
                Cell(row.Summary),
                Cell(row.ChangeBefore),
                Cell(row.ChangeAfter),
                Cell(row.RequestIpAddress),
                Cell(row.RequestUserAgent),
                Cell(row.RequestRoute),
                Cell(row.Id.ToString()),
                Cell(row.Sequence?.ToString(CultureInfo.InvariantCulture)),
                Cell(row.PrevHash),
                Cell(row.RowHash),
                Cell(row.RequestId),
                Cell(row.CorrelationId),
                Cell(row.TraceId),
                Cell(row.SpanId),
                Cell(row.Sequence is null ? null : CalluAuditChain.VersionOf(row.CanonicalizationVersion)),
            });

            await writer.WriteLineAsync(line);
        }

        await writer.FlushAsync(cancellationToken);
    }

    /// <summary>Each line is the event stored when the row was sealed, signed here when a key is active.</summary>
    // The signature sits outside the digest, so adding it now leaves the stored hash true.
    public static async Task WriteJsonLinesAsync(
        Stream output,
        IAsyncEnumerable<string> storedEvents,
        IOpenAuditSigner? signer,
        CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true);

        await foreach (var stored in storedEvents.WithCancellation(cancellationToken))
        {
            if (signer is null)
            {
                await writer.WriteLineAsync(stored);
                continue;
            }

            var document = JsonNode.Parse(stored)!.AsObject();
            var signature = new JsonObject
            {
                ["algorithm"] = signer.Algorithm,
                ["value"] = signer.Sign(OpenAuditDigest.Input(document)),
            };
            if (signer.KeyId is { Length: > 0 } keyId) signature["keyId"] = keyId;
            document["integrity"]!.AsObject()["signature"] = signature;

            await writer.WriteLineAsync(JsonCanonicalizer.Canonicalize(document.ToJsonString()));
        }

        await writer.FlushAsync(cancellationToken);
    }

    /// <summary>Quotes a CSV cell and stops a spreadsheet treating audited text as a formula.</summary>
    public static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";

        // Audit values are operator input. A cell opening with one of these runs as a formula when
        // the export is opened, so the leading character is neutralised rather than dropped.
        var neutralised = value[0] is '=' or '+' or '-' or '@' or '\t' or '\r'
            ? "'" + value
            : value;

        return "\"" + neutralised.Replace("\"", "\"\"") + "\"";
    }
}
