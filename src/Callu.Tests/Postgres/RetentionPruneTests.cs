using Callu.Domain.Entities;
using Callu.Infrastructure.Quartz;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Callu.Tests;

/// <summary>
/// The retention prune hard-deletes rows past the horizon (set-based DELETE) and, because it ignores
/// query filters, reclaims already-soft-deleted rows too — while keeping in-horizon rows.
/// </summary>
[Collection(PostgresCollection.Name)]
public class RetentionPruneTests(PostgresFixture pg)
{
    [PostgresFact]
    public async Task Prunes_rows_past_horizon_including_soft_deleted_keeps_recent()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var now = DateTime.UtcNow;
        var oldId = Guid.NewGuid();
        var oldSoftDeletedId = Guid.NewGuid();
        var recentId = Guid.NewGuid();

        await using (var db = PostgresFixture.Context(cs))
        {
            db.AuditLogs.AddRange(
                new AuditLog { Id = oldId, ResourceType = "Test", Action = default, CreatedAt = now.AddDays(-40) },
                new AuditLog { Id = oldSoftDeletedId, ResourceType = "Test", Action = default, IsDeleted = true, CreatedAt = now.AddDays(-40) },
                new AuditLog { Id = recentId, ResourceType = "Test", Action = default, CreatedAt = now.AddDays(-5) });
            await db.SaveChangesAsync();
        }

        await using (var db = PostgresFixture.Context(cs))
        {
            var deleted = await RetentionPruneQuartzJob.PruneTableAsync(
                db.AuditLogs, days: 30, now, CancellationToken.None);

            Assert.Equal(2, deleted);
        }

