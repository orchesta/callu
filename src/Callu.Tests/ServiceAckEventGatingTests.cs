using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Plugins;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Persistence.UnitOfWork;
using Callu.Infrastructure.Plugins;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Callu.Tests;

/// <summary>Pins how a service's AckEvents selection gates each lifecycle event, and which secret signs the request.</summary>
public class ServiceAckEventGatingTests : IDisposable
{
    private static readonly Guid IncidentId = Guid.NewGuid();
    private static readonly Guid ServiceId = Guid.NewGuid();

    private readonly ApplicationDbContext _ctx = new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase($"ack-gating-{Guid.NewGuid():N}")
        .Options);

    public void Dispose()
    {
        _ctx.Dispose();
        GC.SuppressFinalize(this);
    }

    // ── the gate itself ───────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("acknowledge", true)]
    [InlineData("resolve", true)]
    [InlineData("created", false)]
    [InlineData("closed", false)]
    [InlineData("reopened", false)]
    public void ANullSelection_MeansTheLegacyPair_AcknowledgeAndResolve(string ackType, bool selected)
    {
        Assert.Equal(selected, IncidentEventDispatcher.AckEventSelected(null, ackType));
    }

    [Theory]
    [InlineData(ServiceAckEvents.Created, "created")]
    [InlineData(ServiceAckEvents.Acknowledged, "acknowledge")]
    [InlineData(ServiceAckEvents.Resolved, "resolve")]
    [InlineData(ServiceAckEvents.Closed, "closed")]
    [InlineData(ServiceAckEvents.Reopened, "reopened")]
    public void EachFlag_GatesExactlyItsOwnEvent(ServiceAckEvents flag, string ackType)
    {
        Assert.True(IncidentEventDispatcher.AckEventSelected(flag, ackType));

        foreach (var other in new[] { "created", "acknowledge", "resolve", "closed", "reopened" }.Where(t => t != ackType))
            Assert.False(IncidentEventDispatcher.AckEventSelected(flag, other));
    }

    [Fact]
    public void None_FiresNothing()
    {
        foreach (var ackType in new[] { "created", "acknowledge", "resolve", "closed", "reopened" })
            Assert.False(IncidentEventDispatcher.AckEventSelected(ServiceAckEvents.None, ackType));
    }

    [Fact]
    public void AnUnknownAckType_NeverFires_EvenWithEveryFlagSet()
    {
        var all = ServiceAckEvents.Created | ServiceAckEvents.Acknowledged | ServiceAckEvents.Resolved
                  | ServiceAckEvents.Closed | ServiceAckEvents.Reopened;

        Assert.False(IncidentEventDispatcher.AckEventSelected(all, "manual:00000000-0000-0000-0000-000000000000"));
        Assert.False(IncidentEventDispatcher.AckEventSelected(all, "something-else"));
    }

    [Fact]
    public void UndefinedBits_AreIgnored_NotTreatedAsASelection()
    {
        var withUndefined = (ServiceAckEvents)(1 << 10) | ServiceAckEvents.Resolved;

        Assert.True(IncidentEventDispatcher.AckEventSelected(withUndefined, "resolve"));
        Assert.False(IncidentEventDispatcher.AckEventSelected(withUndefined, "acknowledge"));
    }

    // ── the gate through the dispatcher ───────────────────────────────────────────────────────────

    [Fact]
    public async Task AnUnselectedEvent_IsSkipped_AndWritesNoLedgerRow()
    {
        var outcome = await Dispatcher(Service(s => s.AckEvents = ServiceAckEvents.Created))
            .SendServiceAckAsync(IncidentId, "acknowledge");

        Assert.Equal(AckDispatchOutcome.Skipped, outcome);
        Assert.Empty(await Rows());
    }

    [Fact]
    public async Task ASelectedEvent_IsSent()
    {
        var outcome = await Dispatcher(Service(s => s.AckEvents = ServiceAckEvents.Created))
            .SendServiceAckAsync(IncidentId, "created");

        Assert.Equal(AckDispatchOutcome.Recorded, outcome);
        var row = Assert.Single(await Rows());
        Assert.Equal(WebhookDeliveryStatus.Succeeded, row.Status);
        Assert.Equal("created", row.AckType);
    }

    // ── configuration failures leave a terminal row, not silence ─────────────────────────────────

    /// <summary>Parses clean, throws at render: include has no template loader to call.</summary>
    [Fact]
    public async Task ATemplateThatFailsAtRender_IsRecordedAsTerminallyFailed()
    {
        var outcome = await Dispatcher(Service(s => s.AckPayloadTemplate = "{{ include 'missing' }}"))
            .SendServiceAckAsync(IncidentId, "acknowledge");

        Assert.Equal(AckDispatchOutcome.Recorded, outcome);
        var row = Assert.Single(_ctx.Set<WebhookDelivery>().AsNoTracking().ToList());
        Assert.Equal(WebhookDeliveryStatus.Failed, row.Status);
        Assert.Null(row.NextRetryAt);
        Assert.Contains("render error", row.Error);
    }

    [Fact]
    public async Task AContentTypeWithParameters_IsRecordedAsTerminallyFailed_NotAnUnrecordedThrow()
    {
        var outcome = await Dispatcher(Service(s => s.AckContentType = "application/json; charset=utf-8"))
            .SendServiceAckAsync(IncidentId, "acknowledge");

        Assert.Equal(AckDispatchOutcome.Recorded, outcome);
        var row = Assert.Single(_ctx.Set<WebhookDelivery>().AsNoTracking().ToList());
        Assert.Equal(WebhookDeliveryStatus.Failed, row.Status);
        Assert.Contains("Invalid content type", row.Error);
    }

    // ── delivery key episodes ─────────────────────────────────────────────────────────────────────

    /// <summary>After a delivered event, a reopened incident's repeat of it gets a fresh key.</summary>
    [Fact]
    public async Task ASecondEpisodeOfTheSameEvent_CarriesAFreshDeliveryKey()
    {
        var handler = new RecordingHandler();

        await Dispatcher(Service(), handler).SendServiceAckAsync(IncidentId, "resolve");
        await Dispatcher(Service(), handler).SendServiceAckAsync(IncidentId, "resolve");

        Assert.Equal(2, handler.Requests.Count);
        var first = string.Join("", handler.Requests[0].Headers.GetValues("X-Callu-Delivery-Key"));
        var second = string.Join("", handler.Requests[1].Headers.GetValues("X-Callu-Delivery-Key"));
        Assert.Equal($"{IncidentId:D}:resolve", first);
        Assert.Equal($"{IncidentId:D}:resolve:e1", second);
    }

    // ── which secret signs ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WithoutADedicatedAckSecret_TheInboundWebhookSecretStillSigns_ExactlyAsBefore()
    {
        var handler = new RecordingHandler();
        await Dispatcher(Service(s =>
        {
            s.WebhookSecret = "inbound-secret";
            s.WebhookSignatureHeader = "X-Hub-Signature-256";
        }), handler).SendServiceAckAsync(IncidentId, "acknowledge");

        var request = Assert.Single(handler.Requests);
        Assert.True(request.Headers.Contains("X-Hub-Signature-256"));
    }

    [Fact]
    public async Task ADedicatedAckSecret_Wins_AndItsHeaderNameIsUsed()
    {
        var handler = new RecordingHandler();
        await Dispatcher(Service(s =>
        {
            s.WebhookSecret = "inbound-secret";
            s.WebhookSignatureHeader = "X-Hub-Signature-256";
            s.AckSecret = "outbound-secret";
            s.AckSignatureHeader = "X-Ack-Signature";
        }), handler).SendServiceAckAsync(IncidentId, "acknowledge");

        var request = Assert.Single(handler.Requests);
        Assert.True(request.Headers.Contains("X-Ack-Signature"));
        Assert.False(request.Headers.Contains("X-Hub-Signature-256"));

        var signed = string.Join("", request.Headers.GetValues("X-Ack-Signature"));
        var expected = ExpectedSignature("outbound-secret", "{\"id\":\"" + IncidentId + "\"}");
        Assert.Equal(expected, signed);
    }

    [Fact]
    public async Task NoSecretAtAll_SendsUnsigned()
    {
        var handler = new RecordingHandler();
        await Dispatcher(Service(), handler).SendServiceAckAsync(IncidentId, "acknowledge");

        var request = Assert.Single(handler.Requests);
        Assert.False(request.Headers.Contains("X-Callu-Signature"));
    }

    // ── harness ───────────────────────────────────────────────────────────────────────────────────

    private static string ExpectedSignature(string secret, string payload)
    {
        using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(secret));
        return "sha256=" + Convert.ToHexString(hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
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

    /// <summary>A service whose ACK URL resolves publicly, so the send happens and the request can be inspected.</summary>
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

    private Task<List<WebhookDelivery>> Rows() =>
        _ctx.Set<WebhookDelivery>().AsNoTracking().ToListAsync();

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
            .Returns(_ => new HttpClient(handler ?? new RecordingHandler()));

        return new IncidentEventDispatcher(
            incidents,
            new Repository<WebhookDelivery>(_ctx, NullLogger<Repository<WebhookDelivery>>.Instance),
            new UnitOfWork(_ctx, NullLoggerFactory.Instance),
            _ctx,
            NullLogger<IncidentEventDispatcher>.Instance,
            httpClientFactory,
            Microsoft.Extensions.Options.Options.Create(new Callu.Infrastructure.Configuration.CommunicationSettingsOptions()));
    }
}
