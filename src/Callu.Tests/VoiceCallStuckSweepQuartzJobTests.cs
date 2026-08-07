using Callu.Infrastructure.Providers.Voximplant;
using System.Net;
using Callu.Application.Common.Interfaces;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Providers;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.CalluVoice;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Providers;
using Callu.Infrastructure.Providers.CalluVoice;
using Callu.Infrastructure.Quartz;
using Callu.Infrastructure.Services;
using Callu.Shared.Models.Communication;
using Callu.Shared.Models.Conference;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Quartz;

namespace Callu.Tests;

/// <summary>
/// "alerting" and "connected" leave a CallLog with no CompletedAt and no NextRetryAt — a shape the
/// ordinary retry sweep never looks at. If the call's own terminal callback never lands (an API
/// restart is the ordinary way that happens), nothing was watching that row until this sweep existed.
/// </summary>
public class VoiceCallStuckSweepQuartzJobTests
{
    private const string UserId = "responder-1";
    private const string Phone = "+905321234567";

    private static IJobExecutionContext JobContext()
    {
        var context = Substitute.For<IJobExecutionContext>();
        context.CancellationToken.Returns(CancellationToken.None);
        return context;
    }

    private sealed record World(
        string DbName,
        Guid IncidentId,
        ICalluVoiceCallbackPersistence Persistence,
        IAuditLogService Audit);

