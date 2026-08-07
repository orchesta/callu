using Callu.Infrastructure.Providers.Voximplant;
using System.Net;
using System.Text.Json;
using System.Web;
using Callu.Api.Controllers;
using Callu.Application.Common.Interfaces;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Common.Models.Persistence;
using Callu.Application.Providers;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.DI;
using Callu.Infrastructure.Identity;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Providers;
using Callu.Infrastructure.Providers.CalluVoice;
using Callu.Infrastructure.Services;
using Callu.Infrastructure.Telemetry;
using Callu.Shared.Models.Communication;
using Callu.Shared.Models.Conference;
using Callu.Shared.Models.Notifications;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Callu.Tests;

/// <summary>
/// One page, from the dial to the keypress, when POST /calls answers something that says nothing about
/// whether a call went out. A reverse proxy returning 504 while the service finishes rendering and places
/// the call is the ordinary shape of that, and the phone rings anyway.
///
/// The page must not dial a second time on the strength of a timer, because the second call would carry
/// the id the first one already owns: while the service still holds the first call it is refused as a
/// duplicate, and once the service has forgotten it a real second call goes out under an id that names a
/// call already recorded as finished — every callback it produces is then discarded as out of order, so
/// the responder presses 1, hears the incident acknowledged, and it stays Open.
/// </summary>
[Collection(PostgresCollection.Name)]
public class VoiceDialThatCouldNotBeReadTests(PostgresFixture pg)
{
    private const string UserId = "responder-1";
    private const string Phone = "+905321234567";
    private const string BaseUrl = "http://callu-voice:8090";
    private const string CallbackUrl = "https://callu.example.com";

    /// <summary>
    /// The whole sequence: the dial cannot be read, the call it placed reports in, the retry finds that
    /// record and stands down instead of ringing the responder again, and the 1 they press on the call
    /// that is still up acknowledges the incident.
    /// </summary>
    [PostgresFact]
    public async Task A504ThatStillPlacedTheCall_EndsWithTheKeypressAcknowledgingTheIncident()
    {
        var world = await ArrangeAsync();
        var page = Page(world.IncidentId);

        await world.Voice.SendAsync(page, null, Phone, Payload(world.IncidentId), null);

        // Nothing readable came back, so the page is parked rather than written off or called delivered.
        Assert.Equal(NotificationDeliveryStatus.Failed, page.DeliveryStatus);
        Assert.True(page.NextRetryAt - page.LastAttemptAt >= Notification.UndeterminedRetryFloor);

        // The call the 504 hid: it was placed, it is ringing, and it reports in on the URL it was given.
        var token = TokenSentWithTheDial(world);
        Assert.Equal(CalluVoiceCallbackApplication.Applied, await ReportAsync(world, token, "alerting"));

        // The retry deadline comes round. There is a call on record for this page, so no second dial.
        await world.Voice.RetryAsync(page, baseUrl: null);

        Assert.Single(world.Handler.Bodies);
        Assert.Equal(NotificationDeliveryStatus.Delivered, page.DeliveryStatus);

        // The responder presses 1 on the call that was up the whole time.
        Assert.Equal(CalluVoiceCallbackApplication.Applied, await ReportAsync(world, token, "acknowledged"));

        Assert.Equal(IncidentStatus.Acknowledged, await IncidentStatusAsync(world));
        Assert.Equal(CallStatus.Acknowledged, (await SingleCallAsync(world)).Status);
    }

