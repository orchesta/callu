using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Persistence.UnitOfWork;
using Callu.Infrastructure.Plugins;
using Callu.Shared.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Callu.Tests;

/// <summary>A manual action gets exactly one attempt: every row it writes is terminal, so the retry sweep can never touch it.</summary>
public class ManualActionExecutionTests : IDisposable
{
    private static readonly Guid IncidentId = Guid.NewGuid();
    private static readonly Guid ServiceId = Guid.NewGuid();
    private static readonly Guid ActionId = Guid.NewGuid();

    private readonly ApplicationDbContext _ctx = new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase($"manual-exec-{Guid.NewGuid():N}")
        .Options);

    public void Dispose()
    {
        _ctx.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ASuccessfulRun_WritesOneTerminalRow_AndReturnsTheStatus()
    {
        var sut = Dispatcher(Responds(System.Net.HttpStatusCode.OK));

        var result = await sut.ExecuteManualActionAsync(IncidentId, ActionId, "user-1");

        Assert.Equal("succeeded", result.Outcome);
        Assert.Equal(200, result.HttpStatus);

        var row = Assert.Single(await Rows());
        Assert.Equal(WebhookDeliveryStatus.Succeeded, row.Status);
        Assert.Equal(ActionId, row.ActionId);
        Assert.Equal("Restart Redis", row.ActionName);
        Assert.Equal($"manual:{ActionId:D}", row.AckType);
        Assert.Null(row.NextRetryAt);
    }

    [Fact]
    public async Task AServerError_IsTerminalFailed_NeverRetrying()
    {
        var sut = Dispatcher(Responds(System.Net.HttpStatusCode.InternalServerError));

        var result = await sut.ExecuteManualActionAsync(IncidentId, ActionId, "user-1");

        Assert.Equal("failed", result.Outcome);
        var row = Assert.Single(await Rows());
        Assert.Equal(WebhookDeliveryStatus.Failed, row.Status);
        Assert.Null(row.NextRetryAt);
    }

    [Fact]
    public async Task AConnectionFailure_IsTerminalFailed_NeverRetrying()
    {
        var sut = Dispatcher(new AlwaysFailsHandler());

        var result = await sut.ExecuteManualActionAsync(IncidentId, ActionId, "user-1");

        Assert.Equal("failed", result.Outcome);
        var row = Assert.Single(await Rows());
        Assert.Equal(WebhookDeliveryStatus.Failed, row.Status);
        Assert.Null(row.NextRetryAt);
    }

    [Fact]
    public async Task AnSsrfRejectedUrl_FailsWithoutSending()
    {
        var handler = new RecordingHandler();
        var sut = Dispatcher(handler, a => a.Url = "http://127.0.0.1/restart");

        var result = await sut.ExecuteManualActionAsync(IncidentId, ActionId, "user-1");

        Assert.Equal("failed", result.Outcome);
        Assert.Empty(handler.Requests);
        var row = Assert.Single(await Rows());
        Assert.Equal(WebhookDeliveryStatus.Failed, row.Status);
    }

    [Fact]
    public async Task AnUnresolvableHost_IsTerminal_UnlikeTheEventPath()
    {
        var sut = Dispatcher(Responds(System.Net.HttpStatusCode.OK), a => a.Url = "https://acks.invalid/run");

        var result = await sut.ExecuteManualActionAsync(IncidentId, ActionId, "user-1");

        Assert.Equal("failed", result.Outcome);
        var row = Assert.Single(await Rows());
        Assert.Equal(WebhookDeliveryStatus.Failed, row.Status);
        Assert.Null(row.NextRetryAt);
    }

    [Fact]
    public async Task ASecondClickWithinTenSeconds_IsA409()
    {
        var sut = Dispatcher(Responds(System.Net.HttpStatusCode.OK));

        await sut.ExecuteManualActionAsync(IncidentId, ActionId, "user-1");

        await Assert.ThrowsAsync<ConflictException>(() =>
            sut.ExecuteManualActionAsync(IncidentId, ActionId, "user-1"));
        Assert.Single(await Rows());
    }

    [Fact]
    public async Task AFreshInFlightRow_IsA409_EvenBeyondTheTenSecondWindow()
    {
        SeedDelivery(WebhookDeliveryStatus.Pending, attemptedAt: DateTime.UtcNow.AddSeconds(-30));
        var sut = Dispatcher(Responds(System.Net.HttpStatusCode.OK));

        await Assert.ThrowsAsync<ConflictException>(() =>
            sut.ExecuteManualActionAsync(IncidentId, ActionId, "user-1"));
    }

    [Fact]
    public async Task AStalePendingRow_IsClosedAsInterrupted_AndNeverResent()
    {
        SeedDelivery(WebhookDeliveryStatus.Pending, attemptedAt: DateTime.UtcNow.AddMinutes(-5));
        var sut = Dispatcher(Responds(System.Net.HttpStatusCode.OK));

        var result = await sut.ExecuteManualActionAsync(IncidentId, ActionId, "user-1");

        Assert.Equal("succeeded", result.Outcome);
        var rows = await Rows();
        Assert.Equal(2, rows.Count);
        var stale = rows.Single(r => r.AttemptedAt < DateTime.UtcNow.AddMinutes(-2));
        Assert.Equal(WebhookDeliveryStatus.Failed, stale.Status);
        Assert.Contains("Interrupted", stale.Error);
    }

    [Fact]
    public async Task AContentTypeWithParameters_FailsWithoutSending()
    {
        var handler = new RecordingHandler();
        var sut = Dispatcher(handler, a => a.ContentType = "application/json; charset=utf-8");

        var result = await sut.ExecuteManualActionAsync(IncidentId, ActionId, "user-1");

        Assert.Equal("failed", result.Outcome);
        Assert.Empty(handler.Requests);
        Assert.Contains("Invalid content type", result.Error);
    }

    [Fact]
    public async Task ATemplateThatFailsAtRender_FailsWithATerminalRow()
    {
        var sut = Dispatcher(Responds(System.Net.HttpStatusCode.OK), a => a.PayloadTemplate = "{{ include 'missing' }}");

        var result = await sut.ExecuteManualActionAsync(IncidentId, ActionId, "user-1");

        Assert.Equal("failed", result.Outcome);
        Assert.Contains("render error", result.Error);
        var row = Assert.Single(await Rows());
        Assert.Equal(WebhookDeliveryStatus.Failed, row.Status);
        Assert.Null(row.NextRetryAt);
    }

    [Fact]
    public async Task ADisabledAction_IsA404()
    {
        var sut = Dispatcher(Responds(System.Net.HttpStatusCode.OK), a => a.IsEnabled = false);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            sut.ExecuteManualActionAsync(IncidentId, ActionId, "user-1"));
    }

    [Fact]
    public async Task AnActionOfAnotherService_IsA404()
    {
        var sut = Dispatcher(Responds(System.Net.HttpStatusCode.OK), a => a.ServiceId = Guid.NewGuid());

        await Assert.ThrowsAsync<NotFoundException>(() =>
            sut.ExecuteManualActionAsync(IncidentId, ActionId, "user-1"));
    }

    [Fact]
    public async Task TheRun_WritesATimelineEventWithTheRealActor()
    {
        var sut = Dispatcher(Responds(System.Net.HttpStatusCode.OK));

        await sut.ExecuteManualActionAsync(IncidentId, ActionId, "user-1");

        var evt = Assert.Single(_ctx.Set<IncidentTimelineEvent>().AsNoTracking().ToList());
        Assert.Equal(TimelineEventType.ActionExecuted, evt.EventType);
        Assert.Equal("user-1", evt.ActorUserId);
        Assert.Contains("Restart Redis", evt.Title);
    }

    [Fact]
    public async Task AGetAction_SendsNoBody()
    {
        var handler = new RecordingHandler();
        var sut = Dispatcher(handler, a => a.HttpMethod = "GET");

        var result = await sut.ExecuteManualActionAsync(IncidentId, ActionId, "user-1");

        Assert.Equal("succeeded", result.Outcome);
        var request = Assert.Single(handler.Requests);
        Assert.Null(request.Content);
    }

    [Fact]
    public async Task OnlyTheActionsOwnSecretSigns_TheInboundWebhookSecretIsNeverBorrowed()
    {
        var handler = new RecordingHandler();
        var sut = Dispatcher(handler, configureService: s =>
        {
            s.WebhookSecret = "inbound-secret";
            s.WebhookSignatureHeader = "X-Hub-Signature-256";
        });

        await sut.ExecuteManualActionAsync(IncidentId, ActionId, "user-1");

        var request = Assert.Single(handler.Requests);
        Assert.False(request.Headers.Contains("X-Hub-Signature-256"));
        Assert.False(request.Headers.Contains("X-Callu-Signature"));
    }

    // ── harness ───────────────────────────────────────────────────────────────────────────────────

    private sealed class AlwaysFailsHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("connection refused");
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }

    private sealed class StaticHandler(System.Net.HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("{}") });
    }

    private static HttpMessageHandler Responds(System.Net.HttpStatusCode status) => new StaticHandler(status);

    private void SeedDelivery(WebhookDeliveryStatus status, DateTime attemptedAt)
    {
        _ctx.Set<WebhookDelivery>().Add(new WebhookDelivery
        {
            Id = Guid.NewGuid(),
            IncidentId = IncidentId,
            ServiceId = ServiceId,
            Direction = "Outbound",
            Url = "https://ops.example/restart",
            AckType = $"manual:{ActionId:D}",
            ActionId = null,
            AttemptCount = 1,
            AttemptedAt = attemptedAt,
            Status = status,
            CreatedAt = attemptedAt,
        });
        _ctx.SaveChanges();
    }

    private Task<List<WebhookDelivery>> Rows() =>
        _ctx.Set<WebhookDelivery>().AsNoTracking().ToListAsync();

    private IncidentEventDispatcher Dispatcher(
        HttpMessageHandler handler,
        Action<ServiceAction>? configureAction = null,
        Action<Service>? configureService = null)
    {
        var service = new Service { Id = ServiceId, Name = "checkout", CreatedAt = DateTime.UtcNow };
        configureService?.Invoke(service);

        var action = new ServiceAction
        {
            Id = ActionId,
            ServiceId = ServiceId,
            Name = "Restart Redis",
            Url = "https://example.com/restart",
            HttpMethod = "POST",
            PayloadTemplate = "{\"incident\":\"{{ incident.id }}\",\"action\":\"{{ action.name }}\"}",
            CreatedAt = DateTime.UtcNow,
        };
        configureAction?.Invoke(action);

        if (_ctx.ServiceActions.AsNoTracking().All(a => a.Id != action.Id))
        {
            if (_ctx.Services.AsNoTracking().All(s => s.Id != service.Id))
                _ctx.Services.Add(service);
            _ctx.ServiceActions.Add(action);
            _ctx.SaveChanges();
            _ctx.ChangeTracker.Clear();
        }

        var incident = new Incident
        {
            Id = IncidentId,
            Title = "redis down",
            Status = IncidentStatus.Open,
            Severity = IncidentSeverity.High,
            StartedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            ServiceId = service.Id,
            Service = service
        };

        var incidents = Substitute.For<IIncidentRepository>();
        incidents.GetWithServiceAsync(IncidentId, Arg.Any<CancellationToken>()).Returns(incident);

        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler));

        return new IncidentEventDispatcher(
            incidents,
            new Repository<WebhookDelivery>(_ctx, NullLogger<Repository<WebhookDelivery>>.Instance),
            new UnitOfWork(_ctx, NullLoggerFactory.Instance),
            _ctx,
            NullLogger<IncidentEventDispatcher>.Instance,
            httpClientFactory,
            Microsoft.Extensions.Options.Options.Create(new Callu.Infrastructure.Configuration.CommunicationSettingsOptions()),
            metrics: null,
            auditLog: Substitute.For<IAuditLogService>(),
            serviceActions: new Repository<ServiceAction>(_ctx, NullLogger<Repository<ServiceAction>>.Instance));
    }
}
