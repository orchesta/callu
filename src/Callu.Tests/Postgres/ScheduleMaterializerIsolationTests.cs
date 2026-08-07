using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Identity;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NodaTime;
using Npgsql;

namespace Callu.Tests;

/// <summary>One schedule failing to materialize must cost exactly one schedule, on a shared DbContext.</summary>
// The context is scoped, so entities left staged by a failure are what the next schedule's save flushes.
[Collection(PostgresCollection.Name)]
public class ScheduleMaterializerIsolationTests(PostgresFixture pg)
{
    private static readonly Duration Horizon = Duration.FromDays(7);

    /// <summary>After a schedule fails, the shared change tracker holds nothing of it.</summary>
    [PostgresFact]
    public async Task AFailedSchedule_LeavesNothingOfItselfInTheSharedChangeTracker()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var world = await SeedTwoSchedulesAsync(cs);
        await RejectOccurrencesForAsync(cs, world.Poisoned);

        // ONE context, as the scope hands to every schedule in the loop.
        await using var db = PostgresFixture.Context(cs);
        var materializer = Materializer(db);

        var failure = await Assert.ThrowsAsync<DbUpdateException>(
            () => materializer.RematerializeScheduleAsync(world.Poisoned, Horizon));

        // Guard the premise, or this test passes for the wrong reason: the tracker is trivially empty
        // if the schedule failed BEFORE staging anything. It has to be the INSERT of the occurrences
        // that failed — that is the only failure that leaves entities behind to leak.
        Assert.Equal(
            "no_occurrences_for_this_schedule",
            Assert.IsType<PostgresException>(failure.InnerException).ConstraintName);

        Assert.Empty(db.ChangeTracker.Entries<ScheduleOccurrence>());
    }

    /// <summary>The next schedule on the same scope gets its own rota, and actually gets one.</summary>
    [PostgresFact]
    public async Task AFailedSchedule_DoesNotPoisonTheNextOne_OnTheSameScope()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var world = await SeedTwoSchedulesAsync(cs);
        await RejectOccurrencesForAsync(cs, world.Poisoned);

        await using var db = PostgresFixture.Context(cs);
        var materializer = Materializer(db);

        // The loop RematerializeAllAsync runs, one schedule at a time, on one shared context.
        await Assert.ThrowsAnyAsync<Exception>(
            () => materializer.RematerializeScheduleAsync(world.Poisoned, Horizon));

        await materializer.RematerializeScheduleAsync(world.Healthy, Horizon);

        await using var verify = PostgresFixture.Context(cs);
        var occurrences = await verify.ScheduleOccurrences.AsNoTracking().ToListAsync();

        Assert.NotEmpty(occurrences);
        Assert.All(occurrences, o => Assert.Equal(world.Healthy, o.ScheduleId));
        Assert.All(occurrences, o => Assert.Equal("beth", o.UserId));
    }

    /// <summary>The nightly job extends every healthy schedule despite one that cannot be written.</summary>
    // Loop order is not guaranteed, so the assertion is on the outcome rather than on which ran first.
    [PostgresFact]
    public async Task TheNightlyJob_ExtendsEveryHealthySchedule_DespiteOneThatCannotBeWritten()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var world = await SeedTwoSchedulesAsync(cs);
        await RejectOccurrencesForAsync(cs, world.Poisoned);

        await using var db = PostgresFixture.Context(cs);
        await Materializer(db).RematerializeAllAsync(Horizon);   // swallows the one failure, by design

        await using var verify = PostgresFixture.Context(cs);

        var healthy = await verify.ScheduleOccurrences.AsNoTracking()
            .Where(o => o.ScheduleId == world.Healthy)
            .ToListAsync();
        var poisoned = await verify.ScheduleOccurrences.AsNoTracking()
            .Where(o => o.ScheduleId == world.Poisoned)
            .ToListAsync();

        Assert.NotEmpty(healthy);
        Assert.Empty(poisoned);
    }

    /// <summary>A schedule that failed keeps its recovery flag, so the next run picks it up again.</summary>
    [PostgresFact]
    public async Task AFailedSchedule_KeepsItsRecoveryFlag_WhileTheHealthyOneHasItsCleared()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var world = await SeedTwoSchedulesAsync(cs, flagged: true);
        await RejectOccurrencesForAsync(cs, world.Poisoned);

        await using var db = PostgresFixture.Context(cs);
        await Materializer(db).RematerializeAllAsync(Horizon);

        await using var verify = PostgresFixture.Context(cs);
        var schedules = await verify.Schedules.AsNoTracking().ToDictionaryAsync(s => s.Id);

        Assert.NotNull(schedules[world.Poisoned].NeedsRematerializeSince);
        Assert.Null(schedules[world.Healthy].NeedsRematerializeSince);
    }

    // ---- harness ------------------------------------------------------------

    private sealed record World(Guid Poisoned, Guid Healthy);

    /// <summary>The real materializer over one context, as the production scope hands it out.</summary>
    // A fresh context per schedule would be testing an isolation the product does not have.
    private static ScheduleMaterializer Materializer(ApplicationDbContext db) =>
        new(db,
            new ScheduleRepository(db, NullLogger<ScheduleRepository>.Instance),
            new ScheduleRotationRepository(db, NullLogger<ScheduleRotationRepository>.Instance),
            new ScheduleOccurrenceRepository(db, NullLogger<ScheduleOccurrenceRepository>.Instance),
            DateTimeZoneProviders.Tzdb,
            SystemClock.Instance,
            NullLogger<ScheduleMaterializer>.Instance);

    /// <summary>Makes every occurrence write for one schedule fail from inside the database.</summary>
    // The specific error does not matter; what the catch has to survive is the state it leaves staged.
    private static async Task RejectOccurrencesForAsync(string connectionString, Guid scheduleId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var alter = new NpgsqlCommand(
            $"""
             ALTER TABLE "ScheduleOccurrences"
             ADD CONSTRAINT no_occurrences_for_this_schedule
             CHECK ("ScheduleId" <> '{scheduleId}')
             """, connection);

        await alter.ExecuteNonQueryAsync();
    }

    private static async Task<World> SeedTwoSchedulesAsync(string connectionString, bool flagged = false)
    {
        await using var db = PostgresFixture.Context(connectionString);

        db.Users.Add(new ApplicationUser { Id = "alice", UserName = "alice", Email = "alice@example.io" });
        db.Users.Add(new ApplicationUser { Id = "beth", UserName = "beth", Email = "beth@example.io" });

        var team = new Team { Name = "Payments" };
        db.Add(team);

        var poisoned = Schedule(team, "Rota A", "alice", flagged);
        var healthy = Schedule(team, "Rota B", "beth", flagged);

        db.AddRange(poisoned, healthy);
        await db.SaveChangesAsync();

        return new World(poisoned.Id, healthy.Id);
    }

    private static Schedule Schedule(Team team, string name, string userId, bool flagged) => new()
    {
        Name = name,
        Team = team,
        Timezone = "UTC",
        NeedsRematerializeSince = flagged ? DateTime.UtcNow.AddMinutes(-5) : null,
        Rotations =
        {
            new ScheduleRotation
            {
                UserId = userId,
                Order = 0,
                IsPrimary = true,
                RecurrenceType = RecurrenceType.Daily,
                HandoverStartLocal = new LocalDateTime(2026, 7, 1, 9, 0),
                ShiftLengthMinutes = 24 * 60
            }
        }
    };
}
