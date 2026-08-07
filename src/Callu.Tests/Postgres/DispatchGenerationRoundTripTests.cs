using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Services;
using Callu.Shared.Models.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Callu.Tests;

/// <summary>The dedupe generation must survive a PostgreSQL round trip unchanged.</summary>
[Collection(PostgresCollection.Name)]
public class DispatchGenerationRoundTripTests(PostgresFixture pg)
{
    /// <summary>A timestamp with sub-microsecond ticks — what DateTime.UtcNow actually hands you.</summary>
    private static DateTime WithSubMicrosecondTicks() =>
        new DateTime(2026, 7, 13, 4, 5, 6, DateTimeKind.Utc).AddTicks(1234567);

    [PostgresFact]
    public async Task PostgresReallyDoesDropSubMicrosecondTicks()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var inMemory = WithSubMicrosecondTicks();
        var incidentId = await SeedAsync(cs, inMemory);

        var reRead = await ReadEscalationStartedAtAsync(cs, incidentId);

        // Guard the premise the truncation exists for. If this ever stops being true, the
        // truncation is dead code and should go — but until then it is load-bearing.
        Assert.NotNull(reRead);
        var reReadInstant = reRead.GetValueOrDefault();
        Assert.NotEqual(inMemory.Ticks, reReadInstant.Ticks);
        Assert.Equal(0, reReadInstant.Ticks % TimeSpan.TicksPerMicrosecond);
    }

    /// <summary>
    /// The invariant: the same escalation run yields the same generation whether it is computed from
    /// the entity still in memory or from the row read back out of PostgreSQL.
    /// </summary>
    [PostgresFact]
    public async Task TheGeneration_SurvivesTheRoundTripToPostgres()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var inMemory = WithSubMicrosecondTicks();
        var incidentId = await SeedAsync(cs, inMemory);

        var reRead = await ReadEscalationStartedAtAsync(cs, incidentId);

        Assert.Equal(
            EscalationOrchestrator.ComputeDispatchGeneration(inMemory),
            EscalationOrchestrator.ComputeDispatchGeneration(reRead));
    }

    /// <summary>A redispatch after a reload lands on the same dedupe key, so the claim dedupes it away.</summary>
    [PostgresFact]
    public async Task ARedispatchAfterAReload_ProducesTheSameDedupeKey()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var inMemory = WithSubMicrosecondTicks();
        var incidentId = await SeedAsync(cs, inMemory);
        var reRead = await ReadEscalationStartedAtAsync(cs, incidentId);

        string Key(DateTime? escalationStartedAt)
        {
            var payload = new NotificationPayload
            {
                IncidentId = incidentId,
                Title = "Checkout failing",
                Severity = "Critical",
                EventType = NotificationEventType.EscalationStep,
                EscalationLevel = 1,
                DispatchGeneration = NotificationPayload.GenerationFor(escalationStartedAt)
            };

            return NotificationFactory.ComputeDedupeKey(
                "responder-1", payload, NotificationType.VoiceCall, payload.DispatchGeneration);
        }

        Assert.Equal(Key(inMemory), Key(reRead));
    }

    private static async Task<Guid> SeedAsync(string connectionString, DateTime escalationStartedAt)
    {
        await using var db = PostgresFixture.Context(connectionString);

        var incident = new Incident
        {
            Title = "Checkout failing",
            Severity = IncidentSeverity.Critical,
            Status = IncidentStatus.Open,
            StartedAt = DateTime.UtcNow,
            IsEscalationActive = true,
            EscalationStartedAt = escalationStartedAt
        };

        db.Incidents.Add(incident);
        await db.SaveChangesAsync();

        return incident.Id;
    }

    private static async Task<DateTime?> ReadEscalationStartedAtAsync(string connectionString, Guid incidentId)
    {
        await using var db = PostgresFixture.Context(connectionString);

        return await db.Incidents
            .AsNoTracking()
            .Where(i => i.Id == incidentId)
            .Select(i => i.EscalationStartedAt)
            .SingleAsync();
    }
}
