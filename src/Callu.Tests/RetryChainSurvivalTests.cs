using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Plugins;
using Callu.Application.Providers;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Plugins;
using Callu.Infrastructure.Quartz;
using Callu.Shared.Models.Communication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Quartz;

namespace Callu.Tests;

/// <summary>Pins both retry chains — webhook ACK and voice call — to a state the sweep can still select.</summary>
public class RetryChainSurvivalTests
{
    private static IJobExecutionContext JobContext()
    {
        var context = Substitute.For<IJobExecutionContext>();
        context.CancellationToken.Returns(CancellationToken.None);
        return context;
    }

    // ---------------------------------------------------------------- webhook ACK retries

    private sealed class WebhookHarness
    {
        private readonly string _dbName = $"webhook-retry-{Guid.NewGuid():N}";

        public IIncidentEventDispatcher Dispatcher { get; } = Substitute.For<IIncidentEventDispatcher>();

        public IServiceProvider Services => _services ??= new ServiceCollection()
            .AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(_dbName))
            .AddSingleton(Dispatcher)
            .BuildServiceProvider();

        private IServiceProvider? _services;

        public ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(_dbName).Options);

        public WebhookDeliveryRetryQuartzJob Job() =>
            new(Services.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<WebhookDeliveryRetryQuartzJob>.Instance);
    }

    private static WebhookDelivery DueRow(int attemptCount = 1) => new()
    {
        Id = Guid.NewGuid(),
        IncidentId = Guid.NewGuid(),
        ServiceId = Guid.NewGuid(),
        Direction = "Outbound",
        Url = "https://example.test/ack",
        AckType = "acknowledge",
        Error = "HTTP 503",
        AttemptCount = attemptCount,
        AttemptedAt = DateTime.UtcNow.AddMinutes(-5),
        NextRetryAt = DateTime.UtcNow.AddMinutes(-1),
        Status = WebhookDeliveryStatus.Retrying,
        CreatedAt = DateTime.UtcNow.AddMinutes(-5)
    };

    private static async Task<WebhookDelivery> RunWebhookSweepAsync(WebhookHarness harness, WebhookDelivery row)
    {
        await using (var seed = harness.NewContext())
        {
            seed.Set<WebhookDelivery>().Add(row);
            await seed.SaveChangesAsync();
        }

        await harness.Job().Execute(JobContext());

        await using var verify = harness.NewContext();
        return await verify.Set<WebhookDelivery>().AsNoTracking().SingleAsync(d => d.Id == row.Id);
    }

    /// <summary>A dispatch that recorded nothing leaves the row selectable by the next sweep.</summary>
    [Fact]
    public async Task NotRecorded_LeavesRowSelectableByTheNextSweep()
    {
        var harness = new WebhookHarness();
        harness.Dispatcher
            .SendServiceAckAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(AckDispatchOutcome.NotRecorded);

        var stored = await RunWebhookSweepAsync(harness, DueRow(attemptCount: 1));

        Assert.Equal(WebhookDeliveryStatus.Retrying, stored.Status);
        Assert.NotNull(stored.NextRetryAt);
        Assert.True(stored.NextRetryAt > DateTime.UtcNow);
        Assert.Equal(2, stored.AttemptCount);
    }

    /// <summary>A dispatcher that throws must not be any worse than one that returns NotRecorded.</summary>
    [Fact]
    public async Task DispatcherThrew_LeavesRowSelectableByTheNextSweep()
    {
        var harness = new WebhookHarness();
        harness.Dispatcher
            .SendServiceAckAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("boom"));

        var stored = await RunWebhookSweepAsync(harness, DueRow(attemptCount: 1));

        Assert.Equal(WebhookDeliveryStatus.Retrying, stored.Status);
        Assert.NotNull(stored.NextRetryAt);
    }

    /// <summary>
    /// A missing ACK config can be a transient gap (an admin mid-edit, a DNS blip). It is bounded by
    /// the attempt ladder rather than killed on the spot, so the row is still armed after one skip.
    /// </summary>
    [Fact]
    public async Task Skipped_RequeuesInsteadOfDyingOnTheFirstConfigGap()
    {
        var harness = new WebhookHarness();
        harness.Dispatcher
            .SendServiceAckAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(AckDispatchOutcome.Skipped);

        var stored = await RunWebhookSweepAsync(harness, DueRow(attemptCount: 1));

        Assert.Equal(WebhookDeliveryStatus.Retrying, stored.Status);
        Assert.NotNull(stored.NextRetryAt);
        Assert.Equal(2, stored.AttemptCount);
    }

    /// <summary>...but it stays bounded: the last rung of the ladder is terminal, so this cannot loop forever.</summary>
    [Fact]
    public async Task Skipped_OnTheLastAttempt_IsTerminal()
    {
        var harness = new WebhookHarness();
        harness.Dispatcher
            .SendServiceAckAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(AckDispatchOutcome.Skipped);

        var stored = await RunWebhookSweepAsync(
            harness, DueRow(attemptCount: IncidentEventDispatcher.MaxAttempts - 1));

        Assert.Equal(WebhookDeliveryStatus.Failed, stored.Status);
        Assert.Null(stored.NextRetryAt);
    }

    /// <summary>Once the dispatcher has written a fresh attempt row, the claimed row must not re-fire.</summary>
    [Fact]
    public async Task Recorded_ClosesTheClaimedRowOutWithoutInflatingItsAttemptCount()
    {
        var harness = new WebhookHarness();
        harness.Dispatcher
            .SendServiceAckAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(AckDispatchOutcome.Recorded);

        var stored = await RunWebhookSweepAsync(harness, DueRow(attemptCount: 2));

        Assert.Equal(WebhookDeliveryStatus.Failed, stored.Status);
        Assert.Null(stored.NextRetryAt);
        Assert.Equal(2, stored.AttemptCount);
        Assert.Equal("HTTP 503", stored.Error);
    }

    /// <summary>A row already at the limit is closed out without one more call to the external system.</summary>
    [Fact]
    public async Task RowAtTheAttemptLimit_IsClosedOutWithoutDispatching()
    {
        var harness = new WebhookHarness();

        var stored = await RunWebhookSweepAsync(
            harness, DueRow(attemptCount: IncidentEventDispatcher.MaxAttempts));

        Assert.Equal(WebhookDeliveryStatus.Failed, stored.Status);
        Assert.Null(stored.NextRetryAt);
        await harness.Dispatcher.DidNotReceiveWithAnyArgs()
            .SendServiceAckAsync(default, default!, default);
    }

    /// <summary>The newest row owns the chain; an older armed row closes out without dispatching.</summary>
    [Fact]
    public async Task AnArmedRowSupersededByANewerAttempt_IsClosedOutInsteadOfForkingTheChain()
    {
        var harness = new WebhookHarness();
        harness.Dispatcher
            .SendServiceAckAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(AckDispatchOutcome.Recorded);

        // The stale row: its close-out never committed, so it is still armed.
        var stale = DueRow(attemptCount: 2);

        // The row the dispatcher wrote for the same chain, moments later.
        var successor = DueRow(attemptCount: 3);
        successor.IncidentId = stale.IncidentId;
        successor.AckType = stale.AckType;
        successor.CreatedAt = stale.CreatedAt.AddSeconds(1);
        successor.NextRetryAt = DateTime.UtcNow.AddMinutes(30);   // not due yet — it owns the chain

        await using (var seed = harness.NewContext())
        {
            seed.Set<WebhookDelivery>().AddRange(stale, successor);
            await seed.SaveChangesAsync();
        }

        await harness.Job().Execute(JobContext());

        await using var verify = harness.NewContext();
        var storedStale = await verify.Set<WebhookDelivery>().AsNoTracking().SingleAsync(d => d.Id == stale.Id);

        Assert.Equal(WebhookDeliveryStatus.Failed, storedStale.Status);
        Assert.Null(storedStale.NextRetryAt);
        await harness.Dispatcher.DidNotReceiveWithAnyArgs()
            .SendServiceAckAsync(default, default!, default);

        // ...and the successor is left alone to run its own course.
        var storedSuccessor = await verify.Set<WebhookDelivery>().AsNoTracking().SingleAsync(d => d.Id == successor.Id);
        Assert.Equal(WebhookDeliveryStatus.Retrying, storedSuccessor.Status);
        Assert.NotNull(storedSuccessor.NextRetryAt);
    }

    /// <summary>A row stranded without a deadline is left alone: re-arming it is the operator's call.</summary>
    [Fact]
    public async Task APendingRowFromAnOlderVersion_IsLeftAlone()
    {
        var harness = new WebhookHarness();
        harness.Dispatcher
            .SendServiceAckAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(AckDispatchOutcome.Recorded);

        var stranded = DueRow(attemptCount: 1);
        stranded.Status = WebhookDeliveryStatus.Pending;
        stranded.NextRetryAt = null;      // how the old sweep actually left it

        var stored = await RunWebhookSweepAsync(harness, stranded);

        await harness.Dispatcher.DidNotReceiveWithAnyArgs()
            .SendServiceAckAsync(default, default!, default);
        Assert.Equal(WebhookDeliveryStatus.Pending, stored.Status);
        Assert.Equal(1, stored.AttemptCount);
    }

    /// <summary>
    /// WebhookDelivery.Error is varchar(1000). The sweep used to clamp to 1024, so a 1001..1024
    /// character provider message failed the UPDATE outright (Postgres 22001).
    /// </summary>
    [Fact]
    public void ClampError_FitsTheColumn()
    {
        var clamped = IncidentEventDispatcher.ClampError(new string('x', 5000));

        Assert.Equal(IncidentEventDispatcher.MaxErrorLength, clamped!.Length);
        Assert.Null(IncidentEventDispatcher.ClampError(null));
        Assert.Equal("short", IncidentEventDispatcher.ClampError("short"));
    }

    // ---------------------------------------------------------------- voice call retries

    private sealed class VoiceHarness
    {
        private readonly string _dbName = $"voice-retry-{Guid.NewGuid():N}";

        public ICommunicationProviderRegistry Registry { get; } = Substitute.For<ICommunicationProviderRegistry>();

        public IServiceProvider Services => _services ??= new ServiceCollection()
            .AddLogging()
            .AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(_dbName))
            .AddScoped<ICallLogRepository, CallLogRepository>()
            .AddSingleton(Registry)
            .BuildServiceProvider();

        private IServiceProvider? _services;

        public ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(_dbName).Options);

        public VoiceCallRetryQuartzJob Job() =>
            new(Services.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<VoiceCallRetryQuartzJob>.Instance);
    }

    private const string Callee = "+905550001122";
    private const string SecondCallee = "+905550003344";

    /// <summary>The due row of a voice-retry chain: a call that happened, went unanswered, and carries the next deadline.</summary>
    private static CallLog DueCall(
        Guid incidentId,
        int attemptNumber = 1,
        TimeSpan? completedAgo = null,
        TimeSpan? failingSinceAgo = null,
        VoiceDialOutFailureKind? failureKind = null)
    {
        var completedAt = DateTime.UtcNow - (completedAgo ?? TimeSpan.FromMinutes(1));
        return new CallLog
        {
            Id = Guid.NewGuid(),
            IncidentId = incidentId,
            PhoneNumber = Callee,
            Status = CallStatus.NoAnswer,
            FailureReason = "no answer",
            AttemptNumber = attemptNumber,
            InitiatedAt = completedAt.AddSeconds(-30),
            CompletedAt = completedAt,
            NextRetryAt = DateTime.UtcNow.AddSeconds(-5),
            DialOutFailingSince = failingSinceAgo is null ? null : DateTime.UtcNow - failingSinceAgo,
            DialOutFailureKind = failureKind,
            CreatedAt = completedAt.AddSeconds(-30)
        };
    }

    private static async Task<List<IncidentTimelineEvent>> TimelineAsync(VoiceHarness harness, Guid incidentId)
    {
        await using var verify = harness.NewContext();
        return await verify.Set<IncidentTimelineEvent>()
            .AsNoTracking()
            .Where(e => e.IncidentId == incidentId)
            .ToListAsync();
    }

    private static Incident OpenIncident() => new()
    {
        Id = Guid.NewGuid(),
        Title = "db down",
        Status = IncidentStatus.Open,
        Severity = IncidentSeverity.High,
        StartedAt = DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow
    };

    private static async Task SeedAsync(VoiceHarness harness, Incident incident, params CallLog[] callLogs)
    {
        await using var seed = harness.NewContext();
        seed.Incidents.Add(incident);
        seed.CallLogs.AddRange(callLogs);
        await seed.SaveChangesAsync();
    }

    private static async Task<CallLog> ReloadAsync(VoiceHarness harness, Guid callLogId)
    {
        await using var verify = harness.NewContext();
        return await verify.CallLogs.AsNoTracking().SingleAsync(c => c.Id == callLogId);
    }

    private static async Task<CallLog> RunVoiceSweepAsync(VoiceHarness harness, int attemptNumber = 1)
    {
        var incident = OpenIncident();
        var callLog = DueCall(incident.Id, attemptNumber);

        await SeedAsync(harness, incident, callLog);
        await harness.Job().Execute(JobContext());

        return await ReloadAsync(harness, callLog.Id);
    }

    private static ICommunicationProvider ProviderReturning(CallResult result)
    {
        var provider = Substitute.For<ICommunicationProvider>();
        provider.MakeCallAsync(Arg.Any<MakeCallRequest>()).Returns(result);
        return provider;
    }

    private static ICommunicationProvider ProviderThatTimesOut()
    {
        var provider = Substitute.For<ICommunicationProvider>();
        provider.MakeCallAsync(Arg.Any<MakeCallRequest>())
            .ThrowsAsync(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"));
        return provider;
    }

    /// <summary>A timeout says nothing about whether the call went out, so the row parks instead of re-dialling.</summary>
    [Fact]
    public async Task ProviderTimedOut_DoesNotDialAgain_AndDoesNotDropTheChain()
    {
        var harness = new VoiceHarness();
        var provider = ProviderThatTimesOut();
        harness.Registry.GetProvider(CommunicationCapability.VoiceCalls).Returns(provider);

        var stored = await RunVoiceSweepAsync(harness);

        await provider.Received(1).MakeCallAsync(Arg.Any<MakeCallRequest>());
        Assert.NotNull(stored.NextRetryAt);
        Assert.True(stored.NextRetryAt > DateTime.UtcNow);

        // Call history untouched: this row is still "attempt 1, no answer".
        Assert.Equal(1, stored.AttemptNumber);
        Assert.Equal("no answer", stored.FailureReason);
        Assert.Equal(CallStatus.NoAnswer, stored.Status);
    }

    /// <summary>A timed-out dial that did go out is detected by its own CallLog row, so the chain stands down.</summary>
    [Fact]
    public async Task TimedOutDialThatActuallyWentOut_StandsDownInsteadOfCallingTwice()
    {
        var harness = new VoiceHarness();
        var provider = ProviderThatTimesOut();
        harness.Registry.GetProvider(CommunicationCapability.VoiceCalls).Returns(provider);

        var incident = OpenIncident();
        var chain = DueCall(incident.Id);
        await SeedAsync(harness, incident, chain);

        await harness.Job().Execute(JobContext());          // sweep 1: dial times out, row parked
        var parked = await ReloadAsync(harness, chain.Id);
        Assert.NotNull(parked.NextRetryAt);

        // The call really did go out: VoxEngine reports it and a CallLog row appears for it.
        await using (var db = harness.NewContext())
        {
            db.CallLogs.Add(new CallLog
            {
                Id = Guid.NewGuid(),
                IncidentId = incident.Id,
                PhoneNumber = Callee,
                Status = CallStatus.Initiated,
                CallToken = "session-2",
                AttemptNumber = 2,
                InitiatedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            // Make the parked row due again.
            var row = await db.CallLogs.SingleAsync(c => c.Id == chain.Id);
            row.NextRetryAt = DateTime.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
        }

        await harness.Job().Execute(JobContext());          // sweep 2

        await provider.Received(1).MakeCallAsync(Arg.Any<MakeCallRequest>());   // still ONE dial
        var settled = await ReloadAsync(harness, chain.Id);
        Assert.Null(settled.NextRetryAt);                    // the new call owns the chain now
    }

    /// <summary>
    /// The mirror image: the timed-out dial never left the building, so no CallLog row appeared for
    /// it. The chain must NOT quietly die here — this responder has not been called.
    /// </summary>
    [Fact]
    public async Task TimedOutDialThatNeverWentOut_DialsOnTheNextSweep()
    {
        var harness = new VoiceHarness();
        var provider = Substitute.For<ICommunicationProvider>();
        provider.MakeCallAsync(Arg.Any<MakeCallRequest>())
            .Returns(_ => throw new TaskCanceledException("timeout"),
                     _ => Task.FromResult(new CallResult { Success = true, CallId = "session-2" }));
        harness.Registry.GetProvider(CommunicationCapability.VoiceCalls).Returns(provider);

        var incident = OpenIncident();
        var chain = DueCall(incident.Id);
        await SeedAsync(harness, incident, chain);

        await harness.Job().Execute(JobContext());          // sweep 1: timeout, parked

        await using (var db = harness.NewContext())
        {
            var row = await db.CallLogs.SingleAsync(c => c.Id == chain.Id);
            row.NextRetryAt = DateTime.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
        }

        await harness.Job().Execute(JobContext());          // sweep 2: nothing came of the first dial

        await provider.Received(2).MakeCallAsync(Arg.Any<MakeCallRequest>());
        Assert.Null((await ReloadAsync(harness, chain.Id)).NextRetryAt);
    }

    /// <summary>
    /// Something else already called this responder about this incident (a later escalation step,
    /// say). Firing the retry on top of that is a second call to the same person.
    /// </summary>
    [Fact]
    public async Task ANewerCallToTheSameResponder_StandsTheRetryDown()
    {
        var harness = new VoiceHarness();
        var provider = ProviderReturning(new CallResult { Success = true });
        harness.Registry.GetProvider(CommunicationCapability.VoiceCalls).Returns(provider);

        var incident = OpenIncident();
        var chain = DueCall(incident.Id);
        var newerCall = new CallLog
        {
            Id = Guid.NewGuid(),
            IncidentId = incident.Id,
            PhoneNumber = "90 555 000 11 22",     // same number, different formatting
            Status = CallStatus.Connected,
            AttemptNumber = 2,
            InitiatedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        await SeedAsync(harness, incident, chain, newerCall);
        await harness.Job().Execute(JobContext());

        await provider.DidNotReceiveWithAnyArgs().MakeCallAsync(default!);
        Assert.Null((await ReloadAsync(harness, chain.Id)).NextRetryAt);
    }

    /// <summary>Provider registry reload window: GetProvider returns null for a moment. Not a reason to stop paging.</summary>
    [Fact]
    public async Task NoProviderAvailable_ReArmsTheChain_WithoutRewritingCallHistory()
    {
        var harness = new VoiceHarness();
        harness.Registry.GetProvider(CommunicationCapability.VoiceCalls).Returns((ICommunicationProvider?)null);

        var stored = await RunVoiceSweepAsync(harness);

        Assert.NotNull(stored.NextRetryAt);
        Assert.Equal(1, stored.AttemptNumber);
        Assert.Equal("no answer", stored.FailureReason);
    }

    /// <summary>A refused dial re-arms the chain and leaves the row's own call history untouched.</summary>
    [Fact]
    public async Task ProviderRejectedTheCall_ReArmsTheChain_WithoutRewritingCallHistory()
    {
        var harness = new VoiceHarness();
        var provider = ProviderReturning(new CallResult { Success = false, ErrorMessage = "rate limited" });
        harness.Registry.GetProvider(CommunicationCapability.VoiceCalls).Returns(provider);

        var stored = await RunVoiceSweepAsync(harness);

        Assert.NotNull(stored.NextRetryAt);
        Assert.Equal("no answer", stored.FailureReason);
        Assert.Equal(1, stored.AttemptNumber);
        Assert.Equal(CallStatus.NoAnswer, stored.Status);
    }

    /// <summary>
    /// The call went out: the VoxEngine callback owns the chain from here. Re-arming would ring the
    /// same responder again while the first call is still alerting.
    /// </summary>
    [Fact]
    public async Task CallWasPlaced_DoesNotReArm()
    {
        var harness = new VoiceHarness();
        var provider = ProviderReturning(new CallResult { Success = true, CallId = "session-1" });
        harness.Registry.GetProvider(CommunicationCapability.VoiceCalls).Returns(provider);

        var stored = await RunVoiceSweepAsync(harness);

        Assert.Null(stored.NextRetryAt);
        Assert.Equal(1, stored.AttemptNumber);
    }

    /// <summary>The chain is bounded — three attempts, then it stops, and no further call is placed.</summary>
    [Fact]
    public async Task ChainAtTheAttemptLimit_StopsWithoutDialling()
    {
        var harness = new VoiceHarness();
        var provider = ProviderReturning(new CallResult { Success = true });
        harness.Registry.GetProvider(CommunicationCapability.VoiceCalls).Returns(provider);

        var stored = await RunVoiceSweepAsync(harness, attemptNumber: 3);

        Assert.Null(stored.NextRetryAt);
        await provider.DidNotReceiveWithAnyArgs().MakeCallAsync(default!);
    }

    /// <summary>A chain armed before a long Worker outage has not failed at anything yet, so it dials.</summary>
    [Fact]
    public async Task AChainArmedBeforeALongWorkerOutage_StillDials()
    {
        var harness = new VoiceHarness();
        var provider = ProviderReturning(new CallResult { Success = true, CallId = "session-1" });
        harness.Registry.GetProvider(CommunicationCapability.VoiceCalls).Returns(provider);

        var incident = OpenIncident();
        var chain = DueCall(incident.Id, completedAgo: TimeSpan.FromHours(9));   // worker was away all night
        await SeedAsync(harness, incident, chain);

        await harness.Job().Execute(JobContext());

        await provider.Received(1).MakeCallAsync(Arg.Any<MakeCallRequest>());
        Assert.Empty(await TimelineAsync(harness, incident.Id));
    }

    /// <summary>A cold registry on the first tick is a question never put to a provider, so the chain survives it.</summary>
    [Fact]
    public async Task ColdProviderRegistryOnTheFirstTickAfterAnOutage_DoesNotGiveUp()
    {
        var harness = new VoiceHarness();
        harness.Registry.GetProvider(CommunicationCapability.VoiceCalls).Returns((ICommunicationProvider?)null);

        var incident = OpenIncident();
        var chain = DueCall(incident.Id, completedAgo: TimeSpan.FromHours(9));
        await SeedAsync(harness, incident, chain);

        await harness.Job().Execute(JobContext());

        var stored = await ReloadAsync(harness, chain.Id);
        Assert.NotNull(stored.NextRetryAt);                     // still the chain — it will dial once the registry warms
        Assert.Empty(await TimelineAsync(harness, incident.Id)); // and nobody has been told a provider refused anything
    }

    /// <summary>The first failed dial stamps the marker the dial-out window is measured from.</summary>
    [Fact]
    public async Task TheFirstFailedDial_StampsTheWindowAnchor()
    {
        var harness = new VoiceHarness();
        var provider = ProviderReturning(new CallResult { Success = false, ErrorMessage = "rate limited" });
        harness.Registry.GetProvider(CommunicationCapability.VoiceCalls).Returns(provider);

        var incident = OpenIncident();
        var chain = DueCall(incident.Id, completedAgo: TimeSpan.FromHours(9));
        await SeedAsync(harness, incident, chain);

        await harness.Job().Execute(JobContext());

        var stored = await ReloadAsync(harness, chain.Id);
        Assert.NotNull(stored.NextRetryAt);
        Assert.NotNull(stored.DialOutFailingSince);
        Assert.True(stored.DialOutFailingSince > DateTime.UtcNow.AddMinutes(-1),
            $"the window must start at the FIRST FAILURE, not at some older timestamp: {stored.DialOutFailingSince}");
    }

    /// <summary>A dial that gets out ends the stuck period, so it clears the marker.</summary>
    [Fact]
    public async Task ADialThatGetsOut_ClearsTheStuckMarker()
    {
        var harness = new VoiceHarness();
        var provider = ProviderReturning(new CallResult { Success = true, CallId = "session-1" });
        harness.Registry.GetProvider(CommunicationCapability.VoiceCalls).Returns(provider);

        var incident = OpenIncident();
        var chain = DueCall(incident.Id, failingSinceAgo: TimeSpan.FromMinutes(40));
        await SeedAsync(harness, incident, chain);

        await harness.Job().Execute(JobContext());

        Assert.Null((await ReloadAsync(harness, chain.Id)).DialOutFailingSince);
    }

    /// <summary>
    /// A chain that stands down (a human has the incident) clears the marker too — the stuck period
    /// it belonged to is over.
    /// </summary>
    [Fact]
    public async Task AChainThatStandsDown_ClearsTheStuckMarker()
    {
        var harness = new VoiceHarness();
        var provider = ProviderReturning(new CallResult { Success = true });
        harness.Registry.GetProvider(CommunicationCapability.VoiceCalls).Returns(provider);

        var incident = OpenIncident();
        incident.Status = IncidentStatus.Acknowledged;
        var chain = DueCall(incident.Id, failingSinceAgo: TimeSpan.FromMinutes(40));
        await SeedAsync(harness, incident, chain);

        await harness.Job().Execute(JobContext());

        var stored = await ReloadAsync(harness, chain.Id);
        Assert.Null(stored.NextRetryAt);
        Assert.Null(stored.DialOutFailingSince);
    }

    /// <summary>A dial that never gets out is bounded by wall clock rather than an attempt counter.</summary>
    [Fact]
    public async Task DialThatKeepsFailingToGetOut_StopsAfterTheDialOutWindow()
    {
        var harness = new VoiceHarness();
        var provider = ProviderReturning(new CallResult { Success = false, ErrorMessage = "rate limited" });
        harness.Registry.GetProvider(CommunicationCapability.VoiceCalls).Returns(provider);

        var incident = OpenIncident();
        var chain = DueCall(incident.Id, failingSinceAgo: TimeSpan.FromMinutes(90));
        await SeedAsync(harness, incident, chain);

        await harness.Job().Execute(JobContext());

        Assert.Null((await ReloadAsync(harness, chain.Id)).NextRetryAt);
    }

    /// <summary>The ambiguous path is bounded by the dial-out window like every other path.</summary>
    [Fact]
    public async Task AmbiguousDialThatKeepsTimingOut_StopsAfterTheDialOutWindow()
    {
        var harness = new VoiceHarness();
        var provider = ProviderThatTimesOut();
        harness.Registry.GetProvider(CommunicationCapability.VoiceCalls).Returns(provider);

        var incident = OpenIncident();
        var chain = DueCall(incident.Id, failingSinceAgo: TimeSpan.FromMinutes(90));
        await SeedAsync(harness, incident, chain);

        await harness.Job().Execute(JobContext());

        Assert.Null((await ReloadAsync(harness, chain.Id)).NextRetryAt);
    }

    /// <summary>No voice provider at all is bounded too, but on a much longer leash than a refusal.</summary>
    [Fact]
    public async Task NoVoiceProviderAtAll_EventuallyStops_ButOnALongerLeash()
    {
        var harness = new VoiceHarness();
        harness.Registry.GetProvider(CommunicationCapability.VoiceCalls).Returns((ICommunicationProvider?)null);

        var incident = OpenIncident();
        var stillTrying = DueCall(incident.Id, failingSinceAgo: TimeSpan.FromHours(2));
        await SeedAsync(harness, incident, stillTrying);

        await harness.Job().Execute(JobContext());

        // Two hours of no provider would have stopped a dial that a provider was refusing. Not this one.
        Assert.NotNull((await ReloadAsync(harness, stillTrying.Id)).NextRetryAt);
    }

    /// <summary>A chain that gives up says so on the incident timeline, not only in a worker log.</summary>
    [Fact]
    public async Task AChainThatGivesUp_SaysSoOnTheIncidentTimeline()
    {
        var harness = new VoiceHarness();
        var provider = ProviderReturning(new CallResult { Success = false, ErrorMessage = "rate limited" });
        harness.Registry.GetProvider(CommunicationCapability.VoiceCalls).Returns(provider);

        var incident = OpenIncident();
        var chain = DueCall(incident.Id, failingSinceAgo: TimeSpan.FromMinutes(90));
        await SeedAsync(harness, incident, chain);

        await harness.Job().Execute(JobContext());

        var gaveUp = Assert.Single(await TimelineAsync(harness, incident.Id));
        Assert.Equal(TimelineEventType.CallFailed, gaveUp.EventType);
        Assert.Contains("Gave up", gaveUp.Description);
        Assert.Contains("would not place the call", gaveUp.Description);
    }

    /// <summary>A chain that never found a provider must not report it as a provider refusal.</summary>
    [Fact]
    public async Task AChainThatNeverFoundAProvider_DoesNotBlameTheProvider()
    {
        var harness = new VoiceHarness();
        harness.Registry.GetProvider(CommunicationCapability.VoiceCalls).Returns((ICommunicationProvider?)null);

        var incident = OpenIncident();
        var chain = DueCall(incident.Id, failingSinceAgo: TimeSpan.FromHours(7));
        await SeedAsync(harness, incident, chain);

        await harness.Job().Execute(JobContext());

        Assert.Null((await ReloadAsync(harness, chain.Id)).NextRetryAt);

        var gaveUp = Assert.Single(await TimelineAsync(harness, incident.Id));
        Assert.Contains("never attempted", gaveUp.Description);
        Assert.DoesNotContain("would not place the call", gaveUp.Description);
    }

    /// <summary>
    /// While within the window, the ambiguous dial is still parked rather than re-dialled — and it is
    /// parked long enough for a call that really did go out to have produced its CallLog row.
    /// </summary>
    [Fact]
    public async Task AmbiguousDialWithinTheWindow_IsParkedLongEnoughToVerify()
    {
        var harness = new VoiceHarness();
        var provider = ProviderThatTimesOut();
        harness.Registry.GetProvider(CommunicationCapability.VoiceCalls).Returns(provider);

        var stored = await RunVoiceSweepAsync(harness);

        Assert.NotNull(stored.NextRetryAt);
        Assert.True(stored.NextRetryAt >= DateTime.UtcNow.AddSeconds(90),
            $"ambiguous park was too short to observe a call that went out: {stored.NextRetryAt}");
    }

    /// <summary>A non-terminal status somebody is already working stands every armed retry of that incident down.</summary>
    [Theory]
    [InlineData(IncidentStatus.Acknowledged)]
    [InlineData(IncidentStatus.Investigating)]
    [InlineData(IncidentStatus.Mitigated)]
    public async Task AnIncidentSomebodyIsAlreadyWorking_StandsEveryArmedRetryDown(IncidentStatus status)
    {
        var harness = new VoiceHarness();
        var provider = ProviderReturning(new CallResult { Success = true });
        harness.Registry.GetProvider(CommunicationCapability.VoiceCalls).Returns(provider);

        var incident = OpenIncident();
        incident.Status = status;

        var primary = DueCall(incident.Id);
        var secondOnCall = DueCall(incident.Id);
        secondOnCall.PhoneNumber = "+905550003344";

        await SeedAsync(harness, incident, primary, secondOnCall);
        await harness.Job().Execute(JobContext());

        await provider.DidNotReceiveWithAnyArgs().MakeCallAsync(default!);
        Assert.Null((await ReloadAsync(harness, primary.Id)).NextRetryAt);
        Assert.Null((await ReloadAsync(harness, secondOnCall.Id)).NextRetryAt);
    }

    /// <summary>An acknowledge that lands while the dial is in flight must not be re-armed over.</summary>
    [Fact]
    public async Task AcknowledgedWhileTheDialWasInFlight_DoesNotReArm()
    {
        var harness = new VoiceHarness();

        var incident = OpenIncident();
        var chain = DueCall(incident.Id);
        await SeedAsync(harness, incident, chain);

        var provider = Substitute.For<ICommunicationProvider>();
        provider.MakeCallAsync(Arg.Any<MakeCallRequest>()).Returns(_ =>
        {
            // The operator acknowledges from the UI while our HTTP request is still open.
            using var ui = harness.NewContext();
            var live = ui.Incidents.Single(i => i.Id == incident.Id);
            live.Status = IncidentStatus.Acknowledged;
            ui.SaveChanges();

            return new CallResult { Success = false, ErrorMessage = "rate limited" };
        });
        harness.Registry.GetProvider(CommunicationCapability.VoiceCalls).Returns(provider);

        await harness.Job().Execute(JobContext());

        Assert.Null((await ReloadAsync(harness, chain.Id)).NextRetryAt);
    }

    /// <summary>Incident status is re-read per row immediately before each dial, not taken from the batch's tracked copy.</summary>
    [Fact]
    public async Task AcknowledgedDuringTheFirstDial_DoesNotRingTheSecondOnCall()
    {
        var harness = new VoiceHarness();

        var incident = OpenIncident();

        // Both rows are due, so both are selected — and their incident tracked as Open — before
        // either dial goes out. The primary's deadline is older, so it is dialled first.
        var primary = DueCall(incident.Id);
        primary.NextRetryAt = DateTime.UtcNow.AddMinutes(-2);

        var secondOnCall = DueCall(incident.Id);
        secondOnCall.PhoneNumber = SecondCallee;
        secondOnCall.NextRetryAt = DateTime.UtcNow.AddMinutes(-1);

        await SeedAsync(harness, incident, primary, secondOnCall);

        var dialled = new List<string>();
        var provider = Substitute.For<ICommunicationProvider>();
        provider.MakeCallAsync(Arg.Any<MakeCallRequest>()).Returns(call =>
        {
            dialled.Add(call.Arg<MakeCallRequest>().Destination);

            // T+2s: the primary answers and picks the incident up from the UI, while this first call
            // is still ringing and the sweep still has the second on-call's row to process.
            using var ui = harness.NewContext();
            var live = ui.Incidents.Single(i => i.Id == incident.Id);
            live.Status = IncidentStatus.Acknowledged;
            ui.SaveChanges();

            return new CallResult { Success = true };
        });
        harness.Registry.GetProvider(CommunicationCapability.VoiceCalls).Returns(provider);

        await harness.Job().Execute(JobContext());

        Assert.Equal([Callee], dialled);
        Assert.DoesNotContain(SecondCallee, dialled);

        // ...and the second on-call's chain is stood down rather than left armed to ring later.
        Assert.Null((await ReloadAsync(harness, secondOnCall.Id)).NextRetryAt);
    }

    /// <summary>Same race, ambiguous flavour: a provider that throws must not resurrect the chain either.</summary>
    [Fact]
    public async Task AcknowledgedWhileAnAmbiguousDialWasInFlight_DoesNotReArm()
    {
        var harness = new VoiceHarness();

        var incident = OpenIncident();
        var chain = DueCall(incident.Id);
        await SeedAsync(harness, incident, chain);

        var provider = Substitute.For<ICommunicationProvider>();
        provider.MakeCallAsync(Arg.Any<MakeCallRequest>()).Returns<CallResult>(_ =>
        {
            using var ui = harness.NewContext();
            var live = ui.Incidents.Single(i => i.Id == incident.Id);
            live.Status = IncidentStatus.Acknowledged;
            ui.SaveChanges();

            throw new TaskCanceledException("timeout");
        });
        harness.Registry.GetProvider(CommunicationCapability.VoiceCalls).Returns(provider);

        await harness.Job().Execute(JobContext());

        Assert.Null((await ReloadAsync(harness, chain.Id)).NextRetryAt);
    }

    // ------------------------------------------------- the claim: no unrecoverable row, ever

    /// <summary>Mid-dial the durable row is claimed — pushed out of the due window — never cleared to null.</summary>
    [Fact]
    public async Task WhileTheDialIsInFlight_TheRowIsClaimed_NotCleared()
    {
        var harness = new VoiceHarness();

        var incident = OpenIncident();
        var chain = DueCall(incident.Id);
        await SeedAsync(harness, incident, chain);

        DateTime? deadlineMidDial = null;
        var provider = Substitute.For<ICommunicationProvider>();
        provider.MakeCallAsync(Arg.Any<MakeCallRequest>()).Returns(_ =>
        {
            using var db = harness.NewContext();
            deadlineMidDial = db.CallLogs.AsNoTracking().Single(c => c.Id == chain.Id).NextRetryAt;
            return new CallResult { Success = true, CallId = "session-1" };
        });
        harness.Registry.GetProvider(CommunicationCapability.VoiceCalls).Returns(provider);

        await harness.Job().Execute(JobContext());

        Assert.True(deadlineMidDial is not null,
            "the row was CLEARED before the dial: if anything after this point fails to commit, nothing "
            + "will ever select this row again — the responder is never called and nobody is told");
        Assert.True(deadlineMidDial > DateTime.UtcNow,
            $"the claim must take the row out of the due window or the same retry can fire twice: {deadlineMidDial}");

        // ...and the successful dial still releases it: the call's own CallLog row owns the chain now.
        Assert.Null((await ReloadAsync(harness, chain.Id)).NextRetryAt);
    }

    /// <summary>The same claim on the failing path, where the re-arm is a separate commit that can be lost.</summary>
    [Fact]
    public async Task WhileAFailingDialIsInFlight_TheRowIsClaimed_NotCleared()
    {
        var harness = new VoiceHarness();

        var incident = OpenIncident();
        var chain = DueCall(incident.Id);
        await SeedAsync(harness, incident, chain);

        DateTime? deadlineMidDial = null;
        var provider = Substitute.For<ICommunicationProvider>();
        provider.MakeCallAsync(Arg.Any<MakeCallRequest>()).Returns(_ =>
        {
            using var db = harness.NewContext();
            deadlineMidDial = db.CallLogs.AsNoTracking().Single(c => c.Id == chain.Id).NextRetryAt;
            return new CallResult { Success = false, ErrorMessage = "rate limited" };
        });
        harness.Registry.GetProvider(CommunicationCapability.VoiceCalls).Returns(provider);

        await harness.Job().Execute(JobContext());

        Assert.NotNull(deadlineMidDial);
        Assert.True(deadlineMidDial > DateTime.UtcNow);
        Assert.NotNull((await ReloadAsync(harness, chain.Id)).NextRetryAt);   // and the re-arm still lands
    }

    // ------------------------------------------- mixed-mode: one marker, two windows, no early give-up

    /// <summary>A stuck period is judged by the most lenient failure it has seen, so one refusal cannot end it early.</summary>
    [Fact]
    public async Task AChainStuckWithNoProvider_IsNotGivenUpOnItsFirstRefusal()
    {
        var harness = new VoiceHarness();
        var provider = ProviderReturning(new CallResult { Success = false, ErrorMessage = "rate limited" });
        harness.Registry.GetProvider(CommunicationCapability.VoiceCalls).Returns(provider);

        var incident = OpenIncident();
        var chain = DueCall(
            incident.Id,
            failingSinceAgo: TimeSpan.FromHours(5.5),                 // well past the 1h refusal window...
            failureKind: VoiceDialOutFailureKind.ProviderMissing);    // ...but every minute of it was "nobody to ask"

        await SeedAsync(harness, incident, chain);
        await harness.Job().Execute(JobContext());

        var stored = await ReloadAsync(harness, chain.Id);
        Assert.NotNull(stored.NextRetryAt);
        Assert.Empty(await TimelineAsync(harness, incident.Id));

        // The lenient kind stays: one refusal does not shorten a window that is already running.
        Assert.Equal(VoiceDialOutFailureKind.ProviderMissing, stored.DialOutFailureKind);
    }

    /// <summary>A mixed stuck period still stops, because the anchor is not reset when the failure kind changes.</summary>
    [Fact]
    public async Task AMixedStuckPeriod_StillStops_AtTheLongestSingleWindow()
    {
        var harness = new VoiceHarness();
        var provider = ProviderReturning(new CallResult { Success = false, ErrorMessage = "rate limited" });
        harness.Registry.GetProvider(CommunicationCapability.VoiceCalls).Returns(provider);

        var incident = OpenIncident();
        var chain = DueCall(
            incident.Id,
            failingSinceAgo: TimeSpan.FromHours(6.5),
            failureKind: VoiceDialOutFailureKind.ProviderMissing);

        await SeedAsync(harness, incident, chain);
        await harness.Job().Execute(JobContext());

        var stored = await ReloadAsync(harness, chain.Id);
        Assert.Null(stored.NextRetryAt);
        Assert.Null(stored.DialOutFailureKind);

        // And it says what actually happened — BOTH things. An operator reading "the provider would not
        // place the call" goes to debug the provider; for most of that window there was no provider to
        // debug, and that is the fault they need to be sent to.
        var gaveUp = Assert.Single(await TimelineAsync(harness, incident.Id));
        Assert.Contains("would not place the call", gaveUp.Description);
        Assert.Contains("no voice provider available at all", gaveUp.Description);
    }

    /// <summary>Losing the provider mid-chain lengthens the leash without restarting the clock.</summary>
    [Fact]
    public async Task WhenTheProviderDisappearsMidChain_TheLeashLengthens_ButTheClockDoesNot()
    {
        var harness = new VoiceHarness();
        harness.Registry.GetProvider(CommunicationCapability.VoiceCalls).Returns((ICommunicationProvider?)null);

        var incident = OpenIncident();
        var chain = DueCall(
            incident.Id,
            failingSinceAgo: TimeSpan.FromMinutes(50),
            failureKind: VoiceDialOutFailureKind.ProviderRefused);
        var anchorBefore = chain.DialOutFailingSince;

        await SeedAsync(harness, incident, chain);
        await harness.Job().Execute(JobContext());

        var stored = await ReloadAsync(harness, chain.Id);
        Assert.NotNull(stored.NextRetryAt);
        Assert.Equal(VoiceDialOutFailureKind.ProviderMissing, stored.DialOutFailureKind);
        Assert.Equal(anchorBefore, stored.DialOutFailingSince);
    }

    /// <summary>
    /// A legacy row — armed before the kind column existed, so it carries a marker and no kind. It is
    /// judged by the failure it actually meets, which is the reading that gives it its full window.
    /// </summary>
    [Fact]
    public async Task ARowWithAMarkerButNoRecordedKind_IsJudgedByTheFailureItMeets()
    {
        var harness = new VoiceHarness();
        harness.Registry.GetProvider(CommunicationCapability.VoiceCalls).Returns((ICommunicationProvider?)null);

        var incident = OpenIncident();
        var legacy = DueCall(incident.Id, failingSinceAgo: TimeSpan.FromHours(2), failureKind: null);
        await SeedAsync(harness, incident, legacy);

        await harness.Job().Execute(JobContext());

        // Two hours would have exhausted a refusal window. It is not a refusal: nobody has been asked.
        var stored = await ReloadAsync(harness, legacy.Id);
        Assert.NotNull(stored.NextRetryAt);
        Assert.Equal(VoiceDialOutFailureKind.ProviderMissing, stored.DialOutFailureKind);
    }

    /// <summary>A plain refusal still records itself, and a plain refusal still stops at its own window.</summary>
    [Fact]
    public async Task ARefusedDial_RecordsTheKindThatGovernsTheStuckPeriod()
    {
        var harness = new VoiceHarness();
        var provider = ProviderReturning(new CallResult { Success = false, ErrorMessage = "rate limited" });
        harness.Registry.GetProvider(CommunicationCapability.VoiceCalls).Returns(provider);

        var stored = await RunVoiceSweepAsync(harness);

        Assert.Equal(VoiceDialOutFailureKind.ProviderRefused, stored.DialOutFailureKind);
        Assert.NotNull(stored.DialOutFailingSince);
    }

    /// <summary>A dial that gets out ends the stuck period: marker AND kind go with it.</summary>
    [Fact]
    public async Task ADialThatGetsOut_ClearsTheRecordedKindToo()
    {
        var harness = new VoiceHarness();
        var provider = ProviderReturning(new CallResult { Success = true, CallId = "session-1" });
        harness.Registry.GetProvider(CommunicationCapability.VoiceCalls).Returns(provider);

        var incident = OpenIncident();
        var chain = DueCall(
            incident.Id,
            failingSinceAgo: TimeSpan.FromMinutes(40),
            failureKind: VoiceDialOutFailureKind.ProviderRefused);
        await SeedAsync(harness, incident, chain);

        await harness.Job().Execute(JobContext());

        var stored = await ReloadAsync(harness, chain.Id);
        Assert.Null(stored.DialOutFailingSince);
        Assert.Null(stored.DialOutFailureKind);
    }
}
