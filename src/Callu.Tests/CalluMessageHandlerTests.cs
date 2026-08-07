using System.Linq.Expressions;
using System.Text.Json;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Contracts.Messages;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Infrastructure.Messaging;
using Callu.Infrastructure.Messaging.Consuming;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Infrastructure.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Callu.Domain.Enums;

namespace Callu.Tests;

/// <summary>What each wire message does when it is handled, and what it leaves behind when it is given up on.</summary>
public class CalluMessageHandlerTests
{
    private static string Payload<TMessage>(TMessage message) =>
        JsonSerializer.Serialize(message, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    [Fact]
    public async Task TriggerIncidentEscalation_ForwardsTheIds_ToTheOrchestrator()
    {
        var orchestrator = Substitute.For<IEscalationOrchestrator>();
        var handler = NewEscalationHandler(orchestrator: orchestrator);

        var incidentId = Guid.NewGuid();
        var policyId = Guid.NewGuid();

        await handler.HandleAsync(Payload(new TriggerIncidentEscalation(incidentId, policyId)), CancellationToken.None);

        await orchestrator.Received(1).TriggerEscalationAsync(incidentId, policyId, Arg.Any<CancellationToken>());
    }

    /// <summary>The wire name is what the allow-list matches on, so it belongs to the topology, not the handler.</summary>
    [Fact]
    public void EachHandler_AnswersToItsTopologyName()
    {
        Assert.Equal(CalluTopology.TriggerIncidentEscalationMessage, NewEscalationHandler().WireName);

        var notifier = new NotifyStatusPageSubscribersHandler(
            Substitute.For<IStatusPageSubscriberEmailSender>(),
            NullLogger<NotifyStatusPageSubscribersHandler>.Instance);

        Assert.Equal(CalluTopology.NotifyStatusPageSubscribersMessage, notifier.WireName);
    }

    [Fact]
    public async Task NotifyStatusPageSubscribers_InvokesTheSender()
    {
        var sender = Substitute.For<IStatusPageSubscriberEmailSender>();
        var handler = new NotifyStatusPageSubscribersHandler(
            sender, NullLogger<NotifyStatusPageSubscribersHandler>.Instance);

        var id = Guid.NewGuid();

        await handler.HandleAsync(Payload(new NotifyStatusPageSubscribers(id)), CancellationToken.None);

        await sender.Received(1).SendForIncidentAsync(id, Arg.Any<CancellationToken>());
    }

    /// <summary>A handler failure has to reach the caller, or the delivery is acked as if it had worked.</summary>
    [Fact]
    public async Task AFailingOrchestrator_SurfacesToTheCaller()
    {
        var orchestrator = Substitute.For<IEscalationOrchestrator>();
        orchestrator
            .When(o => o.TriggerEscalationAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("db down"));

        var handler = NewEscalationHandler(orchestrator: orchestrator);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(
            Payload(new TriggerIncidentEscalation(Guid.NewGuid(), Guid.NewGuid())), CancellationToken.None));
    }

    // ── Giving up on a trigger means nobody is being paged ─────────────────────────────────────────

