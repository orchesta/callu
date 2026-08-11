using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Callu.Tests;

/// <summary>One endpoint keeps at most its cap of captures; the oldest go first and neighbours are untouched.</summary>
[Collection(PostgresCollection.Name)]
public class WebhookCaptureCapTests(PostgresFixture pg)
{
    private static WebhookCapture Capture(Guid? serviceId, Guid? integrationId, DateTime capturedAt) => new()
    {
        Id = Guid.NewGuid(),
        ServiceId = serviceId,
        IntegrationId = integrationId,
        CapturedAt = capturedAt,
        Body = "{}",
        Status = WebhookCaptureStatus.Captured,
        CreatedAt = capturedAt
    };

    [PostgresFact]
    public async Task TheOldestRowFalls_WhenAnInsertWouldExceedTheCap()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var integrationId = Guid.NewGuid();
        var otherIntegrationId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using (var db = PostgresFixture.Context(cs))
        {
            db.Integrations.AddRange(
                new Integration { Id = integrationId, Name = "zabbix", CreatedAt = now },
                new Integration { Id = otherIntegrationId, Name = "grafana", CreatedAt = now });
            for (var i = 0; i < WebhookCapture.MaxPerScope; i++)
                db.WebhookCaptures.Add(Capture(null, integrationId, now.AddMinutes(-WebhookCapture.MaxPerScope + i)));
            db.WebhookCaptures.Add(Capture(null, otherIntegrationId, now.AddDays(-30)));
            await db.SaveChangesAsync();
        }

        await using (var db = PostgresFixture.Context(cs))
        {
            var repo = new WebhookCaptureRepository(db, NullLogger<WebhookCaptureRepository>.Instance);

            var trimmed = await repo.TrimScopeForPendingInsertAsync(
                null, integrationId, WebhookCapture.MaxPerScope, maxRows: 2000);
            db.WebhookCaptures.Add(Capture(null, integrationId, now));
            await db.SaveChangesAsync();

            Assert.Equal(1, trimmed);
        }

        await using (var db = PostgresFixture.Context(cs))
        {
            var mine = await db.WebhookCaptures.IgnoreQueryFilters()
                .Where(c => c.IntegrationId == integrationId).ToListAsync();
            Assert.Equal(WebhookCapture.MaxPerScope, mine.Count);
            Assert.DoesNotContain(mine, c => c.CapturedAt <= now.AddMinutes(-WebhookCapture.MaxPerScope + 0.5));
            Assert.Equal(1, await db.WebhookCaptures.IgnoreQueryFilters()
                .CountAsync(c => c.IntegrationId == otherIntegrationId));
        }
    }

    [PostgresFact]
    public async Task ABacklogFarOverTheCap_IsDrainedInBoundedChunks()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var serviceId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        const int backlog = 700;
        const int maxRows = 100;

        await using (var db = PostgresFixture.Context(cs))
        {
            db.Services.Add(new Service { Id = serviceId, Name = "api", CreatedAt = now });
            for (var i = 0; i < backlog; i++)
                db.WebhookCaptures.Add(Capture(serviceId, null, now.AddMinutes(-backlog + i)));
            await db.SaveChangesAsync();
        }

        await using (var db = PostgresFixture.Context(cs))
        {
            var repo = new WebhookCaptureRepository(db, NullLogger<WebhookCaptureRepository>.Instance);
            var trimmed = await repo.TrimScopeForPendingInsertAsync(
                serviceId, null, WebhookCapture.MaxPerScope, maxRows);
            await db.SaveChangesAsync();

            Assert.Equal(maxRows, trimmed);
        }

        await using (var db = PostgresFixture.Context(cs))
        {
            Assert.Equal(backlog - maxRows, await db.WebhookCaptures.IgnoreQueryFilters()
                .CountAsync(c => c.ServiceId == serviceId));
        }
    }

    [PostgresFact]
    public async Task AServiceScopedTrim_DoesNotTouchABoundIntegrationsCaptures()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var serviceId = Guid.NewGuid();
        var integrationId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using (var db = PostgresFixture.Context(cs))
        {
            db.Services.Add(new Service { Id = serviceId, Name = "api", CreatedAt = now });
            db.Integrations.Add(new Integration { Id = integrationId, Name = "zabbix", ServiceId = serviceId, CreatedAt = now });
            for (var i = 0; i < WebhookCapture.MaxPerScope; i++)
                db.WebhookCaptures.Add(Capture(serviceId, null, now.AddMinutes(-1000 + i)));
            db.WebhookCaptures.Add(Capture(serviceId, integrationId, now.AddDays(-30)));
            await db.SaveChangesAsync();
        }

        await using (var db = PostgresFixture.Context(cs))
        {
            var repo = new WebhookCaptureRepository(db, NullLogger<WebhookCaptureRepository>.Instance);
            await repo.TrimScopeForPendingInsertAsync(serviceId, null, WebhookCapture.MaxPerScope, maxRows: 2000);
            await db.SaveChangesAsync();
        }

        await using (var db = PostgresFixture.Context(cs))
        {
            Assert.Equal(1, await db.WebhookCaptures.IgnoreQueryFilters()
                .CountAsync(c => c.IntegrationId == integrationId));
        }
    }
}
