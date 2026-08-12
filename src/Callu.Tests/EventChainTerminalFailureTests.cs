using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Plugins;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Persistence.UnitOfWork;
using Callu.Infrastructure.Plugins;
using Callu.Infrastructure.Quartz;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Quartz;

namespace Callu.Tests;

/// <summary>An event chain that will never be retried again leaves a timeline mark, not only a ledger row.</summary>
public class EventChainTerminalFailureTests : IDisposable
{
    private static readonly Guid IncidentId = Guid.NewGuid();
    private static readonly Guid ServiceId = Guid.NewGuid();

    private readonly ApplicationDbContext _ctx = new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase($"chain-failure-{Guid.NewGuid():N}")
        .Options);

    private readonly IAuditLogService _audit = Substitute.For<IAuditLogService>();

    public void Dispose()
    {
        _ctx.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task AnSsrfRejectedCallback_LeavesATimelineMark()
    {
        await Dispatcher(Service(s => s.AckUrl = "http://127.0.0.1/ack")).SendServiceAckAsync(IncidentId, "acknowledge");

        var evt = Assert.Single(TimelineEvents());
        Assert.Equal(TimelineEventType.ActionFailed, evt.EventType);
        Assert.Equal("system:action", evt.ActorUserId);

        await _audit.Received(1).LogAsync(
            "system:action", AuditAction.ServiceActionFailed, "Incident", IncidentId.ToString(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ABrokenTemplate_LeavesATimelineMark()
    {
        await Dispatcher(Service(s => s.AckPayloadTemplate = "{{ if incident.id }}broken"))
            .SendServiceAckAsync(IncidentId, "acknowledge");

        var evt = Assert.Single(TimelineEvents());
        Assert.Equal(TimelineEventType.ActionFailed, evt.EventType);
    }

    [Fact]
    public async Task ASuccessfulCallback_LeavesNoTimelineNoise()
    {
        await Dispatcher(Service(), Responds(System.Net.HttpStatusCode.OK)).SendServiceAckAsync(IncidentId, "acknowledge");

        Assert.Empty(TimelineEvents());
    }

    [Fact]
    public async Task ARetryableFailure_LeavesNoMarkYet_TheChainIsStillAlive()
    {
        await Dispatcher(Service(), Responds(System.Net.HttpStatusCode.InternalServerError))
            .SendServiceAckAsync(IncidentId, "acknowledge");

        Assert.Empty(TimelineEvents());
    }

    /// <summary>The sweep's close-out of an exhausted chain also reaches the timeline.</summary>
    [Fact]
    public async Task AChainTheSweepStops_LeavesATimelineMark()
    {
        _ctx.Add(new WebhookDelivery
        {
            IncidentId = IncidentId,
            AckType = "acknowledge",
            Url = "https://acks.example.io/incident",
            Status = WebhookDeliveryStatus.Retrying,
            AttemptCount = IncidentEventDispatcher.MaxAttempts,
            AttemptedAt = DateTime.UtcNow.AddHours(-1),
            NextRetryAt = DateTime.UtcNow.AddMinutes(-1),
            CreatedAt = DateTime.UtcNow.AddHours(-1),
        });
        _ctx.SaveChanges();

        await RunSweepAsync();

        var evt = Assert.Single(TimelineEvents());
        Assert.Equal(TimelineEventType.ActionFailed, evt.EventType);
        Assert.Contains("stopped", evt.Description);
    }

    // ── harness ───────────────────────────────────────────────────────────────────────────────────

    private sealed class StaticHandler(System.Net.HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("{}") });
    }

    private static HttpMessageHandler Responds(System.Net.HttpStatusCode status) => new StaticHandler(status);

    private static Service Service(Action<Service>? configure = null)
    {
        var service = new Service
        {
            Id = ServiceId,
            Name = "checkout",
            AckEnabled = true,
            AckUrl = "https://example.com/ack",
            AckPayloadTemplate = "{\"id\":\"{{ incident.id }}\"}"
        };
        configure?.Invoke(service);
        return service;
    }

    private List<IncidentTimelineEvent> TimelineEvents() =>
        _ctx.Set<IncidentTimelineEvent>().AsNoTracking().ToList();

    private Task RunSweepAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_ctx);
        services.AddSingleton(Substitute.For<IIncidentEventDispatcher>());
        services.AddSingleton(_audit);
        using var provider = services.BuildServiceProvider();

        var context = Substitute.For<IJobExecutionContext>();
        context.CancellationToken.Returns(CancellationToken.None);

        return new WebhookDeliveryRetryQuartzJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WebhookDeliveryRetryQuartzJob>.Instance)
            .Execute(context);
    }

    private IncidentEventDispatcher Dispatcher(Service? service, HttpMessageHandler? handler = null)
    {
        var incident = new Incident
        {
            Id = IncidentId,
            Title = "checkout latency",
            Status = IncidentStatus.Open,
            Severity = IncidentSeverity.High,
            StartedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            ServiceId = service?.Id,
            Service = service
        };

        var incidents = Substitute.For<IIncidentRepository>();
        incidents.GetWithServiceAsync(IncidentId, Arg.Any<CancellationToken>()).Returns(incident);

        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>())
            .Returns(_ => new HttpClient(handler ?? new StaticHandler(System.Net.HttpStatusCode.OK)));

        return new IncidentEventDispatcher(
            incidents,
            new Repository<WebhookDelivery>(_ctx, NullLogger<Repository<WebhookDelivery>>.Instance),
            new UnitOfWork(_ctx, NullLoggerFactory.Instance),
            _ctx,
            NullLogger<IncidentEventDispatcher>.Instance,
            httpClientFactory,
            Microsoft.Extensions.Options.Options.Create(new Callu.Infrastructure.Configuration.CommunicationSettingsOptions()),
            metrics: null,
            auditLog: _audit);
    }
}
