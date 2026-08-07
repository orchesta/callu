using System.Text.RegularExpressions;
using Callu.Domain.Entities;
using Callu.Infrastructure.Messaging.Persistence;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Transactions;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Callu.Tests;

/// <summary>There is exactly one shared change-tracker reset for a rollback on the shared scoped context, and this keeps it that way.</summary>
public class ChangeTrackerResetGuardTests
{
    private const string SharedResetFile = "ChangeTrackerReset.cs";

    /// <summary>Comments and literals out, because these files explain in prose the very call the scan hunts for.</summary>
    private static string CodeOnly(string source) => SourceScanner.Mask(source);

    /// <summary>
    /// The structural guard. The reset lives in one place so that what a rollback does to the scope is
    /// decided once, rather than per call site.
    /// </summary>
    [Fact]
    public void OnlyTheSharedReset_ClearsTheChangeTracker()
    {
        var offenders = SourceScanner.ProductFiles()
            .Where(f => Path.GetFileName(f) != SharedResetFile)
            .Where(f => Regex.IsMatch(SourceScanner.Code(f), @"ChangeTracker\s*\.\s*Clear\s*\("))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These files clear the change tracker directly instead of calling "
            + "ChangeTrackerReset.AfterRollback: " + string.Join(", ", offenders)
            + ". Whatever a rollback has to do to the shared scope, it does in one place, or the next "
            + "component to open a transaction on that scope inherits a different answer.");
    }

    /// <summary>Every component that owns a transaction on the shared context runs the reset after a rollback.</summary>
    [Fact]
    public void EveryTransactionOwner_OnTheSharedContext_ResetsAfterRollback()
    {
        string[] transactionOwners = ["TransactionManager.cs", "ScheduleMaterializer.cs"];

        var missing = SourceScanner.ProductFiles()
            .Where(f => transactionOwners.Contains(Path.GetFileName(f)))
            .Where(f => !SourceScanner.Code(f).Contains("AfterRollback", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(missing.Count == 0,
            "These components open a transaction on the shared scoped DbContext but do not reset the "
            + "change tracker when it rolls back: " + string.Join(", ", missing)
            + ". The failed attempt's entities stay tracked and the next SaveChanges on the scope "
            + "flushes rows the database already rolled back.");
    }

    /// <summary>
    /// The guard is only worth having if it can still go red once comments are stripped. Real code
    /// must be caught; the prose that explains the bug must not be.
    /// </summary>
    [Fact]
    public void TheGuard_Detects_ABareClear_ButNotProseAboutOne()
    {
        const string offending = "_context.ChangeTracker.Clear();";
        const string prose = "/// So a bare <c>ChangeTracker.Clear()</c> at the call site is not the reset.";

        Assert.Matches(@"ChangeTracker\s*\.\s*Clear\s*\(", CodeOnly(offending));
        Assert.DoesNotMatch(@"ChangeTracker\s*\.\s*Clear\s*\(", CodeOnly(prose));
    }

    // ── The behaviour the reset has to have ────────────────────────────────────────────────────────

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"tracker-reset-{Guid.NewGuid():N}").Options);

    private static ScheduleOccurrence Occurrence() => new()
    {
        Id = Guid.NewGuid(),
        ScheduleId = Guid.NewGuid(),
        RotationId = Guid.NewGuid(),
        UserId = "alice",
        StartUtc = Instant.FromUtc(2026, 7, 13, 9, 0),
        EndUtc = Instant.FromUtc(2026, 7, 13, 17, 0),
        IsPrimary = true,
        Order = 0,
        MaterializedAt = Instant.FromUtc(2026, 7, 13, 0, 0),
        CreatedAt = DateTime.UtcNow
    };

    /// <summary>
    /// The materializer bug, in miniature: without the reset, schedule A's rolled-back occurrences
    /// are still Added and schedule B's SaveChanges writes them.
    /// </summary>
    [Fact]
    public void AfterRollback_DropsTheFailedAttemptsEntities()
    {
        using var ctx = NewContext();

        ctx.Add(Occurrence());
        Assert.Single(ctx.ChangeTracker.Entries<ScheduleOccurrence>());

        ctx.AfterRollback();

        Assert.Empty(ctx.ChangeTracker.Entries<ScheduleOccurrence>());
    }

    /// <summary>
    /// A message staged by the failed attempt goes with it. Keeping it tracked would publish an
    /// escalation for a write that never happened.
    /// </summary>
    [Fact]
    public void AfterRollback_DropsAMessageTheFailedTransactionStaged()
    {
        using var ctx = NewContext();

        ctx.Add(new OutboxEntry
        {
            MessageType = "incident.escalation.trigger.v1",
            Payload = "{}",
            Status = OutboxEntryStatus.Pending,
            NextAttemptAt = DateTime.UtcNow,
        });

        Assert.Single(ctx.ChangeTracker.Entries<OutboxEntry>());

        ctx.AfterRollback();

        Assert.Empty(ctx.ChangeTracker.Entries<OutboxEntry>());
    }

    /// <summary>An already-committed row is Unchanged, so dropping it from the tracker changes nothing in the database.</summary>
    [Fact]
    public void AfterRollback_LeavesAnAlreadyCommittedRowInTheDatabase()
    {
        using var ctx = NewContext();

        var entry = new OutboxEntry
        {
            MessageType = "incident.escalation.trigger.v1",
            Payload = "{}",
            Status = OutboxEntryStatus.Pending,
            NextAttemptAt = DateTime.UtcNow,
        };

        ctx.Add(entry);
        ctx.SaveChanges();

        ctx.Add(Occurrence()); // the failed attempt's work
        ctx.AfterRollback();

        Assert.Empty(ctx.ChangeTracker.Entries<ScheduleOccurrence>());
        Assert.Equal(1, ctx.OutboxEntries.Count());
    }
}
