using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Callu.Tests;

/// <summary>
/// The side-effect-free paging-suppression pre-pass: it matches only enabled rules that opted in,
/// fails open on broken action JSON, and never mutates rule trigger statistics.
/// </summary>
public class AlertRuleEngineSuppressionTests
{
    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"b3-{Guid.NewGuid():N}").Options);

    private static AlertRuleEngine Engine(ApplicationDbContext ctx) =>
        new(new Repository<AlertRule>(ctx, NullLogger<Repository<AlertRule>>.Instance),
            Substitute.For<IServiceProvider>(),
            Substitute.For<IIncidentNoteService>(),
            Substitute.For<IAuditLogService>(),
            new ImmediateTransactionManager(),
            NullLogger<AlertRuleEngine>.Instance);

    private static AlertRule Rule(string actionsJson, bool enabled = true, string name = "rule-1") => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        IsEnabled = enabled,
        Priority = 100,
        ConditionsJson = """[{"field":"Severity","operator":"Equals","value":"Critical"}]""",
        ActionsJson = actionsJson,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    private static Incident CriticalIncident() => new()
    {
        Id = Guid.NewGuid(),
        Title = "db down",
        Severity = IncidentSeverity.Critical,
        Status = IncidentStatus.Open,
    };

    [Fact]
    public async Task Matching_SuppressPagingRule_ReturnsRuleName()
    {
        using var ctx = NewContext();
        ctx.Add(Rule("""[{"type":"suppressnotification","suppressPaging":true}]""", name: "quiet"));
        await ctx.SaveChangesAsync();

        var result = await Engine(ctx).ShouldSuppressPagingAsync(CriticalIncident());

        Assert.Equal("quiet", result);
    }

    [Fact]
    public async Task LegacyActionJson_WithoutField_DoesNotSuppress()
    {
        using var ctx = NewContext();
        ctx.Add(Rule("""[{"type":"suppressnotification"}]"""));
        await ctx.SaveChangesAsync();

        var result = await Engine(ctx).ShouldSuppressPagingAsync(CriticalIncident());

        Assert.Null(result);
    }

    [Fact]
    public async Task SuppressPagingFalse_DoesNotSuppress()
    {
        using var ctx = NewContext();
        ctx.Add(Rule("""[{"type":"suppressnotification","suppressPaging":false}]"""));
        await ctx.SaveChangesAsync();

        Assert.Null(await Engine(ctx).ShouldSuppressPagingAsync(CriticalIncident()));
    }

    [Fact]
    public async Task NonMatchingConditions_DoesNotSuppress()
    {
        using var ctx = NewContext();
        ctx.Add(Rule("""[{"type":"suppressnotification","suppressPaging":true}]"""));
        await ctx.SaveChangesAsync();

        var lowSeverity = CriticalIncident();
        lowSeverity.Severity = IncidentSeverity.Low;

        Assert.Null(await Engine(ctx).ShouldSuppressPagingAsync(lowSeverity));
    }

    [Fact]
    public async Task DisabledRule_DoesNotSuppress()
    {
        using var ctx = NewContext();
        ctx.Add(Rule("""[{"type":"suppressnotification","suppressPaging":true}]""", enabled: false));
        await ctx.SaveChangesAsync();

        Assert.Null(await Engine(ctx).ShouldSuppressPagingAsync(CriticalIncident()));
    }

    [Fact]
    public async Task BrokenActionsJson_FailsOpen()
    {
        using var ctx = NewContext();
        ctx.Add(Rule("""not json at all — but contains suppressPaging so the pre-filter matches"""));
        await ctx.SaveChangesAsync();

        Assert.Null(await Engine(ctx).ShouldSuppressPagingAsync(CriticalIncident()));
    }

    [Fact]
    public async Task PrePass_DoesNotTouchTriggerStatistics()
    {
        using var ctx = NewContext();
        var rule = Rule("""[{"type":"suppressnotification","suppressPaging":true}]""");
        ctx.Add(rule);
        await ctx.SaveChangesAsync();

        await Engine(ctx).ShouldSuppressPagingAsync(CriticalIncident());

        var reloaded = await ctx.Set<AlertRule>().AsNoTracking().FirstAsync(r => r.Id == rule.Id);
        Assert.Equal(0, reloaded.TriggerCount);
        Assert.Null(reloaded.LastTriggeredAt);
    }
}
