using Callu.Shared.Models.Communication;
using Callu.Infrastructure.Providers.Voximplant;
using System.Net;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Common.Models.Persistence;
using Callu.Application.Providers;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Providers;
using Callu.Infrastructure.Providers.CalluVoice;
using Callu.Infrastructure.Services;
using Callu.Infrastructure.Telemetry;
using Callu.Shared.Models.Notifications;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Callu.Tests;

/// <summary>
/// The FIRST dial of every page goes through the notification dispatcher, not the call-log retry job,
/// and the two used to price an ambiguous answer differently: the job parks for two minutes, this path
/// re-dialled in thirty seconds. A 504 from a voice gateway does not mean the call was not placed, so
/// thirty seconds later the responder's phone rings a second time while the first call is still up.
/// </summary>
public class VoiceDialThroughThePhoneDispatcherTests : IDisposable
{
    private const string UserId = "responder-1";
    private const string Phone = "+905321234567";
    private const string BaseUrl = "http://callu-voice:8090";
    private const string CallbackUrl = "https://callu.example.com";

    private readonly ApplicationDbContext _ctx =
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"voice-dial-{Guid.NewGuid():N}").Options);

    private readonly IUserContactRepository _contacts = Substitute.For<IUserContactRepository>();

    public VoiceDialThroughThePhoneDispatcherTests() =>
        _contacts.GetContactByIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new UserContactSnapshot(UserId, "Responder", Phone, "responder@example.io"));

    public void Dispose() => _ctx.Dispose();

    private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
                Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));

            return respond(request);
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static (CalluVoiceProvider Provider, ScriptedHandler Handler) VoiceService(
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new ScriptedHandler(respond);

        var tts = Substitute.For<ITtsTemplateService>();
        tts.ResolveMessagesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new TtsResolvedMessages(call.Arg<string>(), new Dictionary<string, string>
            {
                ["incident_message"] = "Alert from {service}. {title}.",
                ["dtmf_prompt"] = "Press 1 to acknowledge, 2 to escalate, star to repeat, or 9 to ask to be brought in."
            })));

        var provider = new CalluVoiceProvider(
            new StubHttpClientFactory(handler),
            tts,
            new ProviderSecretProtector(
                new EphemeralDataProtectionProvider(), NullLogger<ProviderSecretProtector>.Instance),
            new SipTrunkPasswordProtector(
                new EphemeralDataProtectionProvider(), NullLogger<SipTrunkPasswordProtector>.Instance),
            new EphemeralDataProtectionProvider(),
            NullLogger<CalluVoiceProvider>.Instance);

        provider.InitializeAsync(
            $"{{\"baseUrl\":\"{BaseUrl}\",\"apiToken\":\"t\",\"callbackUrl\":\"{CallbackUrl}\"}}",
            sipTrunk: null).GetAwaiter().GetResult();

        return (provider, handler);
    }

    private VoiceCallChannelDispatcher Voice(ICommunicationProvider provider)
    {
        var registry = Substitute.For<ICommunicationProviderRegistry>();
        registry.GetProvider(Arg.Any<CommunicationCapability>()).Returns(provider);
        registry.DescribeAbsence(Arg.Any<CommunicationCapability>()).Returns(ProviderAbsence.RegistryNotLoaded);

        return new VoiceCallChannelDispatcher(
            _contacts,
            registry,
            new IncidentRepository(_ctx, NullLogger<IncidentRepository>.Instance),
            new CalluMetrics(new FakeMeterFactory()),
            NullLogger<VoiceCallChannelDispatcher>.Instance,
            new CallLogRepository(_ctx, NullLogger<CallLogRepository>.Instance));
    }

    private static Notification Page(Guid? incidentId = null) => new()
    {
        Id = Guid.NewGuid(),
        UserId = UserId,
        Type = NotificationType.VoiceCall,
        Title = "Escalation step 1",
        Message = "Checkout failing",
        IncidentId = incidentId,
        DeliveryStatus = NotificationDeliveryStatus.Sending,
        CreatedAt = DateTime.UtcNow
    };

    private static NotificationPayload Payload() => new()
    {
        IncidentId = Guid.NewGuid(),
        Title = "Checkout failing",
        Severity = "Critical",
        EventType = NotificationEventType.EscalationStep,
        EscalationLevel = 1
    };

    private static HttpResponseMessage Answer(int status, string body = "{}") =>
        new((HttpStatusCode)status) { Content = new StringContent(body) };

    private async Task<Guid> SeedOpenIncidentAsync()
    {
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            Title = "Checkout failing",
            Severity = IncidentSeverity.Critical,
            Status = IncidentStatus.Open,
            StartedAt = DateTime.UtcNow.AddMinutes(-5),
            CreatedAt = DateTime.UtcNow.AddMinutes(-5)
        };

        _ctx.Incidents.Add(incident);
        await _ctx.SaveChangesAsync();
        _ctx.ChangeTracker.Clear();

        return incident.Id;
    }

    private static TimeSpan WaitOn(Notification notification) =>
        (notification.NextRetryAt ?? notification.LastAttemptAt!.Value) - notification.LastAttemptAt!.Value;

    // ------------------------------------------------------------------ the two answers, priced

    /// <summary>
    /// A gateway timeout says nothing about the far end: the render may have finished and the phone may
    /// already be ringing. The adapter throws for exactly that reason, and the wait has to reflect it.
    /// </summary>
    [Theory]
    [InlineData(504)]
    [InlineData(500)]
    public async Task AnAmbiguousAnswerOnTheFirstDial_ParksAsLongAsTheRetryJobWould(int status)
    {
        var (provider, _) = VoiceService(_ => Answer(status));
        var notification = Page();

        await Voice(provider).SendAsync(notification, null, Phone, Payload(), null);

        Assert.Equal(NotificationDeliveryStatus.Failed, notification.DeliveryStatus);
        Assert.True(WaitOn(notification) >= Notification.UndeterminedRetryFloor,
            $"an ambiguous dial waited only {WaitOn(notification).TotalSeconds:0}s before dialling again");
    }

    /// <summary>A request that never left is the same fact and gets the same wait.</summary>
    [Fact]
    public async Task ATimedOutFirstDial_ParksTheSameWay()
    {
        var (provider, _) = VoiceService(_ => throw new TaskCanceledException("HttpClient.Timeout"));
        var notification = Page();

        await Voice(provider).SendAsync(notification, null, Phone, Payload(), null);

        Assert.True(WaitOn(notification) >= Notification.UndeterminedRetryFloor);
    }

    /// <summary>
    /// The other half of the bargain: a service that plainly said it dialled nothing is retried soon.
    /// Pricing a refusal like an ambiguity delays every page behind a restarting container.
    /// </summary>
    [Theory]
    [InlineData(503)]
    [InlineData(502)]
    public async Task ARefusedFirstDialIsRetriedSoon(int status)
    {
        var (provider, _) = VoiceService(_ => Answer(status, "{\"error\":\"at capacity\"}"));
        var notification = Page();

        await Voice(provider).SendAsync(notification, null, Phone, Payload(), null);

        Assert.Equal(NotificationDeliveryStatus.Failed, notification.DeliveryStatus);
        Assert.True(WaitOn(notification) < Notification.UndeterminedRetryFloor,
            "a refusal must not be charged the ambiguous wait");
    }

    /// <summary>The same rule on the retry path, which shares the skeleton but had its own catch.</summary>
    [Fact]
    public async Task AnAmbiguousAnswerOnARetry_ParksJustAsLong()
    {
        var incidentId = await SeedOpenIncidentAsync();
        var (provider, _) = VoiceService(_ => Answer(504));
        var notification = Page(incidentId);

        await Voice(provider).RetryAsync(notification, baseUrl: null);

        Assert.True(WaitOn(notification) >= Notification.UndeterminedRetryFloor);
    }

    [Fact]
    public async Task ARefusedRetryIsStillRetriedSoon()
    {
        var incidentId = await SeedOpenIncidentAsync();
        var (provider, _) = VoiceService(_ => Answer(503, "{\"error\":\"shutting down\"}"));
        var notification = Page(incidentId);

        await Voice(provider).RetryAsync(notification, baseUrl: null);

        Assert.True(WaitOn(notification) < Notification.UndeterminedRetryFloor);
    }

    // ------------------------------------------------------------------ what the repeat carries

    /// <summary>
    /// A repeat of the same page carries the same call id, so the voice service refuses it as a
    /// duplicate while the first call is still up rather than dialling the responder a second time.
    /// </summary>
    [Fact]
    public async Task TheFirstDialAndItsRetryCarryTheSameCallId()
    {
        var incidentId = await SeedOpenIncidentAsync();
        var (provider, handler) = VoiceService(_ => Answer(504));
        var notification = Page(incidentId);

        await Voice(provider).SendAsync(notification, null, Phone, Payload(), null);
        await Voice(provider).RetryAsync(notification, baseUrl: null);

        Assert.Equal(2, handler.Bodies.Count);
        Assert.Equal(CallIdIn(handler.Bodies[0]), CallIdIn(handler.Bodies[1]));
    }

    /// <summary>A different page to the same person is a different attempt, and must ring on its own.</summary>
    [Fact]
    public async Task TwoDifferentPagesCarryDifferentCallIds()
    {
        var (provider, handler) = VoiceService(_ => Answer(202));

        await Voice(provider).SendAsync(Page(), null, Phone, Payload(), null);
        await Voice(provider).SendAsync(Page(), null, Phone, Payload(), null);

        Assert.Equal(2, handler.Bodies.Count);
        Assert.NotEqual(CallIdIn(handler.Bodies[0]), CallIdIn(handler.Bodies[1]));
    }

    // ------------------------------------------------------------------ what resolves the ambiguity

    /// <summary>
    /// A dial that could not be read may well have placed the call, and the call it leaves behind is the
    /// only proof either way. Once that proof is on record the page has arrived, and the call's own retry
    /// chain owns what happens next — so this row stops, rather than dialling the responder again under an
    /// id that now names a call that has already finished.
    /// </summary>
    [Fact]
    public async Task OnceTheAmbiguousDialsCallIsOnRecord_TheRetryStandsDownInsteadOfDialling()
    {
        var incidentId = await SeedOpenIncidentAsync();
        var (provider, handler) = VoiceService(_ => Answer(504));
        var notification = Page(incidentId);

        await Voice(provider).SendAsync(notification, null, Phone, Payload(), null);
        await RecordTheCallItPlacedAsync(notification, incidentId, CallStatus.NoAnswer);

        await Voice(provider).RetryAsync(notification, baseUrl: null);

        Assert.Single(handler.Bodies);
        Assert.Equal(NotificationDeliveryStatus.Delivered, notification.DeliveryStatus);
    }

    /// <summary>A call still running is just as much proof that this page went out.</summary>
    [Fact]
    public async Task ACallStillInProgress_AlsoStopsTheRetry()
    {
        var incidentId = await SeedOpenIncidentAsync();
        var (provider, handler) = VoiceService(_ => Answer(504));
        var notification = Page(incidentId);

        await Voice(provider).SendAsync(notification, null, Phone, Payload(), null);
        await RecordTheCallItPlacedAsync(notification, incidentId, CallStatus.Connected, ended: false);

        await Voice(provider).RetryAsync(notification, baseUrl: null);

        Assert.Single(handler.Bodies);
    }

    /// <summary>
    /// The other half: with nothing on record the dial left no call, and a page nobody has had is worth
    /// dialling again. Standing down on an unresolved ambiguity is how a responder is never called at all.
    /// </summary>
    [Fact]
    public async Task WithNoCallOnRecord_TheRetryStillDials()
    {
        var incidentId = await SeedOpenIncidentAsync();
        var (provider, handler) = VoiceService(_ => Answer(504));
        var notification = Page(incidentId);

        await Voice(provider).SendAsync(notification, null, Phone, Payload(), null);
        await Voice(provider).RetryAsync(notification, baseUrl: null);

        Assert.Equal(2, handler.Bodies.Count);
    }

    /// <summary>A call another page placed is not this page's, and must not stand this one down.</summary>
    [Fact]
    public async Task ACallBelongingToAnotherPage_DoesNotStopThisOne()
    {
        var incidentId = await SeedOpenIncidentAsync();
        var (provider, handler) = VoiceService(_ => Answer(504));
        var notification = Page(incidentId);

        await Voice(provider).SendAsync(notification, null, Phone, Payload(), null);
        await RecordTheCallItPlacedAsync(Page(incidentId), incidentId, CallStatus.NoAnswer);

        await Voice(provider).RetryAsync(notification, baseUrl: null);

        Assert.Equal(2, handler.Bodies.Count);
    }

    /// <summary>The row a status callback leaves for the call this page's dial would have placed.</summary>
    private async Task RecordTheCallItPlacedAsync(
        Notification notification, Guid incidentId, CallStatus status, bool ended = true)
    {
        _ctx.CallLogs.Add(new CallLog
        {
            Id = Guid.NewGuid(),
            IncidentId = incidentId,
            PhoneNumber = Phone,
            Status = status,
            AttemptNumber = 1,
            CallToken = CalluVoiceProvider.CallIdFor(notification.Id),
            AttemptId = notification.Id,
            InitiatedAt = DateTime.UtcNow.AddSeconds(-30),
            CompletedAt = ended ? DateTime.UtcNow : null,
            CreatedAt = DateTime.UtcNow.AddSeconds(-30)
        });

        await _ctx.SaveChangesAsync();
        _ctx.ChangeTracker.Clear();
    }

    /// <summary>A duplicate is a page already on its way; calling it a failure re-dials a ringing phone.</summary>
    [Fact]
    public async Task ADuplicateRefusalCountsAsDelivered()
    {
        var (provider, _) = VoiceService(_ => Answer(409, "{\"error\":\"call already in progress\"}"));
        var notification = Page();

        await Voice(provider).SendAsync(notification, null, Phone, Payload(), null);

        Assert.Equal(NotificationDeliveryStatus.Delivered, notification.DeliveryStatus);
    }

    private static string CallIdIn(string body) =>
        System.Text.Json.JsonDocument.Parse(body).RootElement.GetProperty("call_id").GetString()!;
}
