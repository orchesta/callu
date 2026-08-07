using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Quartz;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Quartz;
using NSubstitute;

namespace Callu.Tests;

/// <summary>
/// One concurrency conflict must not take the whole expiry batch with it, and the row most likely to
/// be written concurrently is the room somebody is still in.
/// </summary>
[Collection(PostgresCollection.Name)]
public class ConferenceRoomExpiryTests(PostgresFixture pg)
{
    [PostgresFact]
    public async Task AConflictOnOneRoom_DoesNotKeepTheRestActive()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var contested = await SeedExpiredRoomAsync(cs);
        var quiet = await SeedExpiredRoomAsync(cs);
        var alsoQuiet = await SeedExpiredRoomAsync(cs);

        await using var jobContext = PostgresFixture.Context(cs);

        // Load all three into the job's tracker, then move one from another session so its xmin no
        // longer matches — exactly what a participant leaving mid-sweep does.
        var tracked = await jobContext.ConferenceRooms.ToListAsync();
        Assert.Equal(3, tracked.Count);

        await using (var other = PostgresFixture.Context(cs))
        {
            var room = await other.ConferenceRooms.SingleAsync(r => r.Id == contested);
            room.EndedAt = DateTime.UtcNow;
            await other.SaveChangesAsync();
        }

        await Job(jobContext).Execute(JobContext());

        await using var verify = PostgresFixture.Context(cs);
        var byId = await verify.ConferenceRooms.AsNoTracking().ToDictionaryAsync(r => r.Id);

        Assert.Equal(ConferenceRoomStatus.Expired, byId[quiet].Status);
        Assert.Equal(ConferenceRoomStatus.Expired, byId[alsoQuiet].Status);
        Assert.Equal(ConferenceRoomStatus.Active, byId[contested].Status);
    }

    [PostgresFact]
    public async Task TheSweepIsBounded_SoOneRunCannotLoadTheWholeTable()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        // One room per incident: IX_ConferenceRooms_IncidentId_Active allows only one Active room
        // for an incident, which is also why a real backlog is a backlog of separate incidents.
        const int seeded = 205;
        await using (var seed = PostgresFixture.Context(cs))
        {
            for (var i = 0; i < seeded; i++)
            {
                var incident = NewIncident();
                seed.Incidents.Add(incident);
                seed.ConferenceRooms.Add(ExpiredRoom(incident.Id));
            }
            await seed.SaveChangesAsync();
        }

        await using (var db = PostgresFixture.Context(cs))
            await Job(db).Execute(JobContext());

        await using var verify = PostgresFixture.Context(cs);
        var expired = await verify.ConferenceRooms.CountAsync(r => r.Status == ConferenceRoomStatus.Expired);

        Assert.Equal(200, expired);

        // The remainder is not lost: the next minute's run takes it.
        await using (var db = PostgresFixture.Context(cs))
            await Job(db).Execute(JobContext());

        await using var second = PostgresFixture.Context(cs);
        Assert.Equal(seeded, await second.ConferenceRooms.CountAsync(r => r.Status == ConferenceRoomStatus.Expired));
    }

    // ---- harness ------------------------------------------------------------

    private static ConferenceRoomExpiryQuartzJob Job(ApplicationDbContext db) =>
        new(new SingleContextScopeFactory(db), NullLogger<ConferenceRoomExpiryQuartzJob>.Instance);

    private static IJobExecutionContext JobContext()
    {
        var context = Substitute.For<IJobExecutionContext>();
        context.CancellationToken.Returns(CancellationToken.None);
        return context;
    }

    private static ConferenceRoom ExpiredRoom(Guid incidentId) => new()
    {
        Id = Guid.NewGuid(),
        IncidentId = incidentId,
        RoomToken = Guid.NewGuid().ToString("N"),
        Status = ConferenceRoomStatus.Active,
        ExpiresAt = DateTime.UtcNow.AddMinutes(-5),
        CreatedAt = DateTime.UtcNow.AddHours(-2)
    };

    private static Incident NewIncident() => new()
    {
        Id = Guid.NewGuid(),
        Title = "Checkout latency",
        Status = IncidentStatus.Open,
        Severity = IncidentSeverity.High,
        StartedAt = DateTime.UtcNow.AddHours(-2),
        CreatedAt = DateTime.UtcNow.AddHours(-2)
    };

    private static async Task<Guid> SeedExpiredRoomAsync(string connectionString)
    {
        await using var db = PostgresFixture.Context(connectionString);

        var incident = NewIncident();
        var room = ExpiredRoom(incident.Id);

        db.Incidents.Add(incident);
        db.ConferenceRooms.Add(room);
        await db.SaveChangesAsync();
        return room.Id;
    }

    private sealed class SingleContextScopeFactory(ApplicationDbContext context) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new Scope(context);

        private sealed class Scope(ApplicationDbContext context) : IServiceScope, IServiceProvider
        {
            public IServiceProvider ServiceProvider => this;

            public object? GetService(Type serviceType) =>
                serviceType == typeof(ApplicationDbContext) ? context : null;

            public void Dispose() { }
        }
    }
}
