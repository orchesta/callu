using Callu.Shared.Models.Communication;
using Callu.Infrastructure.Providers.Voximplant;
using System.Net;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Providers;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Providers;
using Callu.Infrastructure.Providers.CalluVoice;
using Callu.Infrastructure.Quartz;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Quartz;

namespace Callu.Tests;

/// <summary>
/// Every dial the retry job places for a given row carries that row's own id as the call id — including
/// every re-dial after an ambiguous answer. If the call an earlier ambiguous dial actually placed reports
/// in before the next tick, that report lands under the exact id the next tick is about to reuse, and the
/// job must find it and stand down rather than ringing the responder a second time under the same id.
/// </summary>
public class VoiceCallRetryDoubleDialGuardTests
{
    private const string Callee = "+905321234567";
    private const string BaseUrl = "http://callu-voice:8090";
    private const string CallbackUrl = "https://callu.example.com";

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

    private static CalluVoiceProvider Provider(
        Func<HttpRequestMessage, HttpResponseMessage> respond, out ScriptedHandler handler)
    {
        var tts = Substitute.For<ITtsTemplateService>();
        tts.ResolveMessagesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new TtsResolvedMessages(call.Arg<string>(), new Dictionary<string, string>
            {
                ["incident_message"] = "Alert from {service}. {title}.",
                ["dtmf_prompt"] = "Press 1 to acknowledge, 2 to escalate."
            })));

        handler = new ScriptedHandler(respond);

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

        return provider;
    }

    private sealed class Harness
    {
        private readonly string _dbName = $"voice-retry-guard-{Guid.NewGuid():N}";
        private IServiceProvider? _services;

        public ICommunicationProviderRegistry Registry { get; } = Substitute.For<ICommunicationProviderRegistry>();

        private IServiceProvider Services => _services ??= new ServiceCollection()
            .AddLogging()
            .AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(_dbName))
            .AddScoped<ICallLogRepository, CallLogRepository>()
            .AddSingleton(Registry)
            .BuildServiceProvider();

        public ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(_dbName).Options);

        public VoiceCallRetryQuartzJob Job() =>
            new(Services.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<VoiceCallRetryQuartzJob>.Instance);
    }

    private static IJobExecutionContext JobContext()
    {
        var context = Substitute.For<IJobExecutionContext>();
        context.CancellationToken.Returns(CancellationToken.None);
        return context;
    }

    private static async Task<(Harness Harness, CallLog Row, DateTime CompletedAt)> ArrangeDueRowAsync()
    {
        var harness = new Harness();

        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            Title = "checkout down",
            Status = IncidentStatus.Open,
            Severity = IncidentSeverity.High,
            StartedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        var completedAt = DateTime.UtcNow.AddMinutes(-3);
        var callLog = new CallLog
        {
            Id = Guid.NewGuid(),
            IncidentId = incident.Id,
            PhoneNumber = Callee,
            Status = CallStatus.NoAnswer,
            AttemptNumber = 1,
            InitiatedAt = completedAt.AddSeconds(-30),
            CompletedAt = completedAt,
            NextRetryAt = DateTime.UtcNow.AddSeconds(-5),
            CreatedAt = completedAt.AddSeconds(-30)
        };

        await using (var seed = harness.NewContext())
        {
            seed.Incidents.Add(incident);
            seed.CallLogs.Add(callLog);
            await seed.SaveChangesAsync();
        }

        return (harness, callLog, completedAt);
    }

    /// <summary>
    /// The ambiguous dial's own call reported in before the next tick — under the exact id the next
    /// tick is about to reuse. The job must find it and stand down rather than dial a second time.
    /// </summary>
    [Fact]
    public async Task ACallAlreadyOnRecordUnderTheIdThisRowWouldReuse_StandsDownInsteadOfDialing()
    {
        var (harness, callLog, completedAt) = await ArrangeDueRowAsync();
        var provider = Provider(_ => new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = new StringContent("{\"call_id\":\"new\"}")
        }, out var handler);
        harness.Registry.GetProvider(CommunicationCapability.VoiceCalls).Returns(provider);

        // The report the ambiguous dial's own (still-live) call left behind, under the id this row's
        // next dial would carry — dated before this row's own last recorded completion, so a check
        // keyed on recency rather than on the id itself would walk straight past it.
        await using (var seed = harness.NewContext())
        {
            seed.CallLogs.Add(new CallLog
            {
                Id = Guid.NewGuid(),
                IncidentId = callLog.IncidentId,
                PhoneNumber = Callee,
                Status = CallStatus.Connected,
                AttemptNumber = 2,
                CallToken = CalluVoiceProvider.CallIdFor(callLog.Id),
                AttemptId = callLog.Id,
                InitiatedAt = completedAt.AddSeconds(-5),
                CreatedAt = completedAt.AddSeconds(-5)
            });
            await seed.SaveChangesAsync();
        }

        await harness.Job().Execute(JobContext());

        Assert.Equal(0, handler.Requests);

        await using var verify = harness.NewContext();
        var stored = await verify.CallLogs.AsNoTracking().SingleAsync(c => c.Id == callLog.Id);
        Assert.Null(stored.NextRetryAt);
    }

    /// <summary>The other half: nothing is on record under that id, so a responder nobody has reached is worth dialling again.</summary>
    [Fact]
    public async Task NoCallOnRecordUnderThatId_StillDials()
    {
        var (harness, _, _) = await ArrangeDueRowAsync();
        var provider = Provider(_ => new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = new StringContent("{\"call_id\":\"new\"}")
        }, out var handler);
        harness.Registry.GetProvider(CommunicationCapability.VoiceCalls).Returns(provider);

        await harness.Job().Execute(JobContext());

        Assert.Equal(1, handler.Requests);
    }

    /// <summary>
    /// The whole hazard in one sequence: a 504 that in fact placed the call, followed by that call's own
    /// report landing before the next tick. Without the guard this tick dials the responder a second time
    /// under the identical call id.
    /// </summary>
    [Fact]
    public async Task A504ThatPlacedTheCall_ThenThatCallsOwnReportLanding_StopsTheNextTickFromRedialing()
    {
        var (harness, callLog, completedAt) = await ArrangeDueRowAsync();

        var dials = 0;
        var provider = Provider(_ =>
        {
            Interlocked.Increment(ref dials);
            return new HttpResponseMessage(HttpStatusCode.GatewayTimeout) { Content = new StringContent("{}") };
        }, out var handler);
        harness.Registry.GetProvider(CommunicationCapability.VoiceCalls).Returns(provider);

        // Tick 1: the retry dial reads as ambiguous (504) and is parked, not dialled again yet.
        await harness.Job().Execute(JobContext());
        Assert.Equal(1, handler.Requests);

        await using (var check = harness.NewContext())
        {
            var afterTick1 = await check.CallLogs.AsNoTracking().SingleAsync(c => c.Id == callLog.Id);
            Assert.Equal(VoiceDialOutFailureKind.ProviderThrew, afterTick1.DialOutFailureKind);
            Assert.NotNull(afterTick1.NextRetryAt);
        }

        // The call the 504 hid reports in — under the id tick 1 just sent — and the deadline arrives.
        await using (var seed = harness.NewContext())
        {
            seed.CallLogs.Add(new CallLog
            {
                Id = Guid.NewGuid(),
                IncidentId = callLog.IncidentId,
                PhoneNumber = Callee,
                Status = CallStatus.Connected,
                AttemptNumber = 2,
                CallToken = CalluVoiceProvider.CallIdFor(callLog.Id),
                AttemptId = callLog.Id,
                InitiatedAt = completedAt.AddSeconds(-5),
                CreatedAt = completedAt.AddSeconds(-5)
            });

            var due = await seed.CallLogs.SingleAsync(c => c.Id == callLog.Id);
            due.NextRetryAt = DateTime.UtcNow.AddSeconds(-1);
            await seed.SaveChangesAsync();
        }

        // Tick 2: must find the call already on record and stand down — not ring the responder again.
        await harness.Job().Execute(JobContext());

        Assert.Equal(1, handler.Requests);
    }
}
