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
using Callu.Shared.Exceptions;
using Callu.Shared.Models.Incidents;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Callu.Tests;

/// <summary>Running a manual action honours the same team scope as every other incident mutation.</summary>
[Collection(PostgresCollection.Name)]
public class ServiceActionScopingTests(PostgresFixture pg)
{
    [PostgresFact]
    public async Task AMemberOfAnotherTeam_CannotExecuteActionsOnAForeignIncident()
    {
        var world = await ArrangeAsync();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            world.Incidents.ExecuteServiceActionAsync(world.ForeignIncidentId, Guid.NewGuid(), "member-1"));

        await world.Dispatcher.DidNotReceiveWithAnyArgs()
            .ExecuteManualActionAsync(default, default, default!, default);
    }

    [PostgresFact]
    public async Task AMemberOfTheOwningTeam_ReachesTheDispatcher()
    {
        var world = await ArrangeAsync();
        var actionId = Guid.NewGuid();

        await world.Incidents.ExecuteServiceActionAsync(world.OwnIncidentId, actionId, "member-1");

        await world.Dispatcher.Received(1)
            .ExecuteManualActionAsync(world.OwnIncidentId, actionId, "member-1", Arg.Any<CancellationToken>());
    }

    // ---- harness -----------------------------------------------------------

    private static IValidator<CreateIncidentRequest> PassingValidator()
    {
        var validator = Substitute.For<IValidator<CreateIncidentRequest>>();
        validator.ValidateAsync(Arg.Any<CreateIncidentRequest>(), Arg.Any<CancellationToken>())
            .Returns(new FluentValidation.Results.ValidationResult());
        return validator;
    }

    private sealed record World(Guid OwnIncidentId, Guid ForeignIncidentId, IIncidentService Incidents, IIncidentEventDispatcher Dispatcher);

    private async Task<World> ArrangeAsync()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        Guid ownIncidentId, foreignIncidentId;
        await using (var db = PostgresFixture.Context(cs))
        {
            var ownTeam = new Team { Name = "Payments" };
            var foreignTeam = new Team { Name = "Network" };
            db.AddRange(ownTeam, foreignTeam);
            await db.SaveChangesAsync();

            db.Add(new TeamMember { TeamId = ownTeam.Id, UserId = "member-1", Role = "Member", CreatedAt = DateTime.UtcNow });

            var own = new Incident
            {
                Title = "own-team incident",
                Severity = IncidentSeverity.High,
                Status = IncidentStatus.Open,
                StartedAt = DateTime.UtcNow,
                TeamId = ownTeam.Id,
            };
            var foreign = new Incident
            {
                Title = "foreign-team incident",
                Severity = IncidentSeverity.High,
                Status = IncidentStatus.Open,
                StartedAt = DateTime.UtcNow,
                TeamId = foreignTeam.Id,
            };
            db.AddRange(own, foreign);
            await db.SaveChangesAsync();
            ownIncidentId = own.Id;
            foreignIncidentId = foreign.Id;
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

        var member = Substitute.For<ICurrentUserService>();
        member.IsAuthenticated.Returns(true);
        member.UserId.Returns("member-1");
        member.IsInRole(Arg.Any<string>()).Returns(false);

        var dispatcher = Substitute.For<IIncidentEventDispatcher>();

        var incidents = new IncidentService(
            sp.GetRequiredService<IIncidentRepository>(),
            sp.GetRequiredService<IIncidentTimelineEventRepository>(),
            sp.GetRequiredService<IEscalationPolicyRepository>(),
            sp.GetRequiredService<ITeamMemberRepository>(),
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
            member,
            sp.GetRequiredService<IServiceRepository>(),
            Substitute.For<INotificationChannelService>(),
            Substitute.For<IMaintenanceWindowService>(),
            new CalluMetrics(new FakeMeterFactory()),
            NullLogger<IncidentService>.Instance);

        return new World(ownIncidentId, foreignIncidentId, incidents, dispatcher);
    }
}
