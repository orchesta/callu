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

/// <summary>Pins that every lifecycle mutation hands its event to the ACK dispatcher, after commit.</summary>
[Collection(PostgresCollection.Name)]
public class IncidentLifecycleEmissionTests(PostgresFixture pg)
{
    [PostgresFact]
    public async Task CreatingAnIncident_EmitsCreated()
    {
        var world = await ArrangeAsync();

        var result = await world.Incidents.CreateIncidentAsync(new CreateIncidentRequest
        {
            Title = "Disk filling up",
            Severity = nameof(IncidentSeverity.High),
        });

        await world.Dispatcher.Received(1)
            .SendServiceAckAsync(result.Incident!.Id, "created", Arg.Any<CancellationToken>());
    }

    /// <summary>The maintenance auto-ack used to bypass the dispatcher entirely, leaving the alert source unaware.</summary>
    [PostgresFact]
    public async Task AMaintenanceAutoAcknowledgedCreate_AlsoEmitsAcknowledge()
    {
        var world = await ArrangeAsync(maintenanceMode: nameof(MaintenanceWindowMode.AutoAcknowledge));

        var result = await world.Incidents.CreateIncidentAsync(new CreateIncidentRequest
        {
            Title = "Disk filling up",
            Severity = nameof(IncidentSeverity.High),
            ServiceId = world.ServiceId,
        });

        await world.Dispatcher.Received(1)
            .SendServiceAckAsync(result.Incident!.Id, "created", Arg.Any<CancellationToken>());
        await world.Dispatcher.Received(1)
            .SendServiceAckAsync(result.Incident!.Id, "acknowledge", Arg.Any<CancellationToken>());
    }

    [PostgresFact]
    public async Task ClosingAnIncident_EmitsClosed()
    {
        var world = await ArrangeAsync(IncidentStatus.Resolved);

        await world.Incidents.CloseIncidentAsync(world.IncidentId, "user-1");

        await world.Dispatcher.Received(1)
            .SendServiceAckAsync(world.IncidentId, "closed", Arg.Any<CancellationToken>());
    }

    /// <summary>By dispatch time the transition is already committed: a fresh connection sees it.</summary>
    [PostgresFact]
    public async Task TheEmission_HappensAfterTheCommit()
    {
        var world = await ArrangeAsync(IncidentStatus.Resolved);

        IncidentStatus? statusAtDispatch = null;
        world.Dispatcher
            .SendServiceAckAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(AckDispatchOutcome.Recorded)
            .AndDoes(_ =>
            {
                using var db = PostgresFixture.Context(world.ConnectionString);
                statusAtDispatch = db.Incidents.AsNoTracking().Single(i => i.Id == world.IncidentId).Status;
            });

        await world.Incidents.CloseIncidentAsync(world.IncidentId, "user-1");

        Assert.Equal(IncidentStatus.Closed, statusAtDispatch);
    }

    [PostgresFact]
    public async Task ReopeningAnIncident_EmitsReopened()
    {
        var world = await ArrangeAsync(IncidentStatus.Resolved);

        await world.Incidents.ReopenIncidentAsync(world.IncidentId, "user-1");

        await world.Dispatcher.Received(1)
            .SendServiceAckAsync(world.IncidentId, "reopened", Arg.Any<CancellationToken>());
    }

    /// <summary>The generic PATCH can acknowledge too, and used to tell nobody.</summary>
    [PostgresFact]
    public async Task AnAcknowledgeDrivenThroughUpdate_EmitsAcknowledge()
    {
        var world = await ArrangeAsync();

        await world.Incidents.UpdateIncidentAsync(world.IncidentId, new UpdateIncidentRequest { Status = "Acknowledged" });

        await world.Dispatcher.Received(1)
            .SendServiceAckAsync(world.IncidentId, "acknowledge", Arg.Any<CancellationToken>());
    }

    /// <summary>Investigating straight from Open acknowledges the incident, and that too is an event.</summary>
    [PostgresFact]
    public async Task InvestigatingFromOpen_EmitsAcknowledge()
    {
        var world = await ArrangeAsync();

        await world.Incidents.UpdateIncidentAsync(world.IncidentId, new UpdateIncidentRequest { Status = "Investigating" });

        await world.Dispatcher.Received(1)
            .SendServiceAckAsync(world.IncidentId, "acknowledge", Arg.Any<CancellationToken>());
    }

