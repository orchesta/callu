using Callu.Application.Common.Interfaces.Persistence;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Infrastructure.Services;
using Callu.Shared;
using Callu.Shared.Models.Audit;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Callu.Tests;

/// <summary>The read an auditor actually performs: narrow by time, actor and action, then page.</summary>
// ILike and the ordering only behave like this against the real database.
[Collection(PostgresCollection.Name)]
public class AuditLogSearchTests(PostgresFixture fixture)
{
    private static readonly DateTime Base = new(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);

    private static AuditLog Row(DateTime at, AuditAction action, string? userId, string? userName,
        string entityType = "Incident", string? description = null, string? ip = null,
        Guid? entityId = null) => new()
    {
        Id = Guid.NewGuid(),
        CreatedAt = at,
        UpdatedAt = at,
        ActorId = userId,
        ActorDisplayName = userName,
        Action = action,
        ResourceType = entityType,
        ResourceId = entityId ?? Guid.NewGuid(),
        Summary = description,
        RequestIpAddress = ip,
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

    [PostgresFact]
    public async Task ATimeRange_ExcludesWhatFallsOutsideIt()
    {
        var (sut, ctx) = await SeedAsync(
            Row(Base.AddDays(-2), AuditAction.Login, "u1", "Ali"),
            Row(Base, AuditAction.Login, "u1", "Ali"),
            Row(Base.AddDays(2), AuditAction.Login, "u1", "Ali"));
        await using var _ctx = ctx;

        var page = await sut.SearchAsync(new AuditLogFilter
        {
            From = Base.AddHours(-1),
            To = Base.AddHours(1),
        });

        Assert.Equal(1, page.TotalCount);
    }

    [PostgresFact]
    public async Task TheActionFilter_NowSeparatesEventsThatUsedToLookAlike()
    {
        var (sut, ctx) = await SeedAsync(
            Row(Base, AuditAction.EscalationNobodyReached, "u1", "Ali"),
            Row(Base, AuditAction.Updated, "u1", "Ali"),
            Row(Base, AuditAction.LoginFailed, "u2", "Veli"));
        await using var _ctx = ctx;

        var page = await sut.SearchAsync(new AuditLogFilter { Action = AuditAction.EscalationNobodyReached });

        var only = Assert.Single(page.Items);
        Assert.Equal(AuditAction.EscalationNobodyReached, only.Action);
    }

    [PostgresFact]
    public async Task TheActorFilter_NarrowsToOnePerson()
    {
        var (sut, ctx) = await SeedAsync(
            Row(Base, AuditAction.Login, "u1", "Ali"),
            Row(Base, AuditAction.Login, "u2", "Veli"));
        await using var _ctx = ctx;

        var page = await sut.SearchAsync(new AuditLogFilter { ActorId = "u2" });

        Assert.Equal("Veli", Assert.Single(page.Items).ActorDisplayName);
    }

    [PostgresFact]
    public async Task FreeTextMatchesTheActorAndTheDescription_CaseInsensitively()
    {
        var (sut, ctx) = await SeedAsync(
            Row(Base, AuditAction.LoginFailed, null, null, description: "No account for OPERATOR@example.io"),
            Row(Base, AuditAction.Login, "u2", "Veli"));
        await using var _ctx = ctx;

        var byDescription = await sut.SearchAsync(new AuditLogFilter { Query = "operator@" });
        var byActor = await sut.SearchAsync(new AuditLogFilter { Query = "vel" });

        Assert.Equal(1, byDescription.TotalCount);
        Assert.Equal(1, byActor.TotalCount);
    }

    /// <summary>Rows sharing a timestamp must not swap between pages or vanish.</summary>
    [PostgresFact]
    public async Task PagingIsStable_WhenEveryRowSharesATimestamp()
    {
        var rows = Enumerable.Range(0, 25)
            .Select(_ => Row(Base, AuditAction.Login, "u1", "Ali"))
            .ToArray();
        var (sut, ctx) = await SeedAsync(rows);
        await using var _ctx = ctx;

        var first = await sut.SearchAsync(new AuditLogFilter { Page = 1, PageSize = 10 });
        var second = await sut.SearchAsync(new AuditLogFilter { Page = 2, PageSize = 10 });
        var third = await sut.SearchAsync(new AuditLogFilter { Page = 3, PageSize = 10 });

        var seen = first.Items.Concat(second.Items).Concat(third.Items).Select(i => i.Id).ToList();

        Assert.Equal(25, seen.Count);
        Assert.Equal(25, seen.Distinct().Count());
        Assert.Equal(25, first.TotalCount);

        // Every timestamp is identical, so the id is the only thing left to order by. Without the
        // tie-break the sequence is whatever the database felt like and pages overlap or skip.
        Assert.Equal(seen.OrderByDescending(id => id).ToList(), seen);
    }

    /// <summary>An auditor can reach this read, so an unbounded page size is an outage.</summary>
    [PostgresFact]
    public async Task PageSizeIsClamped_AndPageZeroBecomesPageOne()
    {
        var (sut, ctx) = await SeedAsync(Row(Base, AuditAction.Login, "u1", "Ali"));
        await using var _ctx = ctx;

        var huge = await sut.SearchAsync(new AuditLogFilter { PageSize = 100_000 });
        var zero = await sut.SearchAsync(new AuditLogFilter { Page = 0, PageSize = 0 });

        Assert.Equal(AppConstants.Pagination.MaxPageSize, huge.PageSize);
        Assert.Equal(1, zero.Page);
        Assert.Equal(1, zero.PageSize);
    }

    /// <summary>One incident's rows sit under several types, all keyed by the incident id.</summary>
    [PostgresFact]
    public async Task EveryTypeInTheList_ComesBackForTheSameEntityId()
    {
        var incident = Guid.NewGuid();
        var (sut, ctx) = await SeedAsync(
            Row(Base, AuditAction.Created, "u1", "Ali", "Incident", entityId: incident),
            Row(Base, AuditAction.EscalationNobodyReached, "u1", "Ali", "Escalation", entityId: incident),
            Row(Base, AuditAction.Updated, "u1", "Ali", "NotificationChannel", entityId: incident),
            Row(Base, AuditAction.Login, "u1", "Ali", "User"));
        await using var _ctx = ctx;

        var page = await sut.SearchAsync(new AuditLogFilter
        {
            ResourceTypes = ["Incident", "Escalation", "NotificationChannel"],
        });

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(
            ["Escalation", "Incident", "NotificationChannel"],
            page.Items.Select(i => i.ResourceType).OrderBy(t => t, StringComparer.Ordinal));
        Assert.All(page.Items, i => Assert.Equal(incident, i.ResourceId));
    }

    [PostgresFact]
    public async Task ATypeLeftOutOfTheList_StaysOut_EvenOnTheSameEntityId()
    {
        var incident = Guid.NewGuid();
        var (sut, ctx) = await SeedAsync(
            Row(Base, AuditAction.Created, "u1", "Ali", "Incident", entityId: incident),
            Row(Base, AuditAction.EscalationNobodyReached, "u1", "Ali", "Escalation", entityId: incident),
            Row(Base, AuditAction.Updated, "u1", "Ali", "NotificationChannel", entityId: incident),
            Row(Base, AuditAction.Deleted, "u1", "Ali", "Schedule", entityId: incident));
        await using var _ctx = ctx;

        var page = await sut.SearchAsync(new AuditLogFilter
        {
            ResourceTypes = ["Incident", "Escalation", "NotificationChannel"],
            ResourceId = incident,
        });

        Assert.Equal(3, page.TotalCount);
        Assert.DoesNotContain(page.Items, i => i.ResourceType == "Schedule");
    }

    [PostgresFact]
    public async Task TheSingularAndThePluralTypeFilters_NarrowToTheirIntersection()
    {
        var incident = Guid.NewGuid();
        var (sut, ctx) = await SeedAsync(
            Row(Base, AuditAction.Created, "u1", "Ali", "Incident", entityId: incident),
            Row(Base, AuditAction.EscalationNobodyReached, "u1", "Ali", "Escalation", entityId: incident),
            Row(Base, AuditAction.Updated, "u1", "Ali", "NotificationChannel", entityId: incident));
        await using var _ctx = ctx;

        var overlapping = await sut.SearchAsync(new AuditLogFilter
        {
            ResourceType = "Escalation",
            ResourceTypes = ["Incident", "Escalation"],
            ResourceId = incident,
        });

        var disjoint = await sut.SearchAsync(new AuditLogFilter
        {
            ResourceType = "Escalation",
            ResourceTypes = ["Incident", "NotificationChannel"],
            ResourceId = incident,
        });

        Assert.Equal("Escalation", Assert.Single(overlapping.Items).ResourceType);
        Assert.Equal(0, disjoint.TotalCount);
    }

    /// <summary>An empty list has to mean "no type filter"; translated as IN () it would hide everything.</summary>
    [PostgresFact]
    public async Task AnEmptyTypeList_LeavesTheTrailUnfiltered()
    {
        var (sut, ctx) = await SeedAsync(
            Row(Base, AuditAction.Created, "u1", "Ali", "Incident"),
            Row(Base, AuditAction.Login, "u1", "Ali", "User"),
            Row(Base, AuditAction.Updated, "u1", "Ali", "Schedule"));
        await using var _ctx = ctx;

        var empty = await sut.SearchAsync(new AuditLogFilter { ResourceTypes = [] });
        var absent = await sut.SearchAsync(new AuditLogFilter());

        Assert.Equal(3, empty.TotalCount);
        Assert.Equal(absent.TotalCount, empty.TotalCount);
    }

    [PostgresFact]
    public async Task TheNewestRowComesFirst()
    {
        var (sut, ctx) = await SeedAsync(
            Row(Base.AddMinutes(-5), AuditAction.Login, "u1", "eski"),
            Row(Base, AuditAction.Login, "u1", "yeni"));
        await using var _ctx = ctx;

        var page = await sut.SearchAsync(new AuditLogFilter());

        Assert.Equal("yeni", page.Items[0].ActorDisplayName);
    }

    /// <summary>Read as a sequence of events, page one has to be the start of the trail.</summary>
    [PostgresFact]
    public async Task AscendingPutsTheOldestRowFirst_AndTheDefaultStillPutsTheNewest()
    {
        var (sut, ctx) = await SeedAsync(
            Row(Base.AddMinutes(-5), AuditAction.Login, "u1", "eski"),
            Row(Base, AuditAction.Login, "u1", "yeni"));
        await using var _ctx = ctx;

        var ascending = await sut.SearchAsync(new AuditLogFilter { SortAscending = true });
        var descending = await sut.SearchAsync(new AuditLogFilter());

        Assert.Equal("eski", ascending.Items[0].ActorDisplayName);
        Assert.Equal("yeni", descending.Items[0].ActorDisplayName);
    }

    /// <summary>Paging forward through the trail must not repeat a row or step over one.</summary>
    [PostgresFact]
    public async Task AscendingPaging_WalksTheWholeTrailInOrder()
    {
        var rows = Enumerable.Range(0, 25)
            .Select(i => Row(Base.AddMinutes(i), AuditAction.Login, "u1", $"row-{i:00}"))
            .ToArray();
        var (sut, ctx) = await SeedAsync(rows);
        await using var _ctx = ctx;

        var seen = new List<AuditLogDto>();
        for (var page = 1; page <= 3; page++)
        {
            var slice = await sut.SearchAsync(
                new AuditLogFilter { Page = page, PageSize = 10, SortAscending = true });
            seen.AddRange(slice.Items);
        }

        Assert.Equal(25, seen.Count);
        Assert.Equal(25, seen.Select(i => i.Id).Distinct().Count());
        Assert.Equal("row-00", seen[0].ActorDisplayName);
        Assert.Equal("row-24", seen[^1].ActorDisplayName);

        for (var i = 1; i < seen.Count; i++)
            Assert.True(seen[i].CreatedAt > seen[i - 1].CreatedAt, $"row {i} did not move forward in time");
    }

    /// <summary>Ascending page one is the oldest rows, not the newest ones printed backwards.</summary>
    [PostgresFact]
    public async Task AscendingPageOne_HoldsTheOldestRowsAndNoneOfTheNewest()
    {
        var rows = Enumerable.Range(0, 30)
            .Select(i => Row(Base.AddMinutes(i), AuditAction.Login, "u1", $"row-{i:00}"))
            .ToArray();
        var (sut, ctx) = await SeedAsync(rows);
        await using var _ctx = ctx;

        var first = await sut.SearchAsync(
            new AuditLogFilter { Page = 1, PageSize = 10, SortAscending = true });

        Assert.Equal(
            Enumerable.Range(0, 10).Select(i => $"row-{i:00}"),
            first.Items.Select(i => i.ActorDisplayName));
    }
}
