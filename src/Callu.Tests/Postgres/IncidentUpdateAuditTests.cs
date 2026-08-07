using Callu.Application.Common.Interfaces;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Messaging;
using Callu.Application.Plugins;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.DI;
using Callu.Infrastructure.Identity;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Infrastructure.Services;
using Callu.Infrastructure.Telemetry;
using Callu.Shared.Models.Incidents;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Callu.Tests;

/// <summary>PUT /incidents/{id} can drive every lifecycle transition, and used to record none.</summary>
// No SPA screen calls it, which makes it lower-frequency, not lower-consequence: it is exactly the
// path an automation or a script takes, and the only way to reach Investigating and Mitigated.
[Collection(PostgresCollection.Name)]
public class IncidentUpdateAuditTests(PostgresFixture pg)
{
    [PostgresFact]
    public async Task AStatusChangeThroughUpdate_WritesTheSameActionTheDedicatedEndpointWould()
    {
        var world = await ArrangeAsync();

        await world.Incidents.UpdateIncidentAsync(world.IncidentId, new UpdateIncidentRequest { Status = "Acknowledged" });

        var row = await SingleRowAsync(world.ConnectionString, AuditAction.Acknowledged);

        Assert.Equal(world.IncidentId, row.ResourceId);
        Assert.Equal("Status: Open", row.ChangeBefore);
        Assert.Equal("Status: Acknowledged", row.ChangeAfter);
    }

    /// <summary>The two statuses with no endpoint of their own are still recorded.</summary>
    [PostgresFact]
    public async Task InvestigatingIsRecorded_ThoughItHasNoActionOfItsOwn()
    {
        var world = await ArrangeAsync();

        await world.Incidents.UpdateIncidentAsync(world.IncidentId, new UpdateIncidentRequest { Status = "Investigating" });

        await using var db = PostgresFixture.Context(world.ConnectionString);
        var rows = await db.Set<AuditLog>().AsNoTracking().ToListAsync();

        Assert.Contains(rows, r => (r.ChangeAfter ?? "").Contains("Investigating", StringComparison.Ordinal));
    }

    /// <summary>A transition also has to reach the timeline the responder reads.</summary>
    // Otherwise the auditor-facing record and the responder-facing one disagree about the same act.
    [PostgresFact]
    public async Task AStatusChangeThroughUpdate_AlsoReachesTheTimeline()
    {
        var world = await ArrangeAsync();

        await world.Incidents.UpdateIncidentAsync(world.IncidentId, new UpdateIncidentRequest { Status = "Acknowledged" });

        await using var db = PostgresFixture.Context(world.ConnectionString);
        var events = await db.Set<IncidentTimelineEvent>().AsNoTracking()
            .Where(e => e.IncidentId == world.IncidentId)
            .ToListAsync();

        Assert.Contains(events, e => (e.Description ?? "").Contains("Acknowledged", StringComparison.Ordinal));
    }

    [PostgresFact]
    public async Task ASeverityChange_IsRecordedWithBothValues()
    {
        var world = await ArrangeAsync();

        await world.Incidents.UpdateIncidentAsync(world.IncidentId, new UpdateIncidentRequest { Severity = "Low" });

        var row = await SingleRowAsync(world.ConnectionString, AuditAction.Updated);

        Assert.Contains("Critical", row.ChangeBefore ?? "", StringComparison.Ordinal);
        Assert.Contains("Low", row.ChangeAfter ?? "", StringComparison.Ordinal);
    }

    /// <summary>Only what moved, or the row is a diff of the world and nobody reads it.</summary>
    [PostgresFact]
    public async Task AnUpdateThatChangesNothing_WritesNoRow()
    {
        var world = await ArrangeAsync();

        await world.Incidents.UpdateIncidentAsync(world.IncidentId, new UpdateIncidentRequest { Title = "Checkout failing" });

        await using var db = PostgresFixture.Context(world.ConnectionString);
        Assert.Empty(await db.Set<AuditLog>().AsNoTracking().ToListAsync());
    }

