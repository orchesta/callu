using System.Net;
using System.Text.Json;
using Callu.Application.Common.Interfaces;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.DI;
using Callu.Infrastructure.Providers;
using Callu.Infrastructure.Providers.CalluVoice;
using Callu.Infrastructure.Providers.Voximplant;
using Callu.Infrastructure.Quartz;
using Callu.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Quartz;

namespace Callu.Tests;

/// <summary>One voice service holds one carrier, whatever the panel was talked into storing.</summary>
// Swept per row, two providers on one service each disagreed with what the other had just applied:
// two config rewrites and two PJSIP reloads every few minutes, forever. A page originated inside a
// reload dials an endpoint the replacement has already destroyed, so the responder is never called
// and escalation moves on believing the number failed.
[Collection(PostgresCollection.Name)]
public class VoiceCarrierOneServiceOneCarrierTests(PostgresFixture pg)
{
    /// <summary>Every sweep sends the same carrier, so there is nothing left to flip.</summary>
    [PostgresFact]
    public async Task TwoProvidersOnOneVoiceServiceNeverFlipTheCarrier()
    {
        var world = await ArrangeAsync();

        for (var sweep = 0; sweep < 3; sweep++)
            await world.Job.Execute(JobContext());

        var pushed = new List<string>();
        foreach (var put in world.Sent.Where(r => r.Method == HttpMethod.Put))
            pushed.Add(await put.Content!.ReadAsStringAsync());

        Assert.NotEmpty(pushed);
        Assert.All(pushed, body =>
            Assert.Contains("\"host\":\"sip.primary.example\"", body, StringComparison.Ordinal));
        Assert.DoesNotContain(pushed, body =>
            body.Contains("sip.backup.example", StringComparison.Ordinal));
    }

    /// <summary>The provider whose carrier is being ignored is not something to leave unsaid.</summary>
    [PostgresFact]
    public async Task TheConflictIsWrittenWhereAnOperatorLooks()
    {
        var world = await ArrangeAsync();

        await world.Job.Execute(JobContext());

        await using var db = PostgresFixture.Context(world.ConnectionString);
        var rows = await db.Set<AuditLog>().AsNoTracking()
            .Where(a => a.ResourceType == "CommunicationProvider")
            .ToListAsync();

        Assert.Contains(rows, r => (r.Summary ?? "").Contains("Backup voice", StringComparison.Ordinal));
    }

    /// <summary>The sweep runs every few minutes; a row per sweep buries the trail it is leaving.</summary>
    [PostgresFact]
    public async Task TheConflictIsNotWrittenAgainOnEverySweep()
    {
        var world = await ArrangeAsync();

        for (var sweep = 0; sweep < 3; sweep++)
            await world.Job.Execute(JobContext());

        await using var db = PostgresFixture.Context(world.ConnectionString);
        var rows = await db.Set<AuditLog>().AsNoTracking()
            .Where(a => a.ResourceType == "CommunicationProvider")
            .ToListAsync();

        Assert.Single(rows, r => (r.Summary ?? "").Contains("Backup voice", StringComparison.Ordinal));
    }

    /// <summary>Two providers sharing one carrier is not a conflict, and must not read as one.</summary>
    // They agree about what the voice service should hold, so there is nothing to tell anyone.
    [PostgresFact]
    public async Task TwoProvidersThatNameTheSameCarrierAreNotReported()
    {
        var world = await ArrangeAsync(shareOneTrunk: true);

        await world.Job.Execute(JobContext());

        Assert.Single(world.Sent, r => r.Method == HttpMethod.Put);

        await using var db = PostgresFixture.Context(world.ConnectionString);
        var rows = await db.Set<AuditLog>().AsNoTracking()
            .Where(a => a.ResourceType == "CommunicationProvider")
            .ToListAsync();

        Assert.DoesNotContain(rows, r => (r.Summary ?? "").Contains("shares its voice service", StringComparison.Ordinal));
    }

    private static IJobExecutionContext JobContext()
    {
        var context = Substitute.For<IJobExecutionContext>();
        context.CancellationToken.Returns(CancellationToken.None);
        return context;
    }

    private sealed record World(
        string ConnectionString,
        VoiceCarrierReconciliationQuartzJob Job,
        List<HttpRequestMessage> Sent);

    private async Task<World> ArrangeAsync(bool shareOneTrunk = false)
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var sent = new List<HttpRequestMessage>();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddSingleton(Substitute.For<ICurrentUserService>());
        services.AddPersistenceModule(cs, enableParameterLogging: false);
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddSingleton<ProviderSecretProtector>();
        services.AddSingleton<SipTrunkPasswordProtector>();
        services.AddSingleton(Substitute.For<ITtsTemplateService>());
        services.AddTransient<CalluVoiceProvider>();

        var root = services.BuildServiceProvider();
        var trunkProtector = root.GetRequiredService<SipTrunkPasswordProtector>();
        var secrets = root.GetRequiredService<ProviderSecretProtector>();

        await using (var db = PostgresFixture.Context(cs))
        {
            var primary = new SipTrunkSettings
            {
                Name = "Primary carrier",
                Server = "sip.primary.example",
                Port = 5060,
                Username = "900001",
                Password = trunkProtector.Protect("s3cr3t"),
                IsEnabled = true,
            };
            var backup = new SipTrunkSettings
            {
                Name = "Backup carrier",
                Server = "sip.backup.example",
                Port = 5060,
                Username = "900002",
                Password = trunkProtector.Protect("s3cr3t"),
                IsEnabled = true,
            };
            db.Add(primary);
            db.Add(backup);
            await db.SaveChangesAsync();

            // The same voice service twice, spelled the way two operators would each spell it.
            db.Add(Provider("Primary voice", priority: 1, "http://callu-voice:8090", primary.Id, secrets));
            db.Add(Provider("Backup voice", priority: 2, "http://Callu-Voice:8090/",
                shareOneTrunk ? primary.Id : backup.Id, secrets));
            await db.SaveChangesAsync();
        }

        // The service is holding nothing, so a push is warranted on every sweep.
        services.AddSingleton<IHttpClientFactory>(
            new StubHttpClientFactory(new TrunkHandler(sent, CalluVoiceTrunk.None.Fingerprint())));
        var withHttp = services.BuildServiceProvider();

        var job = new VoiceCarrierReconciliationQuartzJob(
            withHttp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<VoiceCarrierReconciliationQuartzJob>.Instance);

        return new World(cs, job, sent);
    }

    private static CommunicationProvider Provider(
        string name, int priority, string baseUrl, Guid trunkId, ProviderSecretProtector secrets) => new()
        {
            Name = name,
            ProviderType = CalluVoiceProvider.TypeName,
            ConfigJson = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["baseUrl"] = baseUrl,
                ["apiToken"] = secrets.Protect("tok"),
                ["callbackUrl"] = "http://callu-api:5095",
            }),
            SipTrunkId = trunkId,
            IsEnabled = true,
            Priority = priority,
            Capabilities = CommunicationCapability.VoiceCalls,
        };

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class TrunkHandler(List<HttpRequestMessage> seen, string fingerprint) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            seen.Add(new HttpRequestMessage(request.Method, request.RequestUri)
            {
                Content = new StringContent(body),
            });

            return request.Method == HttpMethod.Get
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($"{{\"enabled\":true,\"fingerprint\":\"{fingerprint}\"}}"),
                }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }
}