        await using (var db = PostgresFixture.Context(cs))
        {
            var remaining = await db.AuditLogs.IgnoreQueryFilters().Select(a => a.Id).ToListAsync();
            Assert.Equal(new[] { recentId }, remaining);
        }
    }

    [PostgresFact]
    public async Task Disabled_window_deletes_nothing()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var now = DateTime.UtcNow;
        var id = Guid.NewGuid();

        await using (var db = PostgresFixture.Context(cs))
        {
            db.AuditLogs.Add(new AuditLog { Id = id, ResourceType = "Test", Action = default, CreatedAt = now.AddDays(-3650) });
            await db.SaveChangesAsync();
        }

        await using (var db = PostgresFixture.Context(cs))
        {
            var deleted = await RetentionPruneQuartzJob.PruneTableAsync(
                db.AuditLogs, days: 0, now, CancellationToken.None);

            Assert.Equal(0, deleted);
            Assert.Equal(1, await db.AuditLogs.IgnoreQueryFilters().CountAsync());
        }
    }

    [PostgresFact]
    public async Task Deletes_in_batches_until_the_horizon_is_empty()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var now = DateTime.UtcNow;
        var recentId = Guid.NewGuid();

        await using (var db = PostgresFixture.Context(cs))
        {
            for (var i = 0; i < 5; i++)
                db.AuditLogs.Add(new AuditLog { Id = Guid.NewGuid(), ResourceType = "Test", Action = default, CreatedAt = now.AddDays(-40 - i) });

            db.AuditLogs.Add(new AuditLog { Id = recentId, ResourceType = "Test", Action = default, CreatedAt = now.AddDays(-1) });
            await db.SaveChangesAsync();
        }

        List<int> progress = [];

        await using (var db = PostgresFixture.Context(cs))
        {
            var deleted = await RetentionPruneQuartzJob.PruneTableAsync(
                db.AuditLogs, days: 30, now, CancellationToken.None,
                batchSize: 2, onBatch: progress.Add);

            Assert.Equal(5, deleted);
        }

        Assert.Equal(new[] { 2, 4, 5 }, progress);

        await using (var db = PostgresFixture.Context(cs))
        {
            var remaining = await db.AuditLogs.IgnoreQueryFilters().Select(a => a.Id).ToListAsync();
            Assert.Equal(new[] { recentId }, remaining);
        }
    }

    [PostgresFact]
    public async Task Per_run_cap_stops_early_and_keeps_the_rows_it_already_removed()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var now = DateTime.UtcNow;

        await using (var db = PostgresFixture.Context(cs))
        {
            for (var i = 0; i < 5; i++)
                db.AuditLogs.Add(new AuditLog { Id = Guid.NewGuid(), ResourceType = "Test", Action = default, CreatedAt = now.AddDays(-40 - i) });

            await db.SaveChangesAsync();
        }

        await using (var db = PostgresFixture.Context(cs))
        {
            var deleted = await RetentionPruneQuartzJob.PruneTableAsync(
                db.AuditLogs, days: 30, now, CancellationToken.None,
                batchSize: 2, maxRows: 3);

            Assert.Equal(3, deleted);
        }

        await using (var db = PostgresFixture.Context(cs))
            Assert.Equal(2, await db.AuditLogs.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task A_failing_table_does_not_skip_the_tables_after_it()
    {
        var ran = new List<string>();

        RetentionPruneQuartzJob.RetentionTarget[] targets =
        [
            new RetentionPruneQuartzJob.RetentionTarget("First", 30, _ => { ran.Add("First"); return Task.FromResult(1); }),
            new RetentionPruneQuartzJob.RetentionTarget("Boom", 30, _ => throw new TimeoutException("statement timeout")),
            new RetentionPruneQuartzJob.RetentionTarget("Last", 30, _ => { ran.Add("Last"); return Task.FromResult(2); })
        ];

        var failures = await RetentionPruneQuartzJob.SweepAsync(
            targets, NullLogger<RetentionPruneQuartzJob>.Instance, CancellationToken.None);

        Assert.Equal(new[] { "First", "Last" }, ran);
        Assert.Equal("Boom", Assert.Single(failures).Table);
    }

    [Fact]
    public async Task Sweep_skips_a_table_whose_window_is_disabled()
    {
        var invoked = false;

        RetentionPruneQuartzJob.RetentionTarget[] targets =
        [
            new RetentionPruneQuartzJob.RetentionTarget("Disabled", 0, _ => { invoked = true; return Task.FromResult(1); })
        ];

        var failures = await RetentionPruneQuartzJob.SweepAsync(
            targets, NullLogger<RetentionPruneQuartzJob>.Instance, CancellationToken.None);

        Assert.False(invoked);
        Assert.Empty(failures);
    }

    /// <summary>What retention removes from the audit trail is a prefix of the hash chain.</summary>
    // The chain reads what is left as running from its start; a hole in the middle is
    // indistinguishable from a deleted entry, and the nightly verification reports tampering.
    [PostgresFact]
    public async Task AuditPruning_TakesTheOldestSequencesAndLeavesAnUnbrokenTail()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var now = DateTime.UtcNow;

        await using (var db = PostgresFixture.Context(cs))
        {
            // Written in a shuffled order, so a prefix can only come from the ordering under test.
            foreach (var sequence in new long[] { 7, 3, 9, 1, 5, 8, 2, 6, 4, 10 })
            {
                db.AuditLogs.Add(new AuditLog
                {
                    Id = Guid.NewGuid(),
                    ResourceType = "Test",
                    Action = default,
                    CreatedAt = now.AddDays(-40),
                    Sequence = sequence,
                });
            }
            await db.SaveChangesAsync();
        }

        await using (var db = PostgresFixture.Context(cs))
        {
            await RetentionPruneQuartzJob.PruneTableAsync(
                db.AuditLogs, days: 30, now, CancellationToken.None,
                batchSize: 2, maxRows: 4, oldestFirst: e => e.Sequence!);
        }

        await using (var verify = PostgresFixture.Context(cs))
        {
            var left = await verify.AuditLogs.IgnoreQueryFilters()
                .Select(a => a.Sequence!.Value).OrderBy(v => v).ToListAsync();

            Assert.Equal(new long[] { 5, 6, 7, 8, 9, 10 }, left);
        }
    }

    /// <summary>Without an ordering the other tables keep the cheap unordered scan.</summary>
    [PostgresFact]
    public async Task WithoutAnOrdering_PruningStillRemovesTheRequestedCount()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var now = DateTime.UtcNow;

        await using (var db = PostgresFixture.Context(cs))
        {
            for (var i = 0; i < 10; i++)
                db.AuditLogs.Add(new AuditLog { Id = Guid.NewGuid(), ResourceType = "Test", Action = default, CreatedAt = now.AddDays(-40) });
            await db.SaveChangesAsync();
        }

        await using (var db = PostgresFixture.Context(cs))
        {
            var deleted = await RetentionPruneQuartzJob.PruneTableAsync(
                db.AuditLogs, days: 30, now, CancellationToken.None, batchSize: 2, maxRows: 4);

            Assert.Equal(4, deleted);
        }
    }
}
