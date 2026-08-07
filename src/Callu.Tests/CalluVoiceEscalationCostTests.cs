using Callu.Infrastructure.Providers.Voximplant;
using System.Net;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Providers;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Configuration;
using Callu.Infrastructure.DI;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Infrastructure.Providers;
using Callu.Infrastructure.Providers.CalluVoice;
using Callu.Infrastructure.Quartz;
using Callu.Infrastructure.Services;
using Callu.Shared.Models.Communication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Quartz;

namespace Callu.Tests;

/// <summary>
/// What each callu-voice answer costs a waiting responder. The retry job charges an ambiguous dial
/// four times the wait of a refused one, because a re-dial it cannot rule out would ring a phone
/// twice — so an adapter that reports "refused" as "unknown" delays every page behind a restart.
/// </summary>
public class CalluVoiceEscalationCostTests
{
    private const string Callee = "+905321234567";
    private const string BaseUrl = "http://callu-voice:8090";
    private const string CallbackUrl = "https://callu.example.com";

    private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static CalluVoiceProvider Provider(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var tts = Substitute.For<ITtsTemplateService>();
        tts.ResolveMessagesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new TtsResolvedMessages(call.Arg<string>(), new Dictionary<string, string>
            {
                ["incident_message"] = "Alert from {service}. {title}.",
                ["dtmf_prompt"] = "Press 1 to acknowledge, 2 to escalate."
            })));

        var provider = new CalluVoiceProvider(
            new StubHttpClientFactory(new ScriptedHandler(respond)),
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
        private readonly string _dbName = $"callu-voice-retry-{Guid.NewGuid():N}";
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

    /// <summary>Runs one sweep of the voice retry job with the real adapter behind it.</summary>
    private static async Task<(CallLog Row, TimeSpan Wait)> SweepAsync(
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        // Built before Returns: configuring one substitute inside another's Returns loses the setup.
        var provider = Provider(respond);
        var harness = new Harness();
        harness.Registry.GetProvider(CommunicationCapability.VoiceCalls).Returns(provider);

        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            Title = "db down",
            Status = IncidentStatus.Open,
            Severity = IncidentSeverity.High,
            StartedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        var completedAt = DateTime.UtcNow.AddMinutes(-1);
        var callLog = new CallLog
        {
            Id = Guid.NewGuid(),
            IncidentId = incident.Id,
            PhoneNumber = Callee,
            Status = CallStatus.NoAnswer,
            FailureReason = "no answer",
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

        var before = DateTime.UtcNow;
        await harness.Job().Execute(JobContext());

        await using var verify = harness.NewContext();
        var stored = await verify.CallLogs.AsNoTracking().SingleAsync(c => c.Id == callLog.Id);

        return (stored, (stored.NextRetryAt ?? before) - before);
    }

    private static HttpResponseMessage Answer(int status, string body = "{}") =>
        new((HttpStatusCode)status) { Content = new StringContent(body) };

    /// <summary>
    /// The whole point of reading the status code: a service that is full, draining or restarting
    /// says so, and the next attempt follows in seconds rather than minutes.
    /// </summary>
    [Theory]
    [InlineData(503)]
    [InlineData(502)]
    [InlineData(400)]
    public async Task ARefusedDialIsRetriedSoon(int status)
    {
        var (row, wait) = await SweepAsync(_ => Answer(status, "{\"error\":\"at capacity\"}"));

        Assert.Equal(VoiceDialOutFailureKind.ProviderRefused, row.DialOutFailureKind);
        Assert.InRange(wait, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(45));
    }

    /// <summary>A restarting voice container refuses the connection rather than answering 503.</summary>
    [Fact]
    public async Task AConnectionRefusedByARestartingServiceIsRetriedJustAsSoon()
    {
        var (row, wait) = await SweepAsync(
            _ => throw new HttpRequestException(HttpRequestError.ConnectionError, "connection refused"));

        Assert.Equal(VoiceDialOutFailureKind.ProviderRefused, row.DialOutFailureKind);
        Assert.InRange(wait, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(45));
    }

    /// <summary>A dial whose outcome is unknown is parked far longer, and that is the cost of guessing wrong.</summary>
    [Theory]
    [InlineData(500)]
    [InlineData(504)]
    public async Task AnUndeterminedDialIsParked(int status)
    {
        var (row, wait) = await SweepAsync(_ => Answer(status));

        Assert.Equal(VoiceDialOutFailureKind.ProviderThrew, row.DialOutFailureKind);
        Assert.InRange(wait, TimeSpan.FromSeconds(110), TimeSpan.FromSeconds(135));
    }

    [Fact]
    public async Task ATimedOutDialIsParkedTheSameWay()
    {
        var (row, wait) = await SweepAsync(_ => throw new TaskCanceledException("HttpClient.Timeout"));

        Assert.Equal(VoiceDialOutFailureKind.ProviderThrew, row.DialOutFailureKind);
        Assert.InRange(wait, TimeSpan.FromSeconds(110), TimeSpan.FromSeconds(135));
    }

    /// <summary>The measurement behind the table: reading a refusal as unknown costs this much per attempt.</summary>
    [Fact]
    public async Task ARefusalReadAsUnknownWouldCostFourTimesTheWait()
    {
        var (_, refused) = await SweepAsync(_ => Answer(503, "{\"error\":\"shutting down\"}"));
        var (_, undetermined) = await SweepAsync(_ => Answer(500));

        Assert.True(undetermined > refused * 3,
            $"a refused dial waits {refused.TotalSeconds:0}s and an undetermined one {undetermined.TotalSeconds:0}s");
    }

    /// <summary>A placed call owns the chain from here, so this row stops carrying a deadline.</summary>
    [Theory]
    [InlineData(202)]
    [InlineData(409)]
    public async Task ACallThatIsOnItsWayStandsThisRowDown(int status)
    {
        var (row, _) = await SweepAsync(_ => Answer(status, "{\"call_id\":\"c1\"}"));

        Assert.Null(row.NextRetryAt);
        Assert.Null(row.DialOutFailureKind);
    }

    /// <summary>
    /// A number that is not E.164 is an answered refusal, not a crash: the chain keeps its shape and
    /// the reason an operator needs is on the row rather than in an exception.
    /// </summary>
    [Fact]
    public async Task AnUndiallableNumberIsARefusalWithAReason()
    {
        var provider = Provider(_ => Answer(202));
        var harness = new Harness();
        harness.Registry.GetProvider(CommunicationCapability.VoiceCalls).Returns(provider);

        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            Title = "db down",
            Status = IncidentStatus.Open,
            Severity = IncidentSeverity.High,
            StartedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        var callLog = new CallLog
        {
            Id = Guid.NewGuid(),
            IncidentId = incident.Id,
            PhoneNumber = "0532 123 45 67",
            Status = CallStatus.NoAnswer,
            AttemptNumber = 1,
            InitiatedAt = DateTime.UtcNow.AddMinutes(-2),
            CompletedAt = DateTime.UtcNow.AddMinutes(-1),
            NextRetryAt = DateTime.UtcNow.AddSeconds(-5),
            CreatedAt = DateTime.UtcNow.AddMinutes(-2)
        };

        await using (var seed = harness.NewContext())
        {
            seed.Incidents.Add(incident);
            seed.CallLogs.Add(callLog);
            await seed.SaveChangesAsync();
        }

        await harness.Job().Execute(JobContext());

        await using var verify = harness.NewContext();
        var stored = await verify.CallLogs.AsNoTracking().SingleAsync(c => c.Id == callLog.Id);

        Assert.Equal(VoiceDialOutFailureKind.ProviderRefused, stored.DialOutFailureKind);
        Assert.NotNull(stored.NextRetryAt);
    }

    // ------------------------------------------------------------------ wiring

    /// <summary>An adapter nothing can resolve is an adapter that never places a call.</summary>
    [Fact]
    public void TheAdapterIsRegisteredUnderTheNameItAnswersTo()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions<CommunicationSettingsOptions>();
        services.AddCommunicationModule(disableSsl: false);

        using var built = services.BuildServiceProvider();
        var registry = built.GetRequiredService<ICommunicationProviderRegistry>();

        Assert.Contains("callu-voice", registry.GetAvailableProviderTypes());
    }

    /// <summary>
    /// The bearer token places PSTN calls, so it must be one of the keys the provider service
    /// encrypts before the row is written.
    /// </summary>
    [Fact]
    public async Task TheApiTokenIsEncryptedBeforeItIsStored()
    {
        var providers = Substitute.For<ICommunicationProviderRepository>();
        var transactions = Substitute.For<ITransactionManager>();
        var registry = Substitute.For<ICommunicationProviderRegistry>();
        var audit = Substitute.For<IAuditLogService>();
        var protector = new ProviderSecretProtector(
            new EphemeralDataProtectionProvider(), NullLogger<ProviderSecretProtector>.Instance);

        registry.GetAvailableProviderTypes().Returns(["callu-voice"]);
        transactions.ExecuteInTransactionAsync(Arg.Any<Func<Task<Guid>>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<Task<Guid>>>()());

        CommunicationProvider? stored = null;
        providers.AddAsync(Arg.Do<CommunicationProvider>(p => stored = p), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var service = new CommunicationProviderService(
            providers, Substitute.For<ICapabilityProviderMappingRepository>(), transactions, registry, protector, audit,
            NullLogger<CommunicationProviderService>.Instance);

        await service.CreateProviderAsync(new CreateProviderRequest
        {
            Name = "voice",
            ProviderType = "callu-voice",
            Config = new Dictionary<string, object>
            {
                ["baseUrl"] = BaseUrl,
                ["apiToken"] = "live-token",
                ["callbackUrl"] = CallbackUrl
            }
        });

        Assert.NotNull(stored);
        Assert.Contains(ProviderSecretProtector.CipherPrefix, stored!.ConfigJson);
        Assert.DoesNotContain("live-token", stored.ConfigJson);
        Assert.Contains(BaseUrl, stored.ConfigJson);
        Assert.Equal(CommunicationCapability.VoiceCalls, stored.Capabilities);

        await audit.Received().LogAsync(
            Arg.Any<string?>(), Arg.Any<AuditAction>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Is<string?>(d => d != null && d.Contains("apiToken")),
            Arg.Any<CancellationToken>());
    }
}