    /// <summary>An incident opened with no team can never page anyone, and used to say nothing.</summary>
    // A misconfiguration and a quiet night look identical from the outside: the incident is there,
    // the timeline is empty, and no page was ever sent.
    [PostgresFact]
    public async Task CreatingAnIncidentWithNoTeam_RecordsThatNobodyCouldBePaged()
    {
        var world = await ArrangeAsync();

        await world.Incidents.CreateIncidentAsync(new CreateIncidentRequest
        {
            Title = "Disk filling up",
            Severity = nameof(IncidentSeverity.High),
        });

        var row = await SingleRowAsync(world.ConnectionString, AuditAction.EscalationNobodyReached);

        Assert.Equal("Incident", row.ResourceType);
        Assert.Contains("no team", row.ChangeAfter ?? "", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The same silence, one step further in: a team with no active policy.</summary>
    [PostgresFact]
    public async Task CreatingAnIncidentForATeamWithNoPolicy_RecordsThatNobodyCouldBePaged()
    {
        var world = await ArrangeAsync();

        Guid teamId;
        await using (var db = PostgresFixture.Context(world.ConnectionString))
        {
            var team = new Team { Name = "Payments" };
            db.Add(team);
            await db.SaveChangesAsync();
            teamId = team.Id;
        }

        await world.Incidents.CreateIncidentAsync(new CreateIncidentRequest
        {
            Title = "Disk filling up",
            Severity = nameof(IncidentSeverity.High),
            TeamId = teamId,
        });

        var row = await SingleRowAsync(world.ConnectionString, AuditAction.EscalationNobodyReached);

        Assert.Contains("policy", row.ChangeAfter ?? "", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>It also has to reach the timeline the responder reads.</summary>
    [PostgresFact]
    public async Task TheSilenceAlsoReachesTheTimeline()
    {
        var world = await ArrangeAsync();

        await world.Incidents.CreateIncidentAsync(new CreateIncidentRequest
        {
            Title = "Disk filling up",
            Severity = nameof(IncidentSeverity.High),
        });

        await using var db = PostgresFixture.Context(world.ConnectionString);
        var events = await db.Set<IncidentTimelineEvent>().AsNoTracking().ToListAsync();

        Assert.Contains(events, e => e.Title == "Nobody was paged");
    }

    private static IValidator<CreateIncidentRequest> PassingValidator()
    {
        var validator = Substitute.For<IValidator<CreateIncidentRequest>>();
        validator.ValidateAsync(Arg.Any<CreateIncidentRequest>(), Arg.Any<CancellationToken>())
            .Returns(new FluentValidation.Results.ValidationResult());
        return validator;
    }

    private static async Task<AuditLog> SingleRowAsync(string connectionString, AuditAction action)
    {
        await using var db = PostgresFixture.Context(connectionString);
        var rows = await db.Set<AuditLog>().AsNoTracking().Where(a => a.Action == action).ToListAsync();

        Assert.True(rows.Count == 1, $"expected one {action} row, found {rows.Count}");
        return rows[0];
    }

    private sealed record World(string ConnectionString, Guid IncidentId, IIncidentService Incidents);

    private async Task<World> ArrangeAsync()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        Guid incidentId;
        await using (var db = PostgresFixture.Context(cs))
        {
            var incident = new Incident
            {
                Title = "Checkout failing",
                Severity = IncidentSeverity.Critical,
                Status = IncidentStatus.Open,
                StartedAt = DateTime.UtcNow,
            };
            db.Add(incident);
            await db.SaveChangesAsync();
            incidentId = incident.Id;
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
            Substitute.For<ICallLogRepository>(),
            Substitute.For<IRepository<WebhookDelivery>>(),
            Substitute.For<IRepository<ConferenceRoom>>(),
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

        return new World(cs, incidentId, incidents);
    }
}
