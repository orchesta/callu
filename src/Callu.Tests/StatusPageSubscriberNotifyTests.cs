using System.Linq.Expressions;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Messaging;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Infrastructure.Services;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Callu.Tests;

/// <summary>The manual notify-subscribers publish happens inside the transaction manager, so the outbox row commits with it.</summary>
public class StatusPageSubscriberNotifyTests
{
    private sealed class RecordingTransactionManager : ITransactionManager
    {
        public bool Active { get; private set; }
        public int Executions { get; private set; }

        public async Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> operation, CancellationToken cancellationToken = default)
        {
            Executions++;
            Active = true;
            try { return await operation(); }
            finally { Active = false; }
        }

        public async Task ExecuteInTransactionAsync(Func<Task> operation, CancellationToken cancellationToken = default)
        {
            Executions++;
            Active = true;
            try { await operation(); }
            finally { Active = false; }
        }

        public bool IsInTransaction() => Active;
    }

    private sealed class RecordingNotifier : IStatusPageSubscriberNotifier
    {
        private readonly RecordingTransactionManager _tx;
        public RecordingNotifier(RecordingTransactionManager tx) => _tx = tx;

        public bool Called { get; private set; }
        public bool CalledInsideTransaction { get; private set; }

        public Task NotifyAsync(Guid statusPageIncidentId, CancellationToken cancellationToken = default)
        {
            Called = true;
            CalledInsideTransaction = _tx.Active;
            return Task.CompletedTask;
        }
    }

    private static HybridCache NewCache() =>
        new ServiceCollection().AddHybridCache().Services
            .BuildServiceProvider().GetRequiredService<HybridCache>();

    private static StatusPageService BuildService(
        RecordingTransactionManager tx,
        RecordingNotifier notifier,
        StatusPageIncident? incident,
        StatusPage? page)
    {
        var statusPageRepo = Substitute.For<IStatusPageRepository>();
        statusPageRepo.FindSingleAsync(Arg.Any<Expression<Func<StatusPage, bool>>>(), Arg.Any<CancellationToken>())
            .Returns(page);

        var incidentRepo = Substitute.For<IRepository<StatusPageIncident>>();
        incidentRepo.FindSingleAsync(Arg.Any<Expression<Func<StatusPageIncident, bool>>>(), Arg.Any<CancellationToken>())
            .Returns(incident);

        return new StatusPageService(
            statusPageRepo,
            Substitute.For<IRepository<StatusPageComponent>>(),
            incidentRepo,
            Substitute.For<IRepository<StatusPageIncidentUpdate>>(),
            Substitute.For<IRepository<StatusPageView>>(),
            Substitute.For<IRepository<StatusPageSubscriber>>(),
            tx,
            Substitute.For<IEmailService>(),
            Substitute.For<IOrganizationSettingsService>(),
            NewCache(),
            notifier,
            NullLogger<StatusPageService>.Instance);
    }

    [Fact]
    public async Task TriggerSubscriberNotification_PublishesInsideTransaction()
    {
        var tx = new RecordingTransactionManager();
        var notifier = new RecordingNotifier(tx);
        var pageId = Guid.NewGuid();
        var incident = new StatusPageIncident { Id = Guid.NewGuid(), StatusPageId = pageId, Title = "db down" };
        var page = new StatusPage { Id = pageId, Name = "Main", AllowSubscriptions = true };

        var service = BuildService(tx, notifier, incident, page);

        var result = await service.TriggerSubscriberNotificationAsync(incident.Id);

        Assert.True(result);
        Assert.True(notifier.Called);
        Assert.True(notifier.CalledInsideTransaction);
        Assert.Equal(1, tx.Executions);
    }

    [Fact]
    public async Task TriggerSubscriberNotification_ReturnsFalse_WhenSubscriptionsDisabled()
    {
        var tx = new RecordingTransactionManager();
        var notifier = new RecordingNotifier(tx);
        var pageId = Guid.NewGuid();
        var incident = new StatusPageIncident { Id = Guid.NewGuid(), StatusPageId = pageId, Title = "db down" };
        var page = new StatusPage { Id = pageId, Name = "Main", AllowSubscriptions = false };

        var service = BuildService(tx, notifier, incident, page);

        var result = await service.TriggerSubscriberNotificationAsync(incident.Id);

        Assert.False(result);
        Assert.False(notifier.Called);
    }

    [Fact]
    public async Task TriggerSubscriberNotification_ReturnsFalse_WhenIncidentMissing()
    {
        var tx = new RecordingTransactionManager();
        var notifier = new RecordingNotifier(tx);

        var service = BuildService(tx, notifier, incident: null, page: null);

        var result = await service.TriggerSubscriberNotificationAsync(Guid.NewGuid());

        Assert.False(result);
        Assert.False(notifier.Called);
    }
}