    [PostgresFact]
    public async Task ATransitionWithNoEventOfItsOwn_EmitsNothing()
    {
        var world = await ArrangeAsync(IncidentStatus.Acknowledged);

        await world.Incidents.UpdateIncidentAsync(world.IncidentId, new UpdateIncidentRequest { Status = "Mitigated" });

        await world.Dispatcher.DidNotReceiveWithAnyArgs()
            .SendServiceAckAsync(default, default!, default);
    }

    [PostgresFact]
    public async Task AnUpdateThatChangesNoStatus_EmitsNothing()
    {
        var world = await ArrangeAsync();

        await world.Incidents.UpdateIncidentAsync(world.IncidentId, new UpdateIncidentRequest { Title = "Renamed" });

        await world.Dispatcher.DidNotReceiveWithAnyArgs()
            .SendServiceAckAsync(default, default!, default);
    }

    /// <summary>Reassigning an open incident acknowledges it as a side effect, and that too is an event.</summary>
    [PostgresFact]
    public async Task AReassignThatAcknowledges_EmitsAcknowledge()
    {
        var world = await ArrangeAsync();

        await world.Incidents.ReassignIncidentAsync(world.IncidentId, "user-2", "admin-1");

        await world.Dispatcher.Received(1)
            .SendServiceAckAsync(world.IncidentId, "acknowledge", Arg.Any<CancellationToken>());
    }

    [PostgresFact]
    public async Task AReassignOnAnAlreadyAcknowledgedIncident_EmitsNothing()
    {
        var world = await ArrangeAsync(IncidentStatus.Acknowledged);

        await world.Incidents.ReassignIncidentAsync(world.IncidentId, "user-2", "admin-1");

        await world.Dispatcher.DidNotReceiveWithAnyArgs()
            .SendServiceAckAsync(default, default!, default);
    }

    // ---- harness -----------------------------------------------------------

    private static IValidator<CreateIncidentRequest> PassingValidator()
    {
        var validator = Substitute.For<IValidator<CreateIncidentRequest>>();
        validator.ValidateAsync(Arg.Any<CreateIncidentRequest>(), Arg.Any<CancellationToken>())
            .Returns(new FluentValidation.Results.ValidationResult());
        return validator;
    }

    private sealed record World(string ConnectionString, Guid IncidentId, Guid ServiceId, IIncidentService Incidents, IIncidentEventDispatcher Dispatcher);

    private async Task<World> ArrangeAsync(
        IncidentStatus seededStatus = IncidentStatus.Open,
        string? maintenanceMode = null)
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        Guid incidentId;
        Guid serviceId;
        await using (var db = PostgresFixture.Context(cs))
        {
            var service = new Service { Name = "checkout", CreatedAt = DateTime.UtcNow };
            db.Add(service);

            var incident = new Incident
            {
                Title = "Checkout failing",
                Severity = IncidentSeverity.Critical,
                Status = seededStatus,
                StartedAt = DateTime.UtcNow,
            };
            db.Add(incident);
            await db.SaveChangesAsync();
            incidentId = incident.Id;
            serviceId = service.Id;
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

        var dispatcher = Substitute.For<IIncidentEventDispatcher>();

        var maintenance = Substitute.For<IMaintenanceWindowService>();
        maintenance.GetMaintenanceModeForServiceAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(maintenanceMode);

        var incidents = new IncidentService(
            sp.GetRequiredService<IIncidentRepository>(),
            sp.GetRequiredService<IIncidentTimelineEventRepository>(),
            sp.GetRequiredService<IEscalationPolicyRepository>(),
            Substitute.For<ITeamMemberRepository>(),
            sp.GetRequiredService<ICallLogRepository>(),
            sp.GetRequiredService<IRepository<WebhookDelivery>>(),
            sp.GetRequiredService<IRepository<ConferenceRoom>>(),
            sp.GetRequiredService<ITransactionManager>(),
            sp.GetRequiredService<UserManager<ApplicationUser>>(),
            PassingValidator(),
            dispatcher,
            Substitute.For<IEscalationOrchestrator>(),
            Substitute.For<IEscalationWorkflowSignal>(),
            Substitute.For<IAlertRuleEngine>(),
            sp.GetRequiredService<IAuditLogService>(),
            Substitute.For<ICurrentUserService>(),
            sp.GetRequiredService<IServiceRepository>(),
            Substitute.For<INotificationChannelService>(),
            maintenance,
            new CalluMetrics(new FakeMeterFactory()),
            NullLogger<IncidentService>.Instance);

        return new World(cs, incidentId, serviceId, incidents, dispatcher);
    }
}
