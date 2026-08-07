using System.Reflection;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Common.Models.Persistence;
using Callu.Application.Providers;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Services;
using Callu.Infrastructure.Telemetry;
using Callu.Shared.Models.Communication;
using Callu.Shared.Models.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Callu.Tests;

/// <summary>Drives the two phone channels — the voice call and the SMS — over the decisions that end in a phone ringing.</summary>
public class PhoneChannelDispatcherTests : IDisposable
{
    private const string UserId = "responder-1";
    private const string Phone = "+905551112233";

    private readonly ApplicationDbContext _ctx =
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"phone-channel-{Guid.NewGuid():N}").Options);

    private readonly IUserContactRepository _contacts = Substitute.For<IUserContactRepository>();

    public PhoneChannelDispatcherTests() =>
        _contacts.GetContactByIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new UserContactSnapshot(UserId, "Responder", Phone, "responder@example.io"));

    public void Dispose() => _ctx.Dispose();

    // ─────────────────────────────────────────── 1. no provider is not a refusal

    /// <summary>A first voice dispatch into a warming registry stays retriable with its budget untouched.</summary>
    [Fact]
    public async Task FirstVoiceDispatch_WithAColdRegistry_IsRetriable_NotSkipped()
    {
        var notification = Page(NotificationType.VoiceCall);

        await Voice(provider: null).SendAsync(notification, null, Phone, Payload(), null);

        Assert.Equal(NotificationDeliveryStatus.Pending, notification.DeliveryStatus);
        Assert.NotNull(notification.NextRetryAt);

        // The retry sweep selects DeliveryStatus IN (Pending, Failed) with RetryCount < 3. Skipped and
        // PermanentlyFailed are outside it — which is exactly why the old code lost the page.
        Assert.NotEqual(NotificationDeliveryStatus.Skipped, notification.DeliveryStatus);
        Assert.NotEqual(NotificationDeliveryStatus.PermanentlyFailed, notification.DeliveryStatus);

        // A provider that was never asked must not spend the row's retry budget: three registry
        // reloads would otherwise exhaust a page that had not been attempted once.
        Assert.Equal(0, notification.RetryCount);

        // ...and BECAUSE the budget is intact and the deadline is set, this page is on its way: the
        // retry sweep owns the row and will dial it in sixty seconds. Saying otherwise is what let the
        // escalation treat the step as having nothing in flight — it backdated the step clock and ran a
        // three-step policy to exhaustion in thirty seconds without dialling once.
        Assert.True(notification.IsDeferredPage);
        Assert.True(notification.PageIsOnItsWay);
        Assert.True(notification.RetrySweepWillTakeIt);
    }

    /// <summary>The same window, on the retry path. It used to be MarkPermanentlyFailed — a page killed outright.</summary>
    [Fact]
    public async Task VoiceRetry_WithAColdRegistry_StaysRetriable_InsteadOfDyingPermanently()
    {
        var incidentId = await SeedIncidentAsync(IncidentStatus.Open);
        var notification = Page(NotificationType.VoiceCall, incidentId);

        await Voice(provider: null).RetryAsync(notification, baseUrl: null);

        Assert.Equal(NotificationDeliveryStatus.Pending, notification.DeliveryStatus);
        Assert.NotNull(notification.NextRetryAt);
        Assert.True(notification.PageIsOnItsWay);
    }

    /// <summary>SMS is the sibling that never got the fix. Same rule, same channel-agnostic code path.</summary>
    [Fact]
    public async Task SmsRetry_WithAColdRegistry_StaysRetriable_InsteadOfDyingPermanently()
    {
        var incidentId = await SeedIncidentAsync(IncidentStatus.Open);
        var notification = Page(NotificationType.Sms, incidentId);

        await Sms(provider: null).RetryAsync(notification, baseUrl: null);

        Assert.Equal(NotificationDeliveryStatus.Pending, notification.DeliveryStatus);
        Assert.NotNull(notification.NextRetryAt);
    }

    /// <summary>Once the provider-unavailable window is spent, Skipped is finally the honest answer.</summary>
    [Fact]
    public async Task AProviderThatNeverAppears_EventuallyStops_RatherThanRetryingForever()
    {
        var incidentId = await SeedIncidentAsync(IncidentStatus.Open);
        var notification = Page(NotificationType.VoiceCall, incidentId);
        notification.CreatedAt =
            DateTime.UtcNow - PhoneChannelDispatcher.ProviderUnavailableWindow - TimeSpan.FromMinutes(1);

        await Voice(provider: null).RetryAsync(notification, baseUrl: null);

        Assert.Equal(NotificationDeliveryStatus.Skipped, notification.DeliveryStatus);
        Assert.Null(notification.NextRetryAt);
        Assert.False(notification.PageIsOnItsWay);
    }

    /// <summary>SMS lost pages in the same window, and its first dispatch was never driven either.</summary>
    [Fact]
    public async Task FirstSmsDispatch_WithAColdRegistry_IsRetriable_NotSkipped()
    {
        var notification = Page(NotificationType.Sms);

        await Sms(provider: null).SendAsync(notification, null, Phone, Payload(), null);

        Assert.Equal(NotificationDeliveryStatus.Pending, notification.DeliveryStatus);
        Assert.NotNull(notification.NextRetryAt);
        Assert.Equal(0, notification.RetryCount);
        Assert.True(notification.PageIsOnItsWay);
    }

    // ─────────────────────────────────── 1b. ...but a provider that WAS asked is a different fact

    /// <summary>A provider that refuses has been asked, so the attempt is spent and the page is still on its way.</summary>
    [Fact]
    public async Task FirstVoiceDispatch_WhoseProviderRefuses_SpendsAnAttempt_AndStaysOnItsWay()
    {
        var notification = Page(NotificationType.VoiceCall);
        var provider = Substitute.For<ICommunicationProvider>();
        provider.MakeCallAsync(Arg.Any<MakeCallRequest>())
            .Returns(new CallResult { Success = false, ErrorMessage = "number unreachable" });

        await Voice(provider).SendAsync(notification, null, Phone, Payload(), null);

        Assert.Equal(NotificationDeliveryStatus.Failed, notification.DeliveryStatus);
        Assert.Equal(1, notification.RetryCount);
        Assert.NotNull(notification.NextRetryAt);
        Assert.True(notification.PageIsOnItsWay, "the provider was asked and the retry sweep has the row");
    }

    /// <summary>A provider that blows up is an answer too — the row must not escape the retry window.</summary>
    [Fact]
    public async Task FirstVoiceDispatch_WhoseProviderThrows_IsFailed_NotLost()
    {
        var notification = Page(NotificationType.VoiceCall);
        var provider = Substitute.For<ICommunicationProvider>();
        provider.MakeCallAsync(Arg.Any<MakeCallRequest>())
            .Returns<CallResult>(_ => throw new InvalidOperationException("the SIP trunk is down"));

        await Voice(provider).SendAsync(notification, null, Phone, Payload(), null);

        Assert.Equal(NotificationDeliveryStatus.Failed, notification.DeliveryStatus);
        Assert.NotNull(notification.NextRetryAt);
    }

    /// <summary>
    /// And the attempt budget is real: the last refusal ends the chain rather than looping. This is the
    /// one place PermanentlyFailed is honest — three providers said no, out loud.
    /// </summary>
    [Fact]
    public async Task AVoiceRetryOnItsLastAttempt_WhoseProviderRefuses_DiesRatherThanLoopingForever()
    {
        var incidentId = await SeedIncidentAsync(IncidentStatus.Open);
        var notification = Page(NotificationType.VoiceCall, incidentId);
        notification.RetryCount = Notification.MaxRetries - 1;

        var provider = Substitute.For<ICommunicationProvider>();
        provider.MakeCallAsync(Arg.Any<MakeCallRequest>())
            .Returns(new CallResult { Success = false, ErrorMessage = "number unreachable" });

        await Voice(provider).RetryAsync(notification, baseUrl: null);

        Assert.Equal(NotificationDeliveryStatus.PermanentlyFailed, notification.DeliveryStatus);
        Assert.Null(notification.NextRetryAt);
    }

    /// <summary>
    /// A responder with no phone number cannot be called by any number of retries. Permanent is right
    /// here — and it is NOT what a missing provider gets, which is the whole distinction.
    /// </summary>
    [Fact]
    public async Task ARetryForAResponderWithNoPhoneNumber_StopsPermanently()
    {
        _contacts.GetContactByIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new UserContactSnapshot(UserId, "Responder", null, "responder@example.io"));

        var incidentId = await SeedIncidentAsync(IncidentStatus.Open);
        var notification = Page(NotificationType.VoiceCall, incidentId);

        var provider = WorkingProvider();
        await Voice(provider).RetryAsync(notification, baseUrl: null);

        await provider.DidNotReceive().MakeCallAsync(Arg.Any<MakeCallRequest>());
        Assert.Equal(NotificationDeliveryStatus.PermanentlyFailed, notification.DeliveryStatus);
        Assert.Null(notification.NextRetryAt);
    }

    // ─────────────────────────────────────────── 2. do not ring a phone nobody needs to answer

    /// <summary>A voice retry stands down for an incident acknowledged while the page sat in its backoff.</summary>
    [Fact]
    public async Task VoiceRetry_ForAnIncidentAcknowledgedAfterThePageWasQueued_DoesNotRingThePhone()
    {
        var incidentId = await SeedIncidentAsync(
            IncidentStatus.Acknowledged, acknowledgedAt: DateTime.UtcNow.AddMinutes(-1));

        var notification = Page(NotificationType.VoiceCall, incidentId);
        notification.CreatedAt = DateTime.UtcNow.AddMinutes(-5); // queued BEFORE the ack

        var provider = WorkingProvider();
        await Voice(provider).RetryAsync(notification, baseUrl: null);

        await provider.DidNotReceive().MakeCallAsync(Arg.Any<MakeCallRequest>());
        Assert.Equal(NotificationDeliveryStatus.Skipped, notification.DeliveryStatus);
    }

    /// <summary>Resolved or Closed ends every channel. This one the voice path already had.</summary>
    [Theory]
    [InlineData(IncidentStatus.Resolved)]
    [InlineData(IncidentStatus.Closed)]
    public async Task VoiceRetry_ForAnIncidentThatIsOver_DoesNotRingThePhone(IncidentStatus status)
    {
        var incidentId = await SeedIncidentAsync(status);
        var notification = Page(NotificationType.VoiceCall, incidentId);

        var provider = WorkingProvider();
        await Voice(provider).RetryAsync(notification, baseUrl: null);

        await provider.DidNotReceive().MakeCallAsync(Arg.Any<MakeCallRequest>());
        Assert.Equal(NotificationDeliveryStatus.Skipped, notification.DeliveryStatus);
    }

    /// <summary>
    /// The SMS half of the same bug, and it had NO guard whatsoever: a resolved incident still sent
    /// messages about itself.
    /// </summary>
    [Fact]
    public async Task SmsRetry_ForAResolvedIncident_SendsNothing()
    {
        var incidentId = await SeedIncidentAsync(IncidentStatus.Resolved);
        var notification = Page(NotificationType.Sms, incidentId);

        var provider = WorkingProvider();
        await Sms(provider).RetryAsync(notification, baseUrl: null);

        await provider.DidNotReceive().SendSmsAsync(Arg.Any<SendSmsRequest>());
        Assert.Equal(NotificationDeliveryStatus.Skipped, notification.DeliveryStatus);
    }

    /// <summary>An SMS rings nobody, so an acknowledged incident does not suppress its retry.</summary>
    [Theory]
    [InlineData(IncidentStatus.Acknowledged)]
    [InlineData(IncidentStatus.Investigating)]
    public async Task SmsRetry_ForAnIncidentSomebodyHasTakenOver_StillTellsTheResponder(IncidentStatus status)
    {
        var incidentId = await SeedIncidentAsync(status, acknowledgedAt: DateTime.UtcNow);
        var notification = Page(NotificationType.Sms, incidentId);
        notification.CreatedAt = DateTime.UtcNow.AddMinutes(-5);

        var provider = WorkingProvider();
        await Sms(provider).RetryAsync(notification, baseUrl: null);

        await provider.Received(1).SendSmsAsync(Arg.Is<SendSmsRequest>(r => r.To == Phone));
        Assert.Equal(NotificationDeliveryStatus.Delivered, notification.DeliveryStatus);
    }

    // ─────────────────────────────────────────── 3. ...without throwing away the page that matters

    /// <summary>A page queued after the ack is a deliberate one-shot, so its retry still places the call.</summary>
    [Fact]
    public async Task VoiceRetry_ForAPageQueuedAfterTheAck_StillPlacesTheCall()
    {
        var incidentId = await SeedIncidentAsync(
            IncidentStatus.Acknowledged, acknowledgedAt: DateTime.UtcNow.AddMinutes(-10));

        var notification = Page(NotificationType.VoiceCall, incidentId);
        notification.CreatedAt = DateTime.UtcNow.AddMinutes(-1); // queued AFTER the ack: press-2 one-shot

        var provider = WorkingProvider();
        await Voice(provider).RetryAsync(notification, baseUrl: null);

        await provider.Received(1).MakeCallAsync(Arg.Is<MakeCallRequest>(r => r.Destination == Phone));
        Assert.Equal(NotificationDeliveryStatus.Delivered, notification.DeliveryStatus);
    }

    // ─────────────────────────────────────────── 4. Skipped means nothing is coming. Ever.

    /// <summary>Both ways this channel reaches Skipped are checked against the full sentence it stands for.</summary>
    [Theory]
    [InlineData(IncidentStatus.Resolved)]   // the incident is over: nothing left to say to anyone
    [InlineData(IncidentStatus.Closed)]
    public async Task EverySkippedRow_HasNoRetryComing_AndClaimsNoPage(IncidentStatus over)
    {
        var incidentId = await SeedIncidentAsync(over);
        var notification = Page(NotificationType.VoiceCall, incidentId);

        await Voice(WorkingProvider()).RetryAsync(notification, baseUrl: null);

        AssertSkippedMeansNothingIsComing(notification);
    }

    /// <summary>The other route to Skipped: the provider never turned up and the window is spent.</summary>
    [Fact]
    public async Task TheSkippedRowOfAnExhaustedProviderWindow_AlsoHasNothingComing()
    {
        var incidentId = await SeedIncidentAsync(IncidentStatus.Open);
        var notification = Page(NotificationType.Sms, incidentId);
        notification.CreatedAt =
            DateTime.UtcNow - PhoneChannelDispatcher.ProviderUnavailableWindow - TimeSpan.FromMinutes(1);

        await Sms(provider: null).RetryAsync(notification, baseUrl: null);

        AssertSkippedMeansNothingIsComing(notification);
    }

    /// <summary>"Nothing was sent and nothing ever will be", asserted as the three things it means.</summary>
    private static void AssertSkippedMeansNothingIsComing(Notification notification)
    {
        Assert.Equal(NotificationDeliveryStatus.Skipped, notification.DeliveryStatus);

        Assert.Null(notification.NextRetryAt);
        Assert.False(notification.IsSent);

        Assert.False(notification.PageIsOnItsWay,
            "a Skipped row must never count towards an escalation step's reached count — that is how a step "
            + "announced it had paged a responder whose phone never rang");
    }

    // ─────────────────────────────────────────── 5. the "send test notification" button

    /// <summary>
    /// The operator's one way to find out, BEFORE an incident, that the phone leg does not work. It has
    /// to say so — reporting success here means they discover it at 3am instead.
    /// </summary>
    [Fact]
    public async Task SendTest_OnVoice_WithNoProvider_TellsTheOperatorTheTruth()
    {
        var (success, message) = await Voice(provider: null).SendTestAsync(UserId, null, Phone);

        Assert.False(success);
        Assert.Contains("provider", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendTest_OnSms_WithNoProvider_TellsTheOperatorTheTruth()
    {
        var (success, message) = await Sms(provider: null).SendTestAsync(UserId, null, Phone);

        Assert.False(success);
        Assert.Contains("provider", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendTest_OnVoice_WithAProvider_ActuallyPlacesTheCall()
    {
        var provider = WorkingProvider();

        var (success, _) = await Voice(provider).SendTestAsync(UserId, null, Phone);

        Assert.True(success);
        await provider.Received(1).MakeCallAsync(Arg.Is<MakeCallRequest>(r => r.Destination == Phone));
    }

    [Fact]
    public async Task SendTest_WithNoPhoneNumberOnTheProfile_SaysSo_RatherThanClaimingSuccess()
    {
        var (success, message) = await Sms(WorkingProvider()).SendTestAsync(UserId, null, phoneNumber: null);

        Assert.False(success);
        Assert.Contains("phone", message, StringComparison.OrdinalIgnoreCase);
    }

    // ─────────────────────────────────────────── the guard against the two drifting apart again

    /// <summary>The shared skeleton is not virtual, and this pins the one hole the compiler leaves: shadowing with `new`.</summary>
    [Theory]
    [InlineData(typeof(VoiceCallChannelDispatcher))]
    [InlineData(typeof(SmsChannelDispatcher))]
    public void BothPhoneChannels_RunTheSharedSkeleton_AndCannotRedeclareIt(Type channel)
    {
        Assert.True(
            typeof(PhoneChannelDispatcher).IsAssignableFrom(channel),
            $"{channel.Name} must derive from PhoneChannelDispatcher — a second copy of 'does this phone ring?' "
            + "is how every bug in this file got shipped.");

        string[] skeleton =
        [
            nameof(INotificationChannelDispatcher.SendAsync),
            nameof(INotificationChannelDispatcher.RetryAsync),
            nameof(INotificationChannelDispatcher.SendTestAsync)
        ];

        var redeclared = channel
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => skeleton.Contains(m.Name))
            .Select(m => m.Name)
            .ToArray();

        Assert.True(
            redeclared.Length == 0,
            $"{channel.Name} redeclares {string.Join(", ", redeclared)}. The shared skeleton is the whole point: "
            + "supply the per-channel hooks instead, or the two channels drift apart again.");
    }

    // ─────────────────────────────────────────── harness

    private static NotificationPayload Payload() => new()
    {
        IncidentId = Guid.NewGuid(),
        Title = "Checkout failing",
        Severity = "Critical",
        EventType = NotificationEventType.EscalationStep,
        EscalationLevel = 1
    };

    private static Notification Page(NotificationType type, Guid? incidentId = null) => new()
    {
        Id = Guid.NewGuid(),
        UserId = UserId,
        Type = type,
        Title = "Escalation step 1",
        Message = "Checkout failing",
        IncidentId = incidentId,
        DeliveryStatus = NotificationDeliveryStatus.Sending,
        CreatedAt = DateTime.UtcNow
    };

    private async Task<Guid> SeedIncidentAsync(IncidentStatus status, DateTime? acknowledgedAt = null)
    {
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            Title = "Checkout failing",
            Severity = IncidentSeverity.Critical,
            Status = status,
            StartedAt = DateTime.UtcNow.AddMinutes(-30),
            AcknowledgedAt = acknowledgedAt,
            CreatedAt = DateTime.UtcNow.AddMinutes(-30)
        };

        _ctx.Incidents.Add(incident);
        await _ctx.SaveChangesAsync();
        _ctx.ChangeTracker.Clear();

        return incident.Id;
    }

    /// <summary>A provider that accepts everything, so a test that gets no call proves a decision, not a stub.</summary>
    private static ICommunicationProvider WorkingProvider()
    {
        var provider = Substitute.For<ICommunicationProvider>();
        provider.MakeCallAsync(Arg.Any<MakeCallRequest>()).Returns(new CallResult { Success = true });
        provider.SendSmsAsync(Arg.Any<SendSmsRequest>()).Returns(new SmsResult { Success = true });
        return provider;
    }

    /// <summary>The registry, and — when it has no provider — why: every cold-registry test here means the warm-up window.</summary>
    private ICommunicationProviderRegistry Registry(
        ICommunicationProvider? provider,
        ProviderAbsence absence = ProviderAbsence.RegistryNotLoaded)
    {
        var registry = Substitute.For<ICommunicationProviderRegistry>();
        registry.GetProvider(Arg.Any<CommunicationCapability>()).Returns(provider);
        registry.DescribeAbsence(Arg.Any<CommunicationCapability>()).Returns(absence);
        return registry;
    }

    // The type names are spelled out rather than target-typed (`new(...)`) on purpose: MechanismCoverageTests
    // proves a channel dispatcher is exercised by finding a test that BUILDS it, and `new(...)` names nothing.
    private VoiceCallChannelDispatcher Voice(ICommunicationProvider? provider) =>
        new VoiceCallChannelDispatcher(
            _contacts,
            Registry(provider),
            new IncidentRepository(_ctx, NullLogger<IncidentRepository>.Instance),
            new CalluMetrics(new FakeMeterFactory()),
            NullLogger<VoiceCallChannelDispatcher>.Instance,
            new CallLogRepository(_ctx, NullLogger<CallLogRepository>.Instance));

    private SmsChannelDispatcher Sms(ICommunicationProvider? provider) =>
        new SmsChannelDispatcher(
            _contacts,
            Registry(provider),
            new IncidentRepository(_ctx, NullLogger<IncidentRepository>.Instance),
            new CalluMetrics(new FakeMeterFactory()),
            NullLogger<SmsChannelDispatcher>.Instance);
}