    private static ApplicationDbContext Context(string dbName) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(dbName).Options);

    private static async Task<World> ArrangeAsync()
    {
        var dbName = $"voice-stuck-sweep-{Guid.NewGuid():N}";

        Guid incidentId;
        await using (var db = Context(dbName))
        {
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
        services.AddDbContextFactory<ApplicationDbContext>(o => o.UseInMemoryDatabase(dbName));

        var escalation = Substitute.For<IEscalationOrchestrator>();
        escalation.EscalateNowAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        services.AddSingleton(escalation);

        var conferences = Substitute.For<IVideoConferenceService>();
        conferences.CreateRoomAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new ConferenceRoomResult { Success = true, RoomId = Guid.NewGuid() });
        services.AddSingleton(conferences);

        var audit = Substitute.For<IAuditLogService>();
        services.AddSingleton(audit);

        services.AddSingleton<ICalluVoiceCallbackPersistence, CalluVoiceCallbackPersistence>();

        var provider = services.BuildServiceProvider();
        var persistence = provider.GetRequiredService<ICalluVoiceCallbackPersistence>();

        return new World(dbName, incidentId, persistence, audit);
    }

    private sealed class Harness(string dbName, IAuditLogService audit)
    {
        private IServiceProvider? _services;

        public ICommunicationProviderRegistry Registry { get; } = Substitute.For<ICommunicationProviderRegistry>();

        private IServiceProvider Services => _services ??= new ServiceCollection()
            .AddLogging()
            .AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(dbName))
            .AddScoped<ICallLogRepository, CallLogRepository>()
            .AddSingleton(Registry)
            .AddSingleton(audit)
            .BuildServiceProvider();

        public VoiceCallStuckSweepQuartzJob StuckSweep() =>
            new(Services.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<VoiceCallStuckSweepQuartzJob>.Instance);

        public VoiceCallRetryQuartzJob RetrySweep() =>
            new(Services.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<VoiceCallRetryQuartzJob>.Instance);
    }

    private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Requests);
            return Task.FromResult(respond(request));
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static CalluVoiceProvider VoiceProvider(out ScriptedHandler handler)
    {
        var tts = Substitute.For<ITtsTemplateService>();
        tts.ResolveMessagesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new TtsResolvedMessages(call.Arg<string>(), new Dictionary<string, string>
            {
                ["incident_message"] = "Alert from {service}. {title}.",
                ["dtmf_prompt"] = "Press 1 to acknowledge, 2 to escalate."
            })));

        handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = new StringContent("{\"call_id\":\"new\"}")
        });

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
            "{\"baseUrl\":\"http://callu-voice:8090\",\"apiToken\":\"t\",\"callbackUrl\":\"https://callu.example.com\"}",
            sipTrunk: null).GetAwaiter().GetResult();

        return provider;
    }

    /// <summary>
    /// dial → alerting recorded → the terminal callback never arrives (an API restart, simulated by
    /// letting the grace period elapse) → the sweep runs → the responder is not recorded as reached,
    /// and the retry sweep still places a further attempt.
    /// </summary>
    [Fact]
    public async Task ACallStuckAtAlertingPastItsGracePeriod_IsMarkedFailed_NotDelivered_AndRetried()
    {
        var world = await ArrangeAsync();
        var callId = Guid.NewGuid().ToString("N");
        var ticket = new CalluVoiceCallbackTicket(world.IncidentId, callId, Phone);

        // The call is placed and reports "alerting" — the only callback it will ever manage before
        // the API goes away for the rest of its lifetime.
        var applied = await world.Persistence.ProcessAsync(
            ticket, new CalluVoiceCallbackRequest { CallId = callId, Status = "alerting" });
        Assert.Equal(CalluVoiceCallbackApplication.Applied, applied);

        Guid rowId;
        await using (var db = Context(world.DbName))
        {
            var row = await db.CallLogs.SingleAsync(c => c.CallToken == callId);
            Assert.Equal(CallStatus.Initiated, row.Status);
            Assert.Null(row.CompletedAt);
            Assert.Null(row.NextRetryAt);

            // The grace period has to actually elapse for the sweep to act — simulated here rather
            // than waiting for it in real time.
            row.InitiatedAt = DateTime.UtcNow.AddMinutes(-6);
            rowId = row.Id;
            await db.SaveChangesAsync();
        }

        var harness = new Harness(world.DbName, world.Audit);
        await harness.StuckSweep().Execute(JobContext());

        await using (var db = Context(world.DbName))
        {
            var row = await db.CallLogs.AsNoTracking().SingleAsync(c => c.Id == rowId);

            // Not recorded as reached: the row is failed, never Acknowledged/Connected.
            Assert.Equal(CallStatus.Failed, row.Status);
            Assert.NotEqual(CallStatus.Acknowledged, row.Status);
            Assert.NotNull(row.CompletedAt);
            Assert.NotNull(row.NextRetryAt);

            var timeline = await db.Set<IncidentTimelineEvent>()
                .Where(e => e.IncidentId == world.IncidentId)
                .ToListAsync();
            Assert.Contains(timeline, e => e.EventType == TimelineEventType.CallFailed);
        }

        await world.Audit.Received(1).LogAsync(
            Arg.Any<string?>(), AuditAction.VoiceCallLost, "Incident", world.IncidentId.ToString(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());

        // The incident still gets a further attempt: the ordinary retry sweep dials again.
        var provider = VoiceProvider(out var handler);
        harness.Registry.GetProvider(CommunicationCapability.VoiceCalls).Returns(provider);

        await harness.RetrySweep().Execute(JobContext());

        Assert.Equal(1, handler.Requests);
    }

    /// <summary>A call still within its grace period must not be swept — it may simply still be ringing.</summary>
    [Fact]
    public async Task ACallStillWithinItsGracePeriod_IsLeftAlone()
    {
        var world = await ArrangeAsync();
        var callId = Guid.NewGuid().ToString("N");
        var ticket = new CalluVoiceCallbackTicket(world.IncidentId, callId, Phone);

        await world.Persistence.ProcessAsync(
            ticket, new CalluVoiceCallbackRequest { CallId = callId, Status = "alerting" });

        var harness = new Harness(world.DbName, world.Audit);
        await harness.StuckSweep().Execute(JobContext());

        await using var db = Context(world.DbName);
        var row = await db.CallLogs.AsNoTracking().SingleAsync(c => c.CallToken == callId);

        Assert.Equal(CallStatus.Initiated, row.Status);
        Assert.Null(row.CompletedAt);
    }
}
