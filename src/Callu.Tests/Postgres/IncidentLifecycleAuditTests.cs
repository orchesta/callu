using Callu.Application.Common.Interfaces;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Messaging;
using Callu.Application.Plugins;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using System.Text.Json.Nodes;
using Callu.Infrastructure.Audit;
using Callu.Infrastructure.DI;
using Callu.Infrastructure.Identity;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Infrastructure.Services;
using Callu.Infrastructure.Telemetry;
using Callu.Shared.Models.Audit;
using Callu.Shared.Models.Incidents;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Callu.Tests;

/// <summary>The four lifecycle rows carry what the incident-management profile requires.</summary>
[Collection(PostgresCollection.Name)]
public class IncidentLifecycleAuditTests(PostgresFixture pg)
{
    private static async Task<Guid> CreateAsync(World world, IncidentSeverity severity)
    {
        var result = await world.Incidents.CreateIncidentAsync(new CreateIncidentRequest
        {
            Title = "Disk filling up",
            Severity = severity.ToString(),
            TeamId = world.TeamId,
        });
        return result.Incident!.Id;
    }

    [PostgresFact]
    public async Task TheFourGovernedEvents_CarryWhatTheProfileRequires()
    {
        var world = await ArrangeAsync();
        var service = world.Incidents;

        var created = await CreateAsync(world, IncidentSeverity.High);
        await service.ResolveIncidentAsync(created, "user-1", IncidentTransitionNote.FromInput("permanent-fix", "Disk temizlendi."));
        await service.CloseIncidentAsync(created, "user-1", IncidentTransitionNote.FromInput(null, "Kök neden giderildi."));
        await service.ReopenIncidentAsync(created, "user-1", IncidentTransitionNote.FromInput(null, "Aynı hata tekrar görüldü."));

        await using var ctx = PostgresFixture.Context(world.ConnectionString);
        var rows = await ctx.AuditLogs.Where(l => l.ResourceId == created).OrderBy(l => l.CreatedAt).ToListAsync();
        JsonNode Details(string name) => JsonNode.Parse(rows.Single(r => r.EventName == name).Details!)!;

        var create = Details("incident.create");
        Assert.Equal("open", create["metadata"]!["incident"]!["status"]!.GetValue<string>());
        Assert.Equal("high", create["metadata"]!["incident"]!["priority"]!.GetValue<string>());
        Assert.NotNull(create["metadata"]!["incident"]!["detectedAt"]);
        Assert.Equal("team", create["relatedResources"]![0]!["type"]!.GetValue<string>());
        Assert.Equal("create", create["change"]!["type"]!.GetValue<string>());
        Assert.NotNull(create["authorization"]);

        var resolve = Details("incident.resolve");
        Assert.Equal("resolved", resolve["metadata"]!["incident"]!["status"]!.GetValue<string>());
        Assert.Equal("permanent-fix", resolve["metadata"]!["incident"]!["resolutionType"]!.GetValue<string>());
        Assert.Equal("Disk temizlendi.", resolve["reason"]!["text"]!.GetValue<string>());
        Assert.Equal("resolved", resolve["change"]!["after"]!["status"]!.GetValue<string>());

        Assert.Equal("Kök neden giderildi.", Details("incident.close")["reason"]!["text"]!.GetValue<string>());

        var reopen = Details("incident.reopen");
        Assert.Equal("open", reopen["metadata"]!["incident"]!["status"]!.GetValue<string>());
        Assert.Equal("closed", reopen["change"]!["before"]!["status"]!.GetValue<string>());

        // Every incident row, whether written inside the transaction or after it, shares the incident's id.
        Assert.All(rows.Where(r => r.EventName.StartsWith("incident.", StringComparison.Ordinal) && r.Action != AuditAction.EscalationNobodyReached),
            r => Assert.Equal(AuditCorrelationScope.ForIncident(created), r.CorrelationId));
    }

    [PostgresFact]
    public async Task ACreateRow_NoLongerCarriesTheWholeRequest()
    {
        var world = await ArrangeAsync();

        var created = await CreateAsync(world, IncidentSeverity.Low);

        await using var ctx = PostgresFixture.Context(world.ConnectionString);
        var row = await ctx.AuditLogs.SingleAsync(l => l.ResourceId == created && l.EventName == "incident.create");
        Assert.DoesNotContain("Disk filling up", row.ChangeAfter ?? string.Empty, StringComparison.Ordinal);
    }

    [PostgresFact]
    public async Task ATransitionThroughTheGenericUpdate_CarriesTheSameDetails()
    {
        var world = await ArrangeAsync();

        await world.Incidents.UpdateIncidentAsync(world.IncidentId, new UpdateIncidentRequest { Status = "Resolved" });

        await using var ctx = PostgresFixture.Context(world.ConnectionString);
        var row = await ctx.AuditLogs.SingleAsync(l => l.Action == AuditAction.Resolved);
        var details = JsonNode.Parse(row.Details!)!;
        Assert.Equal("resolved", details["metadata"]!["incident"]!["status"]!.GetValue<string>());
        Assert.Equal("Status: Open", row.ChangeBefore);
    }

    private static IValidator<CreateIncidentRequest> PassingValidator()
    {
        var validator = Substitute.For<IValidator<CreateIncidentRequest>>();
        validator.ValidateAsync(Arg.Any<CreateIncidentRequest>(), Arg.Any<CancellationToken>())
            .Returns(new FluentValidation.Results.ValidationResult());
        return validator;
    }

    private sealed record World(string ConnectionString, Guid IncidentId, Guid TeamId, IIncidentService Incidents);

    private async Task<World> ArrangeAsync()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        Guid incidentId;
        Guid teamId;
        await using (var db = PostgresFixture.Context(cs))
        {
            var incident = new Incident
            {
                Title = "Checkout failing",
                Severity = IncidentSeverity.Critical,
                Status = IncidentStatus.Open,
                StartedAt = DateTime.UtcNow,
            };
            var team = new Team { Name = "Payments" };
            db.Add(incident);
            db.Add(team);
            await db.SaveChangesAsync();
            incidentId = incident.Id;
            teamId = team.Id;
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<ICurrentUserService>());
        services.AddPersistenceModule(cs, enableParameterLogging: false);
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>();

        var provider = services.BuildServiceProvider();
        var sp = provider.CreateScope().ServiceProvider;

        var incidents = new IncidentService(
            sp.GetRequiredService<IIncidentRepository>(),
            sp.GetRequiredService<IIncidentTimelineEventRepository>(),
            sp.GetRequiredService<IEscalationPolicyRepository>(),
            Substitute.For<ITeamMemberRepository>(),
            sp.GetRequiredService<ICallLogRepository>(),
            Substitute.For<IRepository<WebhookDelivery>>(),
            sp.GetRequiredService<IRepository<ConferenceRoom>>(),
            sp.GetRequiredService<ITransactionManager>(),
            sp.GetRequiredService<UserManager<ApplicationUser>>(),
            PassingValidator(),
            Substitute.For<IIncidentEventDispatcher>(),
            Substitute.For<IEscalationOrchestrator>(),
            Substitute.For<IEscalationWorkflowSignal>(),
            Substitute.For<IAlertRuleEngine>(),
            sp.GetRequiredService<IAuditLogService>(),
            Substitute.For<ICurrentUserService>(),
            Substitute.For<IServiceRepository>(),
            Substitute.For<INotificationChannelService>(),
            Substitute.For<IMaintenanceWindowService>(),
            new CalluMetrics(new FakeMeterFactory()),
            NullLogger<IncidentService>.Instance);

        return new World(cs, incidentId, teamId, incidents);
    }
}
