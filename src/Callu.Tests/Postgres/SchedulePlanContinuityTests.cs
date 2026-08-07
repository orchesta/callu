using Callu.Application.Services;
using Callu.Application.Validators;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Identity;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Infrastructure.Services;
using Callu.Shared.Models.Schedules;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NodaTime;
using Npgsql;
using NSubstitute;

namespace Callu.Tests;

/// <summary>The plan endpoint at the level that decides whether a phone rings: the occurrence rows.</summary>
// The delete-then-reinsert window inside the materializer only exists where transactions are real.
[Collection(PostgresCollection.Name)]
public class SchedulePlanContinuityTests(PostgresFixture pg)
{
    /// <summary>Europe/Istanbul is a fixed UTC+3 — no DST, so a gap in the rota is the rota's fault.</summary>
    private const string Zone = "Europe/Istanbul";

    /// <summary>Must match ScheduleMaterializer's private advisory-lock namespace.</summary>
    private const int LockNamespace = 0x5CED_0001;

    private static readonly Instant Now = Instant.FromUtc(2026, 7, 14, 12, 0);

    /// <summary>Anchors for two seven-day shifts a fortnight apart, tiling the timeline with no seam.</summary>
    private static readonly LocalDateTime AliceHandover = new(2026, 6, 30, 10, 0);
    private static readonly LocalDateTime BobHandover = new(2026, 7, 7, 10, 0);

    private static readonly Guid ScheduleId = Guid.NewGuid();
    private static readonly Guid TeamId = Guid.NewGuid();
    private static readonly Guid OtherTeamId = Guid.NewGuid();
    private static readonly Guid AliceRotationId = Guid.NewGuid();
    private static readonly Guid BobRotationId = Guid.NewGuid();

    // ---- the gap ---------------------------------------------------------------------------

    /// <summary>A re-order leaves every hour of the horizon covered by exactly one member.</summary>
    // Sampled hour by hour: a missing week and a duplicated week both leave the row count plausible.
    [PostgresFact]
    public async Task Reorder_LeavesEveryHourOfTheHorizonCoveredByExactlyOneMember()
    {
        var cs = await SeededAsync();
        await MaterializeAsync(cs);

        await AssertNoUncoveredHourAsync(cs, "before the re-order");

        await SaveAsync(cs, SwappedRota);

        await AssertNoUncoveredHourAsync(cs, "after the re-order");
    }

    /// <summary>A re-order has to actually move who is paged, not just what the UI shows.</summary>
    [PostgresFact]
    public async Task Reorder_MovesWhoIsOnCall_AtTheOccurrenceLevel()
    {
        var cs = await SeededAsync();
        await MaterializeAsync(cs);

        Assert.Equal("alice", await OnCallAtAsync(cs, Now));

        await SaveAsync(cs, SwappedRota);

        Assert.Equal("bob", await OnCallAtAsync(cs, Now));

        // ...and the handover a week out swaps with it, rather than both slots collapsing onto bob.
        Assert.Equal("alice", await OnCallAtAsync(cs, Now + Duration.FromDays(8)));
    }

    /// <summary>The rota is materialized once, from a plan that is already committed in full.</summary>
    // The spy reads on its own connection, so what it sees is only what was durable.
    [PostgresFact]
    public async Task Reorder_MaterializesTheFinishedPlan_Once_NeverAHalfAppliedOne()
    {
        var cs = await SeededAsync();
        await MaterializeAsync(cs);

        await using var db = PostgresFixture.Context(cs);
        var spy = new RotaSpy(Materializer(db), cs);

        Assert.True(await Service(db, spy).SaveSchedulePlanAsync(ScheduleId, SwappedRota));

        var rota = Assert.Single(spy.Committed);
        Assert.Equal([("bob", AliceHandover, 1), ("alice", BobHandover, 2)], rota);
    }

    // ---- the rematerialize window ------------------------------------------------------------

