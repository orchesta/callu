using Callu.Application.Plugins;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Quartz;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Quartz;

namespace Callu.Tests;

/// <summary>At most one armed delivery row per incident and ack type, which is what SupersededAsync restores.</summary>
public class WebhookDeliveryRetryArmedRowTests : IDisposable
{
    private static readonly Guid IncidentId = Guid.NewGuid();

    private readonly ApplicationDbContext _ctx;
    private readonly ServiceProvider _provider;
    private readonly IIncidentEventDispatcher _dispatcher = Substitute.For<IIncidentEventDispatcher>();

    public WebhookDeliveryRetryArmedRowTests()
    {
        _ctx = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"webhook-retry-{Guid.NewGuid():N}")
            .Options);

        _dispatcher
            .SendServiceAckAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(AckDispatchOutcome.Recorded);

        var services = new ServiceCollection();
        services.AddSingleton(_ctx);
        services.AddSingleton(_dispatcher);
        _provider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _ctx.Dispose();
    }

    private Task RunSweepAsync()
    {
        var context = Substitute.For<IJobExecutionContext>();
        context.CancellationToken.Returns(CancellationToken.None);

        return new WebhookDeliveryRetryQuartzJob(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WebhookDeliveryRetryQuartzJob>.Instance)
            .Execute(context);
    }

    private WebhookDelivery Armed(
        DateTime createdAt,
        string ackType = "acknowledge",
        int attemptCount = 1,
        WebhookDeliveryStatus status = WebhookDeliveryStatus.Retrying)
    {
        var row = new WebhookDelivery
        {
            IncidentId = IncidentId,
            AckType = ackType,
            Url = "https://acks.example.io/incident",
            Status = status,
            AttemptCount = attemptCount,
            AttemptedAt = createdAt,
            NextRetryAt = DateTime.UtcNow.AddMinutes(-1),
            CreatedAt = createdAt,
        };

        _ctx.Add(row);
        _ctx.SaveChanges();
        return row;
    }

    private async Task<WebhookDelivery> ReloadAsync(Guid id) =>
        await _ctx.Set<WebhookDelivery>().AsNoTracking().SingleAsync(d => d.Id == id);

    private async Task<int> ArmedRowCountAsync() =>
        await _ctx.Set<WebhookDelivery>().AsNoTracking()
            .CountAsync(d => d.NextRetryAt != null && d.Status == WebhookDeliveryStatus.Retrying);

    /// <summary>An older armed row is closed out without dispatching, because the newer row is the chain.</summary>
    [Fact]
    public async Task AnOlderArmedRow_IsClosedOutWithoutDispatching()
    {
        var older = Armed(createdAt: DateTime.UtcNow.AddMinutes(-10));
        var newer = Armed(createdAt: DateTime.UtcNow.AddMinutes(-1));

        await RunSweepAsync();

        var olderRow = await ReloadAsync(older.Id);
        Assert.Equal(WebhookDeliveryStatus.Failed, olderRow.Status);
        Assert.Null(olderRow.NextRetryAt);
        Assert.Contains("Superseded", olderRow.Error);

        // Exactly one dispatch — from the newer row, which owns the chain.
        await _dispatcher.Received(1).SendServiceAckAsync(
            IncidentId, "acknowledge", Arg.Any<CancellationToken>());

        // And the sweep restored the invariant: the newer row was dispatched and closed out.
        Assert.Equal(WebhookDeliveryStatus.Failed, (await ReloadAsync(newer.Id)).Status);
        Assert.Equal(0, await ArmedRowCountAsync());
    }

    /// <summary>Three rows, one chain: the two stale ones are retired, the ACK fires once.</summary>
    [Fact]
    public async Task HoweverManyArmedRowsAChainHas_TheAckFiresOnce()
    {
        Armed(createdAt: DateTime.UtcNow.AddMinutes(-30));
        Armed(createdAt: DateTime.UtcNow.AddMinutes(-20));
        Armed(createdAt: DateTime.UtcNow.AddMinutes(-10));

        await RunSweepAsync();

        await _dispatcher.Received(1).SendServiceAckAsync(
            IncidentId, "acknowledge", Arg.Any<CancellationToken>());
        Assert.Equal(0, await ArmedRowCountAsync());
    }

    /// <summary>
    /// Superseding is scoped to one (incident, ack type) chain. An acknowledge and a resolve for the
    /// same incident are independent — collapsing them would drop the resolve ACK entirely.
    /// </summary>
    [Fact]
    public async Task ADifferentAckType_IsADifferentChain_AndIsNotSuperseded()
    {
        Armed(createdAt: DateTime.UtcNow.AddMinutes(-10), ackType: "acknowledge");
        Armed(createdAt: DateTime.UtcNow.AddMinutes(-1), ackType: "resolve");

        await RunSweepAsync();

        await _dispatcher.Received(1).SendServiceAckAsync(
            IncidentId, "acknowledge", Arg.Any<CancellationToken>());
        await _dispatcher.Received(1).SendServiceAckAsync(
            IncidentId, "resolve", Arg.Any<CancellationToken>());
    }

    /// <summary>A single armed row is the normal case and must still dispatch.</summary>
    [Fact]
    public async Task TheOnlyArmedRowOfAChain_IsDispatched()
    {
        var only = Armed(createdAt: DateTime.UtcNow.AddMinutes(-5));

        await RunSweepAsync();

        await _dispatcher.Received(1).SendServiceAckAsync(
            IncidentId, "acknowledge", Arg.Any<CancellationToken>());

        // The dispatcher recorded its own attempt row, so this one hands the chain over and closes.
        var row = await ReloadAsync(only.Id);
        Assert.Equal(WebhookDeliveryStatus.Failed, row.Status);
        Assert.Null(row.NextRetryAt);
        Assert.Equal(1, row.AttemptCount);
    }

    /// <summary>A stranded Pending row does not perturb the live chain beside it, which still dispatches once.</summary>
    [Fact]
    public async Task APendingRowFromAnOlderVersion_DoesNotDisturbTheLiveChain()
    {
        var stranded = Armed(
            createdAt: DateTime.UtcNow.AddMinutes(-10), status: WebhookDeliveryStatus.Pending);
        Armed(createdAt: DateTime.UtcNow.AddMinutes(-1));

        await RunSweepAsync();

        await _dispatcher.Received(1).SendServiceAckAsync(
            IncidentId, "acknowledge", Arg.Any<CancellationToken>());

        // Untouched — not dispatched from, and not rewritten either.
        Assert.Equal(WebhookDeliveryStatus.Pending, (await ReloadAsync(stranded.Id)).Status);
        Assert.Equal(0, await ArmedRowCountAsync());
    }

    /// <summary>
    /// Nothing was recorded, so this row is still the chain: the claim already requeued it, and it
    /// must stay armed — exactly one armed row, not zero. Dropping it here loses the ACK for good.
    /// </summary>
    [Fact]
    public async Task WhenTheDispatchRecordsNothing_TheRowStaysTheChain_AndStaysArmed()
    {
        _dispatcher
            .SendServiceAckAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(AckDispatchOutcome.NotRecorded);

        var row = Armed(createdAt: DateTime.UtcNow.AddMinutes(-5), attemptCount: 1);

        await RunSweepAsync();

        var after = await ReloadAsync(row.Id);
        Assert.Equal(WebhookDeliveryStatus.Retrying, after.Status);
        Assert.NotNull(after.NextRetryAt);
        Assert.Equal(2, after.AttemptCount);
        Assert.Equal(1, await ArmedRowCountAsync());
    }

    /// <summary>An ACK that can never be delivered is retired rather than left armed forever.</summary>
    [Fact]
    public async Task WhenTheAckIsNotConfigured_TheRowIsNotLeftArmedForever()
    {
        _dispatcher
            .SendServiceAckAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(AckDispatchOutcome.Skipped);

        var row = Armed(createdAt: DateTime.UtcNow.AddMinutes(-5), attemptCount: 5);

        await RunSweepAsync();

        var after = await ReloadAsync(row.Id);
        Assert.Equal(WebhookDeliveryStatus.Failed, after.Status);
        Assert.Null(after.NextRetryAt);
        Assert.Equal(0, await ArmedRowCountAsync());
    }
}
