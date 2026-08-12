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

/// <summary>Pins which real-world ACK failure maps to which AckDispatchOutcome, the end the retry sweep branches on.</summary>
public class IncidentEventDispatcherOutcomeTests : IDisposable
{
    private static readonly Guid IncidentId = Guid.NewGuid();
    private static readonly Guid ServiceId = Guid.NewGuid();

    private readonly ApplicationDbContext _ctx = new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase($"ack-outcome-{Guid.NewGuid():N}")
        .Options);

    public void Dispose()
    {
        _ctx.Dispose();
        GC.SuppressFinalize(this);
    }

    // ── Skipped: nothing was sent, and nothing ever will be ───────────────────────────────────────

    /// <summary>An incident with no service behind it has nobody to acknowledge to.</summary>
    [Fact]
    public async Task NoService_IsSkipped_AndWritesNoLedgerRow()
    {
        var outcome = await Dispatcher(service: null).SendServiceAckAsync(IncidentId, "acknowledge");

        Assert.Equal(AckDispatchOutcome.Skipped, outcome);
        Assert.Empty(await Rows());
    }

    [Fact]
    public async Task AckDisabled_IsSkipped()
    {
        var outcome = await Dispatcher(Service(s => s.AckEnabled = false)).SendServiceAckAsync(IncidentId, "acknowledge");

        Assert.Equal(AckDispatchOutcome.Skipped, outcome);
        Assert.Empty(await Rows());
    }

    [Fact]
    public async Task AckEnabledWithNoUrl_IsSkipped()
    {
        var outcome = await Dispatcher(Service(s => s.AckUrl = null)).SendServiceAckAsync(IncidentId, "acknowledge");

        Assert.Equal(AckDispatchOutcome.Skipped, outcome);
        Assert.Empty(await Rows());
    }

    [Fact]
    public async Task AckEnabledWithNoTemplate_IsSkipped()
    {
        var outcome = await Dispatcher(Service(s => s.AckPayloadTemplate = null)).SendServiceAckAsync(IncidentId, "acknowledge");

        Assert.Equal(AckDispatchOutcome.Skipped, outcome);
        Assert.Empty(await Rows());
    }

    /// <summary>A template that does not parse will not parse on the next attempt either, so it is a terminal Failed delivery the operator can see.</summary>
    [Fact]
    public async Task ABrokenTemplate_IsRecordedAsTerminallyFailed()
    {
        // Scriban is forgiving — an unterminated `{{ incident.id` parses fine — so the template is
        // broken in a way it actually rejects: a block that is never closed.
        var template = Scriban.Template.Parse("{{ if incident.id }}{\"id\":\"{{ incident.id }}\"}");
        Assert.True(template.HasErrors, "the template this test relies on must actually fail to parse");

        var outcome = await Dispatcher(Service(s => s.AckPayloadTemplate = "{{ if incident.id }}{\"id\":\"{{ incident.id }}\"}"))
            .SendServiceAckAsync(IncidentId, "acknowledge");

        Assert.Equal(AckDispatchOutcome.Recorded, outcome);

        var row = Assert.Single(await Rows());
        Assert.Equal(WebhookDeliveryStatus.Failed, row.Status);
        Assert.Null(row.NextRetryAt);
        Assert.Contains("Template parse error", row.Error);
    }

    /// <summary>
    /// The SSRF guard REFUSED the URL — it points at loopback, or at the cloud metadata service.
    /// Retrying changes nothing, so it is recorded as a terminal Failed delivery, not skipped.
    /// </summary>
    [Theory]
    [InlineData("http://127.0.0.1/ack")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("file:///etc/passwd")]
    [InlineData("not-a-url")]
    public async Task AUrlTheSsrfGuardRejects_IsRecordedAsTerminallyFailed(string url)
    {
        var outcome = await Dispatcher(Service(s => s.AckUrl = url)).SendServiceAckAsync(IncidentId, "acknowledge");

        Assert.Equal(AckDispatchOutcome.Recorded, outcome);

        var row = Assert.Single(await Rows());
        Assert.Equal(WebhookDeliveryStatus.Failed, row.Status);
        Assert.Null(row.NextRetryAt);
        Assert.NotNull(row.Error);
    }

    // ── Recorded: it was attempted, and the row carries what happens next ─────────────────────────

    /// <summary>A host that does not resolve is recorded as retryable, since a resolver outage is not a configuration verdict.</summary>
    [Fact]
    public async Task AHostThatDoesNotResolve_IsRecorded_AndLeftRetryable()
    {
        var outcome = await Dispatcher(Service()).SendServiceAckAsync(IncidentId, "acknowledge");

        Assert.Equal(AckDispatchOutcome.Recorded, outcome);

        var row = Assert.Single(await Rows());
        Assert.Equal(WebhookDeliveryStatus.Retrying, row.Status);
        Assert.NotNull(row.NextRetryAt);
    }

    /// <summary>
    /// The endpoint answered, and it answered 500. Retryable: the row is written with the next
    /// deadline on it and the sweep will come back.
    /// </summary>
    [Fact]
    public async Task AServerError_IsRecorded_AndLeftRetryable()
    {
        var outcome = await Dispatcher(Reachable(), Responds(System.Net.HttpStatusCode.InternalServerError))
            .SendServiceAckAsync(IncidentId, "acknowledge");

        Assert.Equal(AckDispatchOutcome.Recorded, outcome);

        var row = Assert.Single(await Rows());
        Assert.Equal(WebhookDeliveryStatus.Retrying, row.Status);
        Assert.Equal(500, row.HttpStatus);
        Assert.NotNull(row.NextRetryAt);
    }

    /// <summary>A client error is recorded, because the attempt went out, but terminal.</summary>
    [Fact]
    public async Task AClientError_IsRecorded_ButTerminal()
    {
        var outcome = await Dispatcher(Reachable(), Responds(System.Net.HttpStatusCode.BadRequest))
            .SendServiceAckAsync(IncidentId, "acknowledge");

        Assert.Equal(AckDispatchOutcome.Recorded, outcome);

        var row = Assert.Single(await Rows());
        Assert.Equal(WebhookDeliveryStatus.Failed, row.Status);
        Assert.Null(row.NextRetryAt);
    }

    [Fact]
    public async Task ASuccessfulAck_IsRecorded_AndTheChainIsDone()
    {
        var outcome = await Dispatcher(Reachable(), Responds(System.Net.HttpStatusCode.OK))
            .SendServiceAckAsync(IncidentId, "acknowledge");

        Assert.Equal(AckDispatchOutcome.Recorded, outcome);

        var row = Assert.Single(await Rows());
        Assert.Equal(WebhookDeliveryStatus.Succeeded, row.Status);
        Assert.Null(row.NextRetryAt);
    }

    // ── NotRecorded: the delivery state is unknown, so the sweep must keep it alive ───────────────

    /// <summary>A failed ledger write is NotRecorded, because the row that would carry the retry does not exist.</summary>
    [Fact]
    public async Task ALedgerWriteThatFails_IsNotRecorded_SoTheSweepKeepsTheDeliveryAlive()
    {
        await using var failing = new FailingDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"ack-outcome-fail-{Guid.NewGuid():N}")
            .Options);

        var outcome = await Dispatcher(Service(), ctx: failing).SendServiceAckAsync(IncidentId, "acknowledge");

        Assert.Equal(AckDispatchOutcome.NotRecorded, outcome);
    }

    /// <summary>The row it could not write is detached, so it cannot poison a later save on the shared scope.</summary>
    [Fact]
    public async Task ALedgerWriteThatFails_LeavesNothingBehindInTheSharedContext()
    {
        await using var failing = new FailingDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"ack-outcome-detach-{Guid.NewGuid():N}")
            .Options);

        await Dispatcher(Service(), ctx: failing).SendServiceAckAsync(IncidentId, "acknowledge");

        Assert.Empty(failing.ChangeTracker.Entries<WebhookDelivery>());
    }

    // ── harness ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>Every send fails at the socket, so a reachable host still lands on a retryable failure.</summary>
    private sealed class AlwaysFailsHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("connection refused");
    }

    private sealed class StaticHandler(System.Net.HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("{}") });
    }

    private sealed class FailingDbContext(DbContextOptions<ApplicationDbContext> options) : ApplicationDbContext(options)
    {
        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
            => Task.FromException<int>(new DbUpdateException("the delivery ledger is unavailable"));
    }

    private static HttpMessageHandler Responds(System.Net.HttpStatusCode status) => new StaticHandler(status);

    /// <summary>A service whose ACK URL does not resolve (RFC 6761 reserved TLD) — the default here.</summary>
    private static Service Service(Action<Service>? configure = null)
    {
        var service = new Service
        {
            Id = ServiceId,
            Name = "checkout",
            AckEnabled = true,
            AckUrl = "https://acks.invalid/incident",
            AckPayloadTemplate = "{\"id\":\"{{ incident.id }}\"}"
        };

        configure?.Invoke(service);
        return service;
    }

    /// <summary>A service whose ACK URL resolves to a public address, so the send actually happens.</summary>
    private static Service Reachable() => Service(s => s.AckUrl = "https://example.com/ack");

    private Task<List<WebhookDelivery>> Rows() =>
        _ctx.Set<WebhookDelivery>().AsNoTracking().ToListAsync();

    private IncidentEventDispatcher Dispatcher(
        Service? service,
        HttpMessageHandler? handler = null,
        ApplicationDbContext? ctx = null)
    {
        var context = ctx ?? _ctx;

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
            .Returns(_ => new HttpClient(handler ?? new AlwaysFailsHandler()));

        return new IncidentEventDispatcher(
            incidents,
            new Repository<WebhookDelivery>(context, NullLogger<Repository<WebhookDelivery>>.Instance),
            new UnitOfWork(context, NullLoggerFactory.Instance),
            context,
            NullLogger<IncidentEventDispatcher>.Instance,
            httpClientFactory,
            Microsoft.Extensions.Options.Options.Create(new Callu.Infrastructure.Configuration.CommunicationSettingsOptions()));
    }
}
