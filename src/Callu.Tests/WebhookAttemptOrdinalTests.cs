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

/// <summary>The attempt ordinal the dispatcher stamps is what holds both ends of the ACK retry budget.</summary>
public class WebhookAttemptOrdinalTests : IDisposable
{
    private static readonly Guid IncidentId = Guid.NewGuid();
    private static readonly Guid ServiceId = Guid.NewGuid();
    private const string AckType = "acknowledge";

    private readonly ApplicationDbContext _ctx;

    public WebhookAttemptOrdinalTests()
    {
        _ctx = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"webhook-ordinal-{Guid.NewGuid():N}")
            .Options);
    }

    public void Dispose() => _ctx.Dispose();

    /// <summary>Fails every send, so the dispatch always lands on the "retryable failure" path that records a row.</summary>
    private sealed class AlwaysFailsHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("connection refused");
    }

    private IncidentEventDispatcher Dispatcher()
    {
        var service = new Service
        {
            Id = ServiceId,
            Name = "checkout",
            AckEnabled = true,
            // RFC 6761 reserved: never resolves, so the SSRF pre-check reports Unresolvable and the
            // attempt is recorded as retryable. Should a hostile resolver answer anyway, the handler
            // above fails the send and we land on the same path.
            AckUrl = "https://acks.invalid/incident",
            AckPayloadTemplate = "{\"id\":\"{{ incident.id }}\"}",
        };

        var incident = new Incident
        {
            Id = IncidentId,
            Title = "checkout latency",
            Status = IncidentStatus.Open,
            Severity = IncidentSeverity.High,
            StartedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            ServiceId = ServiceId,
            Service = service,
        };

        var incidents = Substitute.For<IIncidentRepository>();
        incidents.GetWithServiceAsync(IncidentId, Arg.Any<CancellationToken>()).Returns(incident);

        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(new AlwaysFailsHandler()));

        return new IncidentEventDispatcher(
            incidents,
            new Repository<WebhookDelivery>(_ctx, NullLogger<Repository<WebhookDelivery>>.Instance),
            new UnitOfWork(_ctx, NullLoggerFactory.Instance),
            _ctx,
            NullLogger<IncidentEventDispatcher>.Instance,
            httpClientFactory,
            Microsoft.Extensions.Options.Options.Create(new Callu.Infrastructure.Configuration.CommunicationSettingsOptions()));
    }

    private void Seed(params int[] ordinals)
    {
        foreach (var ordinal in ordinals)
            _ctx.Add(new WebhookDelivery
            {
                Id = Guid.NewGuid(),
                IncidentId = IncidentId,
                ServiceId = ServiceId,
                Direction = "Outbound",
                Url = "https://acks.invalid/incident",
                AckType = AckType,
                Error = "HTTP 503",
                AttemptCount = ordinal,
                AttemptedAt = DateTime.UtcNow.AddMinutes(-5),
                Status = WebhookDeliveryStatus.Failed,
                CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            });

        _ctx.SaveChanges();
    }

    /// <summary>Dispatch once and return the row the dispatcher wrote for that attempt.</summary>
    private async Task<WebhookDelivery> DispatchAsync(string ackType = AckType)
    {
        var before = _ctx.Set<WebhookDelivery>().AsNoTracking().Select(d => d.Id).ToHashSet();

        var outcome = await Dispatcher().SendServiceAckAsync(IncidentId, ackType);
        Assert.Equal(AckDispatchOutcome.Recorded, outcome);

        var written = await _ctx.Set<WebhookDelivery>().AsNoTracking().ToListAsync();
        return Assert.Single(written, d => !before.Contains(d.Id));
    }

    /// <summary>The first attempt of a chain is attempt 1 — there is nothing to carry forward.</summary>
    [Fact]
    public async Task TheFirstAttemptOfAChain_IsNumberedOne()
    {
        Assert.Equal(1, (await DispatchAsync()).AttemptCount);
    }

    /// <summary>A chain whose every attempt produced a row keeps counting the way it always did.</summary>
    [Fact]
    public async Task AChainWhereEveryAttemptWasRecorded_CountsOnFromTheLastOne()
    {
        Seed(1, 2);

        Assert.Equal(3, (await DispatchAsync()).AttemptCount);
    }

    /// <summary>An attempt whose row could not be written is still counted, because the chain's highest ordinal is the floor.</summary>
    [Fact]
    public async Task AnAttemptThatCouldNotBeRecorded_IsStillCountedAgainstTheBudget()
    {
        Seed(3);

        Assert.Equal(3, (await DispatchAsync()).AttemptCount);
    }

    /// <summary>Chains are per (incident, ack type): a resolve ACK does not inherit the acknowledge chain's budget.</summary>
    [Fact]
    public async Task ADifferentAckType_StartsItsOwnBudget()
    {
        Seed(1, 2, 3, 4, 5);

        var resolveRow = await DispatchAsync("resolve");

        Assert.Equal(1, resolveRow.AttemptCount);
        Assert.Equal(WebhookDeliveryStatus.Retrying, resolveRow.Status);
    }

    /// <summary>
    /// ...and the budget is still an upper bound: the attempt that lands on the last rung is recorded
    /// as terminal rather than left armed, so the chain stops instead of retrying forever.
    /// </summary>
    [Fact]
    public async Task TheAttemptOnTheLastRung_IsRecordedAsTerminal()
    {
        Seed(1, 2, 3, 4, 5);

        var written = await DispatchAsync();

        Assert.Equal(IncidentEventDispatcher.MaxAttempts, written.AttemptCount);
        Assert.Equal(WebhookDeliveryStatus.Failed, written.Status);
        Assert.Null(written.NextRetryAt);
    }
}
