using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Callu.Domain.Entities;
using Callu.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Callu.Infrastructure.Audit;

public sealed record AuditArchiveResult(int DaysArchived, int RowsArchived, DateOnly? ArchivedThrough);

/// <summary>Writes closed days of the audit trail out as compressed files, with a manifest.</summary>
public sealed class AuditArchiveService(
    ApplicationDbContext db,
    IOptions<AuditArchiveOptions> options,
    ILogger<AuditArchiveService> logger)
{
    private const string ManifestFileName = "manifest.json";

    public bool Enabled => options.Value.Enabled;

    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<AuditArchiveResult> ArchiveAsync(DateTime utcNow, CancellationToken ct = default)
    {
        var settings = options.Value;
        if (!settings.Enabled) return new AuditArchiveResult(0, 0, null);

        Directory.CreateDirectory(settings.Path!);

        var closedBefore = DateOnly.FromDateTime(utcNow)
            .AddDays(-Math.Max(1, settings.AfterDays))
            .ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var alreadyDone = await db.AuditArchiveRuns.Select(r => r.Day).ToListAsync(ct);
        var done = alreadyDone.ToHashSet();

        var days = await db.AuditLogs
            .Where(l => l.CreatedAt < closedBefore)
            .Select(l => l.CreatedAt.Date)
            .Distinct()
            .OrderBy(d => d)
            .ToListAsync(ct);

        var pending = days
            .Select(DateOnly.FromDateTime)
            .Where(d => !done.Contains(d))
            .Take(Math.Max(1, settings.MaxDaysPerRun))
            .ToList();

        var archivedDays = 0;
        var archivedRows = 0;
        DateOnly? through = null;

        foreach (var day in pending)
        {
            var run = await ArchiveDayAsync(day, settings.Path!, ct);
            if (run is null) break;

            archivedDays++;
            archivedRows += run.RowCount;
            through = day;
        }

        if (archivedDays > 0)
        {
            await WriteManifestAsync(settings.Path!, ct);
            logger.LogInformation(
                "Audit archive wrote {Days} day(s), {Rows} entry(ies), through {Day}",
                archivedDays, archivedRows, through);
        }

        return new AuditArchiveResult(archivedDays, archivedRows, through);
    }

    /// <summary>The last day that is fully on disk; retention must not delete past it.</summary>
    public async Task<DateOnly?> ArchivedThroughAsync(CancellationToken ct = default)
    {
        if (!options.Value.Enabled) return null;

        var days = await db.AuditArchiveRuns.Select(r => r.Day).ToListAsync(ct);
        return days.Count == 0 ? null : days.Max();
    }

    private async Task<AuditArchiveRun?> ArchiveDayAsync(DateOnly day, string directory, CancellationToken ct)
    {
        var from = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var to = from.AddDays(1);

        // An unsealed entry cannot be verified from the archive later, and archiving past it would
        // let retention delete it. Stop here and pick the day up once the chain has caught up.
        var unsealed = await db.AuditLogs
            .CountAsync(l => l.CreatedAt >= from && l.CreatedAt < to && l.Sequence == null, ct);

        if (unsealed > 0)
        {
            logger.LogWarning(
                "Audit archive stopped at {Day}: {Count} entry(ies) are not chained yet", day, unsealed);
            return null;
        }

        var fileName = $"audit-{day:yyyy-MM-dd}.jsonl.gz";
        var finalPath = Path.Combine(directory, fileName);
        var tempPath = finalPath + ".partial";

        int rows;
        string hash;
        long size;
        long? firstSequence = null, lastSequence = null;
        DateTime firstAt = default, lastAt = default;

        // Written to a temporary name and moved into place, so a crash mid-write cannot leave a
        // truncated file that the manifest then vouches for.
        await using (var file = File.Create(tempPath))
        using (var sha = SHA256.Create())
        await using (var hashing = new CryptoStream(file, sha, CryptoStreamMode.Write))
        {
            await using (var gzip = new GZipStream(hashing, CompressionLevel.SmallestSize, leaveOpen: true))
            await using (var writer = new StreamWriter(gzip))
            {
                rows = 0;

                var entries = db.AuditLogs
                    .AsNoTracking()
                    .Where(l => l.CreatedAt >= from && l.CreatedAt < to)
                    .OrderBy(l => l.CreatedAt).ThenBy(l => l.Id)
                    .AsAsyncEnumerable();

                await foreach (var entry in entries.WithCancellation(ct))
                {
                    await writer.WriteLineAsync(JsonSerializer.Serialize(Record(entry), Json));

                    if (rows == 0) { firstSequence = entry.Sequence; firstAt = entry.CreatedAt; }
                    lastSequence = entry.Sequence;
                    lastAt = entry.CreatedAt;
                    rows++;
                }
            }

            hashing.FlushFinalBlock();
            hash = Convert.ToBase64String(sha.Hash!);
            size = file.Length;
        }

        if (rows == 0)
        {
            File.Delete(tempPath);
            return null;
        }

        var run = new AuditArchiveRun
        {
            Id = Guid.NewGuid(),
            Day = day,
            FileName = fileName,
            Sha256 = hash,
            RowCount = rows,
            SizeBytes = size,
            FirstSequence = firstSequence,
            LastSequence = lastSequence,
            FirstCreatedAt = firstAt,
            LastCreatedAt = lastAt,
        };

        // The row is the claim on this day, and it is taken BEFORE the file is published. The other
        // way round, two hosts both write the file and the loser's overwrite leaves the winner's
        // committed hash describing bytes that are no longer there.
        db.AuditArchiveRuns.Add(run);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            db.Entry(run).State = EntityState.Detached;
            File.Delete(tempPath);

            logger.LogInformation(
                "Audit archive for {Day} was already claimed by another run; this one wrote nothing.", day);
            return null;
        }

        try
        {
            File.Move(tempPath, finalPath, overwrite: false);
        }
        catch (Exception ex)
        {
            // The claim is committed and cannot be withdrawn: the row is soft-deletable but the unique
            // index on Day is not filtered, so removing it would block this day from ever being archived.
            logger.LogError(ex,
                "Audit archive for {Day} was claimed but its file could not be published to {Path}. "
                + "The run row exists with no file behind it; remove that row to let the day be archived again.",
                day, finalPath);
            throw;
        }

        return run;
    }

    private async Task WriteManifestAsync(string directory, CancellationToken ct)
    {
        var runs = await db.AuditArchiveRuns
            .AsNoTracking()
            .OrderBy(r => r.Day)
            .Select(r => new
            {
                day = r.Day.ToString("yyyy-MM-dd"),
                file = r.FileName,
                sha256 = r.Sha256,
                rows = r.RowCount,
                bytes = r.SizeBytes,
                firstSequence = r.FirstSequence,
                lastSequence = r.LastSequence,
                firstCreatedAt = r.FirstCreatedAt,
                lastCreatedAt = r.LastCreatedAt,
            })
            .ToListAsync(ct);

        var path = Path.Combine(directory, ManifestFileName);
        var temp = path + ".partial";

        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(new { archives = runs }, Json), ct);
        File.Move(temp, path, overwrite: true);
    }

    // The chain fields travel with the entry, so an archive plus the key verifies on its own.
    private static object Record(AuditLog e) => new
    {
        e.Id,
        e.CreatedAt,
        e.Sequence,
        e.PrevHash,
        e.RowHash,
        e.ActorId,
        e.ActorDisplayName,
        e.ActorType,
        e.Action,
        e.EventName,
        e.EventCategory,
        e.Outcome,
        e.ResourceType,
        e.ResourceId,
        e.Summary,
        e.ChangeBefore,
        e.ChangeAfter,
        e.RequestIpAddress,
        e.RequestUserAgent,
        e.RequestRoute,
        e.RequestId,
        e.CorrelationId,
        e.TraceId,
        e.SpanId,
        e.CanonicalizationVersion,
    };
}