    /// <summary>A reader must see the old rota or the new one, never neither, mid-rematerialize.</summary>
    // Another connection holds the schedule's advisory lock, so the materializer waits at its gate.
    [PostgresFact]
    public async Task ARematerializeInFlight_NeverLeavesTheRotaUncovered()
    {
        var cs = await SeededAsync();
        await MaterializeAsync(cs);

        await using var gate = new NpgsqlConnection(cs);
        await gate.OpenAsync();
        await using var holding = await gate.BeginTransactionAsync();
        await using (var take = new NpgsqlCommand(
                         $"SELECT pg_advisory_xact_lock({LockNamespace}, {ScheduleKey})", gate, holding))
            await take.ExecuteNonQueryAsync();

        var save = Task.Run(async () =>
        {
            await using var db = PostgresFixture.Context(cs);
            await Service(db).SaveSchedulePlanAsync(ScheduleId, SwappedRota);
        });

        // Premise check. The save must be blocked INSIDE the materializer; if it sailed through, the
        // lock namespace moved and this test would be asserting nothing at all.
        var finished = await Task.WhenAny(save, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.False(finished == save,
            "the rematerialize did not take the schedule's advisory lock — this test's premise is broken, "
            + "not the code under test.");

        // The rota is mid-flight: the new rotations are committed, the occurrences are not yet
        // regenerated. Somebody is still on call — the pager is never handed to nobody.
        Assert.Equal("alice", await OnCallAtAsync(cs, Now));
        await AssertNoUncoveredHourAsync(cs, "while the rematerialize is blocked");

        await holding.RollbackAsync();
        await save;

        Assert.Equal("bob", await OnCallAtAsync(cs, Now));
        await AssertNoUncoveredHourAsync(cs, "after the rematerialize committed");
    }

    /// <summary>A reader scanning across real, unrigged re-orders never once finds the rota empty.</summary>
    [PostgresFact]
    public async Task AReaderScanningAcrossRealReorders_NeverFindsTheRotaEmpty()
    {
        var cs = await SeededAsync();
        await MaterializeAsync(cs);

        // An instant every version of the rota covers, so an empty read means a torn write, never a
        // legitimately unstaffed hour.
        var probe = Now + Duration.FromHours(2);
        var reads = 0;
        var uncovered = 0;
        using var stop = new CancellationTokenSource();

        var reader = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                await using var db = PostgresFixture.Context(cs);
                var onCall = await db.Set<ScheduleOccurrence>()
                    .CountAsync(o => o.ScheduleId == ScheduleId && o.StartUtc <= probe && o.EndUtc > probe,
                        CancellationToken.None);

                reads++;
                if (onCall == 0) uncovered++;
            }
        });

        for (var i = 0; i < 6; i++)
            await SaveAsync(cs, i % 2 == 0 ? SwappedRota : StoredRota);

        await stop.CancelAsync();
        await reader;

        Assert.True(reads > 10, $"the reader only managed {reads} scans — it never overlapped a rematerialize.");
        Assert.True(uncovered == 0,
            $"{uncovered} of {reads} scans found NOBODY on call while the rota was being regenerated. "
            + "An incident in that window pages nobody.");
    }

    // ---- surviving a rematerialize that never lands -------------------------------------------

    /// <summary>A rota left behind by a failed rematerialize is regenerated by the next save.</summary>
    // Field by field the plan is unchanged, so recovery has to come from the flag rather than the diff.
    [PostgresFact]
    public async Task ARotaLeftBehindByAFailedRematerialize_IsRegeneratedByTheNextSave_AndTheFlagCleared()
    {
        var cs = await SeededAsync();
        await MaterializeAsync(cs);

        // Exactly what a crashed post-commit rematerialize leaves: rotations swapped, occurrences not.
        await using (var db = PostgresFixture.Context(cs))
        {
            var alice = await db.Set<ScheduleRotation>().SingleAsync(r => r.Id == AliceRotationId);
            var bob = await db.Set<ScheduleRotation>().SingleAsync(r => r.Id == BobRotationId);

            (alice.HandoverStartLocal, bob.HandoverStartLocal) = (BobHandover, AliceHandover);
            (alice.IsPrimary, bob.IsPrimary) = (false, true);
            (alice.Order, bob.Order) = (2, 1);

            var schedule = await db.Set<Schedule>().SingleAsync(s => s.Id == ScheduleId);
            schedule.NeedsRematerializeSince = DateTime.UtcNow;

            await db.SaveChangesAsync();
        }

        // The damage, stated plainly: bob is the stored primary and alice is still the one paged.
        Assert.Equal("alice", await OnCallAtAsync(cs, Now));

        // The no-op re-save. Nothing in the plan moves — the recovery has to come from the flag.
        await SaveAsync(cs, SwappedRota);

        Assert.Equal("bob", await OnCallAtAsync(cs, Now));
        await AssertNoUncoveredHourAsync(cs, "after recovering a rota left behind by a failed rematerialize");

        await using var check = PostgresFixture.Context(cs);
        var recovered = await check.Set<Schedule>().AsNoTracking().SingleAsync(s => s.Id == ScheduleId);
        Assert.Null(recovered.NeedsRematerializeSince);
    }

    /// <summary>The daily job recovers a flagged schedule when no operator comes back to retry.</summary>
    [PostgresFact]
    public async Task TheDailyJob_RecoversAFlaggedSchedule_WithoutAnOperatorRetrying()
    {
        var cs = await SeededAsync();
        await MaterializeAsync(cs);

        await using (var db = PostgresFixture.Context(cs))
        {
            var alice = await db.Set<ScheduleRotation>().SingleAsync(r => r.Id == AliceRotationId);
            var bob = await db.Set<ScheduleRotation>().SingleAsync(r => r.Id == BobRotationId);

            (alice.HandoverStartLocal, bob.HandoverStartLocal) = (BobHandover, AliceHandover);
            (alice.IsPrimary, bob.IsPrimary) = (false, true);
            (alice.Order, bob.Order) = (2, 1);

            var schedule = await db.Set<Schedule>().SingleAsync(s => s.Id == ScheduleId);
            schedule.NeedsRematerializeSince = DateTime.UtcNow;

            await db.SaveChangesAsync();
        }

        await using (var db = PostgresFixture.Context(cs))
            await Materializer(db).RematerializeAllAsync(IScheduleMaterializer.DefaultHorizon);

        Assert.Equal("bob", await OnCallAtAsync(cs, Now));

        await using var check = PostgresFixture.Context(cs);
        var recovered = await check.Set<Schedule>().AsNoTracking().SingleAsync(s => s.Id == ScheduleId);
        Assert.Null(recovered.NeedsRematerializeSince);
    }

    // ---- the neighbouring behaviours a rewrite must not break --------------------------------

    /// <summary>An edit that sends no rotations must not regenerate a single occurrence.</summary>
    [PostgresFact]
    public async Task ANameOnlyEdit_DoesNotRegenerateASingleOccurrence()
    {
        var cs = await SeededAsync();
        await MaterializeAsync(cs);

        var before = await OccurrencesAsync(cs);

        await SaveAsync(cs, new SaveSchedulePlanRequest { Name = "Renamed", Description = "Now with a description" });

        var after = await OccurrencesAsync(cs);

        Assert.Equal(
            before.Select(o => (o.Id, o.UserId, o.StartUtc, o.EndUtc, o.MaterializedAt)),
            after.Select(o => (o.Id, o.UserId, o.StartUtc, o.EndUtc, o.MaterializedAt)));
        Assert.Equal("alice", await OnCallAtAsync(cs, Now));
    }

    /// <summary>
    /// Moving the schedule to another team and re-staffing it in one save: the new team's member
    /// ends up holding the pager, with the horizon still covered end to end.
    /// </summary>
    [PostgresFact]
    public async Task ATeamMoveWithANewRota_PutsTheNewTeamOnCall_WithoutOpeningAGap()
    {
        var cs = await SeededAsync();
        await MaterializeAsync(cs);

        await SaveAsync(cs, new SaveSchedulePlanRequest
        {
            TeamId = OtherTeamId,
            Rotations =
            [
                PlanRotation(null, "carol", AliceHandover, isPrimary: true, order: 1),
                PlanRotation(null, "dave", BobHandover, isPrimary: false, order: 2)
            ]
        });

        Assert.Equal("carol", await OnCallAtAsync(cs, Now));
        Assert.Equal("dave", await OnCallAtAsync(cs, Now + Duration.FromDays(8)));

        var occurrences = await OccurrencesAsync(cs);
        Assert.DoesNotContain(occurrences, o => o.UserId is "alice" or "bob");
        await AssertNoUncoveredHourAsync(cs, "after the team move");
    }

    // ---- assertions ---------------------------------------------------------------------------

    /// <summary>Walks the horizon hour by hour and demands exactly one member on call at every one.</summary>
    // Zero pages nobody; two is the double-page the Order/IsPrimary tie-break exists to prevent.
    private async Task AssertNoUncoveredHourAsync(string cs, string when)
    {
        var occurrences = await OccurrencesAsync(cs);

        // Stop an hour short of the horizon: the materializer stops EMITTING at the horizon, so the
        // final hour is legitimately open-ended, not a hole.
        for (var hour = 0; hour < 29 * 24; hour++)
        {
            var at = Now + Duration.FromHours(hour);
            var onCall = occurrences.Where(o => o.StartUtc <= at && o.EndUtc > at).ToList();

            Assert.True(onCall.Count == 1,
                $"{when}: {onCall.Count} member(s) on call at {at} (hour {hour} of the horizon) — expected exactly 1. "
                + (onCall.Count == 0
                    ? "An incident at that instant pages NOBODY."
                    : $"Double-booked: {string.Join(", ", onCall.Select(o => o.UserId))}."));
        }
    }

    private async Task<string?> OnCallAtAsync(string cs, Instant at)
    {
        var occurrences = await OccurrencesAsync(cs);

        return occurrences
            .Where(o => o.StartUtc <= at && o.EndUtc > at)
            .OrderByDescending(o => o.IsPrimary)
            .ThenBy(o => o.Order)
            .Select(o => o.UserId)
            .FirstOrDefault();
    }

    private static async Task<List<ScheduleOccurrence>> OccurrencesAsync(string cs)
    {
        await using var db = PostgresFixture.Context(cs);
        return await db.Set<ScheduleOccurrence>()
            .AsNoTracking()
            .Where(o => o.ScheduleId == ScheduleId)
            .OrderBy(o => o.StartUtc)
            .ToListAsync();
    }

    // ---- the rota ------------------------------------------------------------------------------

    private static SaveSchedulePlanRequest StoredRota => new()
    {
        Rotations =
        [
            PlanRotation(AliceRotationId, "alice", AliceHandover, isPrimary: true, order: 1),
            PlanRotation(BobRotationId, "bob", BobHandover, isPrimary: false, order: 2)
        ]
    };

    /// <summary>alice and bob trade places: bob takes the shift alice is holding.</summary>
    private static SaveSchedulePlanRequest SwappedRota => new()
    {
        Rotations =
        [
            PlanRotation(BobRotationId, "bob", AliceHandover, isPrimary: true, order: 1),
            PlanRotation(AliceRotationId, "alice", BobHandover, isPrimary: false, order: 2)
        ]
    };

    private static SchedulePlanRotation PlanRotation(
        Guid? id, string userId, LocalDateTime handover, bool isPrimary, int order) => new()
    {
        Id = id,
        UserId = userId,
        HandoverStartLocal = handover,
        ShiftLengthMinutes = 7 * 1440,
        IsPrimary = isPrimary,
        Order = order,
        RecurrenceType = RecurrenceType.Biweekly,
        RecurrenceIntervalDays = 14
    };

    // ---- harness -------------------------------------------------------------------------------

    private static int ScheduleKey => BitConverter.ToInt32(ScheduleId.ToByteArray(), 0);

    private async Task<string> SeededAsync()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        await using var db = PostgresFixture.Context(cs);

        db.Add(new Team { Id = TeamId, Name = "Platform", CreatedAt = DateTime.UtcNow });
        db.Add(new Team { Id = OtherTeamId, Name = "Payments", CreatedAt = DateTime.UtcNow });

        db.Add(new Schedule
        {
            Id = ScheduleId,
            Name = "Primary",
            Description = "Original",
            TeamId = TeamId,
            Timezone = Zone,
            CreatedAt = DateTime.UtcNow
        });

        db.Add(Rotation(AliceRotationId, "alice", AliceHandover, isPrimary: true, order: 1));
        db.Add(Rotation(BobRotationId, "bob", BobHandover, isPrimary: false, order: 2));

        Member(db, TeamId, "alice");
        Member(db, TeamId, "bob");
        Member(db, OtherTeamId, "carol");
        Member(db, OtherTeamId, "dave");

        await db.SaveChangesAsync();
        return cs;
    }

    private static ScheduleRotation Rotation(
        Guid id, string userId, LocalDateTime handover, bool isPrimary, int order) => new()
    {
        Id = id,
        ScheduleId = ScheduleId,
        UserId = userId,
        HandoverStartLocal = handover,
        ShiftLengthMinutes = 7 * 1440,
        IsPrimary = isPrimary,
        Order = order,
        RecurrenceType = RecurrenceType.Biweekly,
        RecurrenceIntervalDays = 14,
        CreatedAt = DateTime.UtcNow
    };

    private static void Member(ApplicationDbContext db, Guid teamId, string userId) =>
        db.Add(new TeamMember
        {
            Id = Guid.NewGuid(),
            TeamId = teamId,
            UserId = userId,
            Role = "Member",
            CreatedAt = DateTime.UtcNow
        });

    private static async Task SaveAsync(string cs, SaveSchedulePlanRequest plan)
    {
        await using var db = PostgresFixture.Context(cs);
        Assert.True(await Service(db).SaveSchedulePlanAsync(ScheduleId, plan));
    }

    /// <summary>Generates the rota the way the daily Quartz job does, before any plan is saved.</summary>
    private static async Task MaterializeAsync(string cs)
    {
        await using var db = PostgresFixture.Context(cs);
        await Materializer(db).RematerializeScheduleAsync(ScheduleId, IScheduleMaterializer.DefaultHorizon);
    }

    /// <summary>The real materializer, on a clock pinned to <see cref="Now"/> so the rota is stable.</summary>
    private static ScheduleMaterializer Materializer(ApplicationDbContext db) =>
        new(db,
            new ScheduleRepository(db, NullLogger<ScheduleRepository>.Instance),
            new ScheduleRotationRepository(db, NullLogger<ScheduleRotationRepository>.Instance),
            new ScheduleOccurrenceRepository(db, NullLogger<ScheduleOccurrenceRepository>.Instance),
            DateTimeZoneProviders.Tzdb,
            new FixedClock(Now),
            NullLogger<ScheduleMaterializer>.Instance);

    private static ScheduleService Service(ApplicationDbContext db, IScheduleMaterializer? materializer = null)
    {
        var tz = DateTimeZoneProviders.Tzdb;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHybridCache();
        var sp = services.BuildServiceProvider();

        return new ScheduleService(
            new ScheduleRepository(db, NullLogger<ScheduleRepository>.Instance),
            new ScheduleOccurrenceRepository(db, NullLogger<ScheduleOccurrenceRepository>.Instance),
            new ScheduleRotationRepository(db, NullLogger<ScheduleRotationRepository>.Instance),
            new EscalationStepRepository(db, NullLogger<EscalationStepRepository>.Instance),
            new TeamMemberRepository(db, NullLogger<TeamMemberRepository>.Instance),
            new TransactionManager(db, NullLogger<TransactionManager>.Instance),
            UserManagerFor("alice", "bob", "carol", "dave"),
            new CreateScheduleRequestValidator(tz),
            new SaveSchedulePlanRequestValidator(tz),
            materializer ?? Materializer(db),
            tz,
            Substitute.For<IOnCallOverrideService>(),
            Substitute.For<IAuditLogService>(),
            sp.GetRequiredService<HybridCache>(),
            NullLogger<ScheduleService>.Instance,
            Substitute.For<IOnCallService>());
    }

    private static UserManager<ApplicationUser> UserManagerFor(params string[] userIds)
    {
        var mgr = Substitute.For<UserManager<ApplicationUser>>(
            Substitute.For<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null);

        foreach (var id in userIds)
            mgr.FindByIdAsync(id).Returns(new ApplicationUser
            {
                Id = id,
                FirstName = id,
                LastName = "Tester",
                Email = $"{id}@example.io"
            });

        return mgr;
    }

    /// <summary>
    /// Records the rota as COMMITTED data — on its own connection, so uncommitted work in the
    /// caller's transaction is invisible to it — at the moment the materializer is asked to run.
    /// </summary>
    private sealed class RotaSpy(IScheduleMaterializer inner, string connectionString) : IScheduleMaterializer
    {
        public List<List<(string UserId, LocalDateTime Handover, int Order)>> Committed { get; } = [];

        public async Task RematerializeScheduleAsync(Guid scheduleId, Duration horizon, CancellationToken cancellationToken = default)
        {
            await using (var db = PostgresFixture.Context(connectionString))
            {
                var rota = await db.Set<ScheduleRotation>()
                    .AsNoTracking()
                    .Where(r => r.ScheduleId == scheduleId && !r.IsDeleted)
                    .OrderBy(r => r.Order)
                    .ToListAsync(cancellationToken);

                Committed.Add(rota.Select(r => (r.UserId, r.HandoverStartLocal, r.Order)).ToList());
            }

            await inner.RematerializeScheduleAsync(scheduleId, horizon, cancellationToken);
        }

        public Task RematerializeAllAsync(Duration horizon, CancellationToken cancellationToken = default) =>
            inner.RematerializeAllAsync(horizon, cancellationToken);
    }
}
