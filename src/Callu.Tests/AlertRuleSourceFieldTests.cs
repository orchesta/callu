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

public class AlertRuleSourceFieldTests
{
    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"source-{Guid.NewGuid():N}").Options);

    private static AlertRuleEngine Engine(ApplicationDbContext ctx) =>
        new(new Repository<AlertRule>(ctx, NullLogger<Repository<AlertRule>>.Instance),
            Substitute.For<IServiceProvider>(),
            Substitute.For<IIncidentNoteService>(),
            Substitute.For<IAuditLogService>(),
            new ImmediateTransactionManager(),
            NullLogger<AlertRuleEngine>.Instance);

    private static AlertRule SourceRule(Guid value) => new()
    {
        Id = Guid.NewGuid(),
        Name = "source-rule",
        IsEnabled = true,
        Priority = 100,
        ConditionsJson = $$"""[{"field":"Source","operator":"Equals","value":"{{value}}"}]""",
        ActionsJson = """[{"type":"suppressnotification","suppressPaging":true}]""",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    private static Incident Incident(Guid serviceId, Guid integrationId) => new()
    {
        Id = Guid.NewGuid(),
        Title = "db down",
        Severity = IncidentSeverity.Critical,
        Status = IncidentStatus.Open,
        ServiceId = serviceId,
        SourceIntegrationId = integrationId,
    };

    [Fact]
    public async Task Source_matches_the_source_integration()
    {
        var integrationId = Guid.NewGuid();
        using var ctx = NewContext();
        ctx.Add(SourceRule(integrationId));
        await ctx.SaveChangesAsync();

        var result = await Engine(ctx)
            .ShouldSuppressPagingAsync(Incident(Guid.NewGuid(), integrationId));

        Assert.Equal("source-rule", result);
    }

    [Fact]
    public async Task Source_does_not_match_the_service()
    {
        var serviceId = Guid.NewGuid();
        using var ctx = NewContext();
        ctx.Add(SourceRule(serviceId));
        await ctx.SaveChangesAsync();

        var result = await Engine(ctx)
            .ShouldSuppressPagingAsync(Incident(serviceId, Guid.NewGuid()));

        Assert.Null(result);
    }
}