    /// <summary>
    /// One log line is not a record. An abandoned trigger has to leave an audit row and a timeline
    /// event, because those are the two places an operator looks.
    /// </summary>
    [Fact]
    public async Task GivingUpOnATrigger_WritesAnAuditRowAndATimelineEvent()
    {
        var incidentId = Guid.NewGuid();

        var audit = Substitute.For<IAuditLogService>();
        var timelineRepo = Substitute.For<IIncidentTimelineEventRepository>();
        var handler = NewEscalationHandler(audit: audit, timelineRepo: timelineRepo, incidentExists: true);

        await handler.ReportPermanentFailureAsync(
            Payload(new TriggerIncidentEscalation(incidentId, Guid.NewGuid())), "db down", CancellationToken.None);

        await audit.Received(1).LogAsync(
            Arg.Any<string?>(), AuditAction.EscalationTriggerFailed, "Incident", incidentId.ToString(),
            Arg.Any<string?>(), Arg.Is<string?>(v => v != null && v.Contains("db down")),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());

        await timelineRepo.Received(1).AddAsync(
            Arg.Is<IncidentTimelineEvent>(e => e.IncidentId == incidentId && e.Title.Contains("did not start")),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A hard-deleted incident must not turn the last line of reporting into a failure itself.</summary>
    [Fact]
    public async Task GivingUpOnATrigger_SkipsTheTimelineRow_WhenTheIncidentIsGone()
    {
        var audit = Substitute.For<IAuditLogService>();
        var timelineRepo = Substitute.For<IIncidentTimelineEventRepository>();
        var handler = NewEscalationHandler(audit: audit, timelineRepo: timelineRepo, incidentExists: false);

        await handler.ReportPermanentFailureAsync(
            Payload(new TriggerIncidentEscalation(Guid.NewGuid(), Guid.NewGuid())), "db down", CancellationToken.None);

        await audit.Received(1).LogAsync(
            Arg.Any<string?>(), AuditAction.EscalationTriggerFailed, Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());

        await timelineRepo.DidNotReceive().AddAsync(
            Arg.Any<IncidentTimelineEvent>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Neither write may throw: this runs on the path where the ladder has already been exhausted.</summary>
    [Fact]
    public async Task GivingUpOnATrigger_StillWritesTheTimeline_WhenTheAuditRowCannotBeWritten()
    {
        var incidentId = Guid.NewGuid();

        var audit = Substitute.For<IAuditLogService>();
        audit.LogAsync(
                Arg.Any<string?>(), Arg.Any<AuditAction>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("audit table is gone too"));

        var timelineRepo = Substitute.For<IIncidentTimelineEventRepository>();
        var handler = NewEscalationHandler(audit: audit, timelineRepo: timelineRepo, incidentExists: true);

        await handler.ReportPermanentFailureAsync(
            Payload(new TriggerIncidentEscalation(incidentId, Guid.NewGuid())), "db down", CancellationToken.None);

        await timelineRepo.Received(1).AddAsync(
            Arg.Is<IncidentTimelineEvent>(e => e.IncidentId == incidentId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivingUpOnATrigger_DoesNotThrow_WhenTheTimelineWriteFails()
    {
        var timelineRepo = Substitute.For<IIncidentTimelineEventRepository>();
        timelineRepo
            .AddAsync(Arg.Any<IncidentTimelineEvent>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("timeline write failed"));

        var handler = NewEscalationHandler(timelineRepo: timelineRepo, incidentExists: true);

        await handler.ReportPermanentFailureAsync(
            Payload(new TriggerIncidentEscalation(Guid.NewGuid(), Guid.NewGuid())), "db down", CancellationToken.None);
    }

    private static TriggerIncidentEscalationHandler NewEscalationHandler(
        IEscalationOrchestrator? orchestrator = null,
        IIncidentTimelineEventRepository? timelineRepo = null,
        IAuditLogService? audit = null,
        bool incidentExists = true)
    {
        var incidentRepo = Substitute.For<IIncidentRepository>();
        incidentRepo
            .ExistsAsync(Arg.Any<Expression<Func<Incident, bool>>>(), Arg.Any<CancellationToken>())
            .Returns(incidentExists);

        return new TriggerIncidentEscalationHandler(
            orchestrator ?? Substitute.For<IEscalationOrchestrator>(),
            timelineRepo ?? Substitute.For<IIncidentTimelineEventRepository>(),
            incidentRepo,
            new PassThroughTransactionManager(),
            audit ?? Substitute.For<IAuditLogService>(),
            new CalluMetrics(new FakeMeterFactory()),
            NullLogger<TriggerIncidentEscalationHandler>.Instance);
    }
}

internal sealed class PassThroughTransactionManager : ITransactionManager
{
    public async Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> operation, CancellationToken cancellationToken = default) =>
        await operation();

    public async Task ExecuteInTransactionAsync(Func<Task> operation, CancellationToken cancellationToken = default) =>
        await operation();

    public bool IsInTransaction() => false;
}
