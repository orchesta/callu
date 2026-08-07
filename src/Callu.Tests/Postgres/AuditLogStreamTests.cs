using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Infrastructure.Services;
using Callu.Shared.Models.Audit;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Callu.Tests;

/// <summary>The export walks a keyset cursor, so a mistake there drops or repeats audited rows.</summary>
// The batch boundary is where that goes wrong, and only a real database orders it the same way.
[Collection(PostgresCollection.Name)]
public class AuditLogStreamTests(PostgresFixture fixture)
{
    private static readonly DateTime Base = new(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);

    private static AuditLog Row(DateTime at, AuditAction action = AuditAction.Login,
        string entityType = "User", Guid? entityId = null) => new()
    {
        Id = Guid.NewGuid(),
        CreatedAt = at,
        UpdatedAt = at,
        ActorId = "u1",
        ActorDisplayName = "Ali",
        Action = action,
        ResourceType = entityType,
        ResourceId = entityId ?? Guid.NewGuid(),
    };

    private async Task<(AuditLogService Sut, ApplicationDbContext Ctx)> SeedAsync(params AuditLog[] rows)
    {
        var cs = await fixture.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var ctx = PostgresFixture.Context(cs);
        ctx.AddRange(rows);
        await ctx.SaveChangesAsync();

        var sut = new AuditLogService(
            new AuditLogRepository(ctx, NullLogger<AuditLogRepository>.Instance),
            Substitute.For<IHttpContextAccessor>(),
            new TransactionManager(ctx, NullLogger<TransactionManager>.Instance));

        return (sut, ctx);
    }

    private static async Task<List<AuditLogDto>> DrainAsync(AuditLogService sut, AuditLogFilter filter)
    {
        var seen = new List<AuditLogDto>();
        await foreach (var row in sut.StreamAsync(filter))
            seen.Add(row);
        return seen;
    }

    /// <summary>More rows than one batch, so the cursor has to carry the reader across the seam.</summary>
    [PostgresFact]
    public async Task EveryRowComesOutExactlyOnce_AcrossBatchBoundaries()
    {
        var rows = Enumerable.Range(0, 1200)
            .Select(i => Row(Base.AddSeconds(-i)))
            .ToArray();
        var (sut, ctx) = await SeedAsync(rows);
        await using var _ctx = ctx;

        var seen = await DrainAsync(sut, new AuditLogFilter());

        Assert.Equal(1200, seen.Count);
        Assert.Equal(1200, seen.Select(r => r.Id).Distinct().Count());
    }

    /// <summary>Rows sharing a timestamp are exactly where a timestamp-only cursor loops or skips.</summary>
    [PostgresFact]
    public async Task RowsSharingATimestamp_AreNotLostOrRepeated()
    {
        var rows = Enumerable.Range(0, 1100)
            .Select(_ => Row(Base))
            .ToArray();
        var (sut, ctx) = await SeedAsync(rows);
        await using var _ctx = ctx;

        var seen = await DrainAsync(sut, new AuditLogFilter());

        Assert.Equal(1100, seen.Count);
        Assert.Equal(1100, seen.Select(r => r.Id).Distinct().Count());
    }

    [PostgresFact]
    public async Task TheStreamComesOutNewestFirst()
    {
        var (sut, ctx) = await SeedAsync(
            Row(Base.AddMinutes(-10)),
            Row(Base),
            Row(Base.AddMinutes(-5)));
        await using var _ctx = ctx;

        var seen = await DrainAsync(sut, new AuditLogFilter());

        Assert.Equal(seen.OrderByDescending(r => r.CreatedAt).Select(r => r.Id), seen.Select(r => r.Id));
    }

    [PostgresFact]
    public async Task TheStreamHonoursTheSameFiltersAsTheScreen()
    {
        var (sut, ctx) = await SeedAsync(
            Row(Base, AuditAction.Login),
            Row(Base, AuditAction.LoginFailed),
            Row(Base.AddDays(-30), AuditAction.Login));
        await using var _ctx = ctx;

        var byAction = await DrainAsync(sut, new AuditLogFilter { Action = AuditAction.LoginFailed });
        var byRange = await DrainAsync(sut, new AuditLogFilter { From = Base.AddHours(-1) });

        Assert.Single(byAction);
        Assert.Equal(2, byRange.Count);
    }

    [PostgresFact]
    public async Task TheStreamHonoursTheTypeList_AndAnEmptyOneDoesNotFilter()
    {
        var incident = Guid.NewGuid();
        var (sut, ctx) = await SeedAsync(
            Row(Base, AuditAction.Created, "Incident", incident),
            Row(Base, AuditAction.EscalationNobodyReached, "Escalation", incident),
            Row(Base, AuditAction.Updated, "NotificationChannel", incident),
            Row(Base, AuditAction.Deleted, "Schedule", incident));
        await using var _ctx = ctx;

        var scoped = await DrainAsync(sut, new AuditLogFilter
        {
            ResourceTypes = ["Incident", "Escalation", "NotificationChannel"],
            ResourceId = incident,
        });
        var empty = await DrainAsync(sut, new AuditLogFilter { ResourceTypes = [] });

        Assert.Equal(3, scoped.Count);
        Assert.DoesNotContain(scoped, r => r.ResourceType == "Schedule");
        Assert.Equal(4, empty.Count);
    }

    /// <summary>Ascending, across several batches, every row still comes out once and in order.</summary>
    // The cursor comparison has to turn around with the sort: left at "<" while the order is
    // ascending it re-reads rows it already yielded and stops early.
    [PostgresFact]
    public async Task AnAscendingStreamStaysCompleteAndInOrder_AcrossBatchBoundaries()
    {
        var rows = Enumerable.Range(0, 1200)
            .Select(i => Row(Base.AddSeconds(i)))
            .ToArray();
        var (sut, ctx) = await SeedAsync(rows);
        await using var _ctx = ctx;

        var seen = await DrainAsync(sut, new AuditLogFilter { SortAscending = true });

        Assert.Equal(1200, seen.Count);
        Assert.Equal(1200, seen.Select(r => r.Id).Distinct().Count());
        Assert.Equal(Base, seen[0].CreatedAt);

        for (var i = 1; i < seen.Count; i++)
            Assert.True(seen[i].CreatedAt > seen[i - 1].CreatedAt, $"row {i} did not move forward in time");
    }

    /// <summary>Ascending rows sharing a timestamp: the id half of the cursor has to turn around too.</summary>
    [PostgresFact]
    public async Task AnAscendingStream_DoesNotLoseOrRepeatRowsSharingATimestamp()
    {
        var rows = Enumerable.Range(0, 1100)
            .Select(_ => Row(Base))
            .ToArray();
        var (sut, ctx) = await SeedAsync(rows);
        await using var _ctx = ctx;

        var seen = await DrainAsync(sut, new AuditLogFilter { SortAscending = true });

        Assert.Equal(1100, seen.Count);
        Assert.Equal(1100, seen.Select(r => r.Id).Distinct().Count());
    }

    [PostgresFact]
    public async Task AnEmptyRangeEndsInsteadOfLooping()
    {
        var (sut, ctx) = await SeedAsync(Row(Base));
        await using var _ctx = ctx;

        var seen = await DrainAsync(sut, new AuditLogFilter { From = Base.AddYears(1) });

        Assert.Empty(seen);
    }
}