    /// <summary>
    /// The same sequence with the answer applied through the endpoint rather than the persistence, so the
    /// URL the adapter actually built is what the keypress arrives on.
    /// </summary>
    [PostgresFact]
    public async Task TheKeypressIsAcceptedOnTheUrlTheDialSentOut()
    {
        var world = await ArrangeAsync();
        var page = Page(world.IncidentId);

        await world.Voice.SendAsync(page, null, Phone, Payload(world.IncidentId), null);

        var sent = new Uri(CallbackUrlSentWithTheDial(world));
        Assert.Equal(CalluVoiceConfig.CallbackPath, sent.AbsolutePath);

        var token = HttpUtility.ParseQueryString(sent.Query)
            [CalluVoiceCallbackTokenProtector.TokenQueryKey];

        var answered = await world.Controller.ReceiveCallback(
            token, new CalluVoiceCallbackRequest { CallId = CallIdOf(page), Status = "acknowledged" },
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(answered);
        Assert.Equal(IncidentStatus.Acknowledged, await IncidentStatusAsync(world));
    }

    /// <summary>
    /// The page stands down only against proof. With no call on record the dial left nothing behind, and
    /// a responder nobody has reached is worth dialling again — going quiet there is the worse failure.
    /// </summary>
    [PostgresFact]
    public async Task A504ThatPlacedNothing_IsDialledAgain()
    {
        var world = await ArrangeAsync();
        var page = Page(world.IncidentId);

        await world.Voice.SendAsync(page, null, Phone, Payload(world.IncidentId), null);
        await world.Voice.RetryAsync(page, baseUrl: null);

        Assert.Equal(2, world.Handler.Bodies.Count);
    }

    // ------------------------------------------------------------------ the world

    private static string CallIdOf(Notification page) => CalluVoiceProvider.CallIdFor(page.Id);

    private static string CallbackUrlSentWithTheDial(World world) =>
        JsonDocument.Parse(world.Handler.Bodies[0]).RootElement
            .GetProperty("callback_url").GetString()!;

    private static string TokenSentWithTheDial(World world) =>
        HttpUtility.ParseQueryString(new Uri(CallbackUrlSentWithTheDial(world)).Query)
            [CalluVoiceCallbackTokenProtector.TokenQueryKey]!;

    private static async Task<CalluVoiceCallbackApplication> ReportAsync(
        World world, string token, string status)
    {
        Assert.True(world.Tokens.TryResolve(token, out var ticket));

        return await world.Persistence.ProcessAsync(
            ticket, new CalluVoiceCallbackRequest { CallId = ticket.CallId, Status = status });
    }

    private static async Task<IncidentStatus> IncidentStatusAsync(World world)
    {
        await using var db = PostgresFixture.Context(world.ConnectionString);
        return (await db.Incidents.AsNoTracking().FirstAsync(i => i.Id == world.IncidentId)).Status;
    }

    private static async Task<CallLog> SingleCallAsync(World world)
    {
        await using var db = PostgresFixture.Context(world.ConnectionString);
        return Assert.Single(await db.CallLogs.AsNoTracking().ToListAsync());
    }

    private static Notification Page(Guid incidentId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = UserId,
        Type = NotificationType.VoiceCall,
        Title = "Escalation step 1",
        IncidentId = incidentId,
        DeliveryStatus = NotificationDeliveryStatus.Sending,
        CreatedAt = DateTime.UtcNow
    };

    private static NotificationPayload Payload(Guid incidentId) => new()
    {
        IncidentId = incidentId,
        Title = "Checkout failing",
        Severity = "Critical",
        EventType = NotificationEventType.EscalationStep,
        EscalationLevel = 1
    };

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

    private sealed record World(
        string ConnectionString,
        Guid IncidentId,
        ScriptedHandler Handler,
        VoiceCallChannelDispatcher Voice,
        CalluVoiceCallbackTokenProtector Tokens,
        CalluVoiceCallbackController Controller,
        ICalluVoiceCallbackPersistence Persistence);

    private async Task<World> ArrangeAsync()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        Guid incidentId;
        await using (var db = PostgresFixture.Context(cs))
        {
            db.Users.Add(new ApplicationUser
            {
                Id = UserId,
                UserName = "ada",
                Email = "ada@example.com",
                PhoneNumber = Phone,
                FirstName = "Ada",
                LastName = "Çelik",
            });

            var incident = new Incident
            {
                Title = "Checkout failing",
                Severity = IncidentSeverity.Critical,
                Status = IncidentStatus.Open,
                StartedAt = DateTime.UtcNow,
            };
            db.Add(incident);
            await db.SaveChangesAsync();
            incidentId = incident.Id;
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<ICurrentUserService>());
        services.AddPersistenceModule(cs, enableParameterLogging: false);
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddScoped<IAuditLogService, AuditLogService>();

        var escalation = Substitute.For<IEscalationOrchestrator>();
        escalation.EscalateNowAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        services.AddSingleton(escalation);

        var conferences = Substitute.For<IVideoConferenceService>();
        conferences.CreateRoomAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new ConferenceRoomResult { Success = true, RoomId = Guid.NewGuid() });
        services.AddSingleton(conferences);

        var scope = services.BuildServiceProvider().CreateScope().ServiceProvider;

        var keyring = new EphemeralDataProtectionProvider();
        var (provider, handler) = VoiceService(keyring);

        var registry = Substitute.For<ICommunicationProviderRegistry>();
        registry.GetProvider(Arg.Any<CommunicationCapability>()).Returns(provider);
        registry.DescribeAbsence(Arg.Any<CommunicationCapability>()).Returns(ProviderAbsence.RegistryNotLoaded);

        var contacts = Substitute.For<IUserContactRepository>();
        contacts.GetContactByIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new UserContactSnapshot(UserId, "Ada", Phone, "ada@example.com"));

        var voice = new VoiceCallChannelDispatcher(
            contacts,
            registry,
            scope.GetRequiredService<IIncidentRepository>(),
            new CalluMetrics(new FakeMeterFactory()),
            NullLogger<VoiceCallChannelDispatcher>.Instance,
            scope.GetRequiredService<ICallLogRepository>());

        var persistence = scope.GetRequiredService<ICalluVoiceCallbackPersistence>();

        return new World(
            cs, incidentId, handler, voice,
            new CalluVoiceCallbackTokenProtector(keyring),
            new CalluVoiceCallbackController(
                persistence, keyring, NullLogger<CalluVoiceCallbackController>.Instance),
            persistence);
    }

    /// <summary>A service that answers 504 to the first dial and 202 to anything after it, so a second call shows up as one.</summary>
    private static (CalluVoiceProvider Provider, ScriptedHandler Handler) VoiceService(
        IDataProtectionProvider keyring)
    {
        var dials = 0;
        var handler = new ScriptedHandler(_ =>
            new HttpResponseMessage(++dials == 1 ? HttpStatusCode.GatewayTimeout : HttpStatusCode.Accepted)
            {
                Content = new StringContent("{}")
            });

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
            keyring,
            NullLogger<CalluVoiceProvider>.Instance);

        provider.InitializeAsync(
            $"{{\"baseUrl\":\"{BaseUrl}\",\"apiToken\":\"t\",\"callbackUrl\":\"{CallbackUrl}\"}}",
            sipTrunk: null).GetAwaiter().GetResult();

        return (provider, handler);
    }
}
