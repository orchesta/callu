using System.Net;
using System.Text.Json;
using Callu.Application.Common.Interfaces;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Providers;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.DI;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Infrastructure.Providers;
using Callu.Infrastructure.Providers.CalluVoice;
using Callu.Infrastructure.Providers.Voximplant;
using Callu.Infrastructure.Quartz;
using Callu.Infrastructure.Services;
using Callu.Shared.Models.Communication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Quartz;

namespace Callu.Tests;

/// <summary>A self-hosted voice service holds no carrier of its own, so Callu has to hand it one.</summary>
// The carrier only reaches Asterisk when something pushes it; every path that changes what Callu
// believes the carrier is has to be one of those things.
[Collection(PostgresCollection.Name)]
public class SipTrunkCarrierPushTests(PostgresFixture pg)
{
    /// <summary>Rotating the SIP password is the reason this feature exists.</summary>
    // Saved here and nowhere else, the voice service keeps registering with the old password and
    // the carrier answers 403 — no page leaves the box, and the panel shows the new password.
    [PostgresFact]
    public async Task EditingTheTrunkSendsTheNewCarrierToTheVoiceService()
    {
        var world = await ArrangeAsync();

        await world.Trunks.UpdateTrunkAsync(world.TrunkId, new UpdateSipTrunkRequest
        {
            Name = "Carrier A",
            Server = "sip.carrier.example",
            Port = 5060,
            Username = "900001",
            Password = "rotated",
            IsEnabled = true,
        });

        var body = await Assert.Single(world.Sent).Content!.ReadAsStringAsync();
        Assert.Contains("\"password\":\"rotated\"", body, StringComparison.Ordinal);
    }

    /// <summary>Switching the trunk off in the panel has to switch it off on the voice service.</summary>
    [PostgresFact]
    public async Task DisablingTheTrunkStopsTheVoiceServiceDiallingThroughIt()
    {
        var world = await ArrangeAsync();

        await world.Trunks.UpdateTrunkAsync(world.TrunkId, new UpdateSipTrunkRequest
        {
            Name = "Carrier A",
            Server = "sip.carrier.example",
            Port = 5060,
            Username = "900001",
            IsEnabled = false,
        });

        var body = await Assert.Single(world.Sent).Content!.ReadAsStringAsync();
        Assert.Contains("\"enabled\":false", body, StringComparison.Ordinal);
    }

    /// <summary>A refused push is written where an operator looks, because the save still succeeds.</summary>
    [PostgresFact]
    public async Task ACarrierTheVoiceServiceRefusesIsRecorded()
    {
        var world = await ArrangeAsync(HttpStatusCode.BadRequest);

        await world.Trunks.UpdateTrunkAsync(world.TrunkId, new UpdateSipTrunkRequest
        {
            Name = "Carrier A",
            Server = "sip.carrier.example",
            Port = 5060,
            Username = "900001",
            IsEnabled = true,
        });

        await using var db = PostgresFixture.Context(world.ConnectionString);
        var rows = await db.Set<AuditLog>().AsNoTracking()
            .Where(a => a.ResourceType == "SipTrunk")
            .ToListAsync();

        Assert.Contains(rows, r => (r.Summary ?? "").Contains("NOT applied", StringComparison.Ordinal));
    }

    /// <summary>Deleting the provider has to take the carrier off the voice service with it.</summary>
    // Otherwise Asterisk keeps registering with credentials Callu no longer routes through, and
    // there is no screen left that mentions them.
    [PostgresFact]
    public async Task DeletingTheProviderTakesTheCarrierOffTheVoiceService()
    {
        var world = await ArrangeAsync();

        await world.Providers.DeleteProviderAsync(world.ProviderId);

        var body = await Assert.Single(world.Sent).Content!.ReadAsStringAsync();
        Assert.Contains("\"enabled\":false", body, StringComparison.Ordinal);
    }

    /// <summary>Saving the provider reports success either way, so the refusal needs a home.</summary>
    [PostgresFact]
    public async Task AProviderSaveThatCouldNotApplyTheCarrierIsRecorded()
    {
        var world = await ArrangeAsync(HttpStatusCode.BadRequest);

        await world.Providers.UpdateProviderAsync(world.ProviderId, new UpdateProviderRequest
        {
            Name = "Self-hosted voice",
            IsEnabled = true,
            Priority = 1,
            SipTrunkId = world.TrunkId,
            Config = null,
        });

        await using var db = PostgresFixture.Context(world.ConnectionString);
        var rows = await db.Set<AuditLog>().AsNoTracking()
            .Where(a => a.ResourceType == "CommunicationProvider")
            .ToListAsync();

        Assert.Contains(rows, r => (r.Summary ?? "").Contains("NOT applied", StringComparison.Ordinal));
    }

    /// <summary>A request that changes only the carrier is a change, not a no-op.</summary>
    [PostgresFact]
    public async Task ChangingOnlyTheTrunkOnAProviderIsWritten()
    {
        var world = await ArrangeAsync();

        await world.Providers.UpdateProviderAsync(world.ProviderId, new UpdateProviderRequest
        {
            Name = "Self-hosted voice",
            IsEnabled = true,
            Priority = 1,
            SipTrunkId = null,
            Config = null,
        });

        await using var db = PostgresFixture.Context(world.ConnectionString);
        var stored = await db.Set<CommunicationProvider>().AsNoTracking()
            .FirstAsync(p => p.Id == world.ProviderId);

        Assert.Null(stored.SipTrunkId);
    }

    /// <summary>Taking the trunk off a provider that had one is an explicit removal, so it is sent.</summary>
    [PostgresFact]
    public async Task UnlinkingATrunkTheProviderHadTakesTheCarrierOffTheVoiceService()
    {
        var world = await ArrangeAsync();

        await world.Providers.UpdateProviderAsync(world.ProviderId, new UpdateProviderRequest
        {
            Name = "Self-hosted voice",
            IsEnabled = true,
            Priority = 1,
            SipTrunkId = null,
            Config = null,
        });

        var body = await Assert.Single(world.Sent).Content!.ReadAsStringAsync();
        Assert.Contains("\"enabled\":false", body, StringComparison.Ordinal);
    }

    /// <summary>Saving a provider that never had a trunk says nothing about the carrier, so nothing is sent.</summary>
    // Documented setup order is: add the provider, then the trunk, then link them. With every save
    // pushing what the selector holds, step one destroyed a working carrier and said "Saved".
    [PostgresFact]
    public async Task SavingAProviderThatNeverHadATrunkDoesNotTouchTheCarrier()
    {
        var world = await ArrangeAsync();

        await using (var db = PostgresFixture.Context(world.ConnectionString))
        {
            var row = await db.Set<CommunicationProvider>().FirstAsync(p => p.Id == world.ProviderId);
            row.SipTrunkId = null;
            await db.SaveChangesAsync();
        }
        world.Sent.Clear();

        await world.Providers.UpdateProviderAsync(world.ProviderId, new UpdateProviderRequest
        {
            Name = "Renamed voice",
            IsEnabled = true,
            Priority = 1,
            SipTrunkId = null,
            Config = null,
        });

        Assert.Empty(world.Sent);
    }

    /// <summary>Same rule at creation: a provider that has never had a carrier is not removing one.</summary>
    [PostgresFact]
    public async Task CreatingAProviderWithNoTrunkChosenDoesNotTouchTheCarrier()
    {
        var world = await ArrangeAsync();
        world.Sent.Clear();

        await world.Providers.CreateProviderAsync(new CreateProviderRequest
        {
            Name = "Second voice",
            ProviderType = CalluVoiceProvider.TypeName,
            Priority = 2,
            SipTrunkId = null,
            Config = new Dictionary<string, object>
            {
                ["baseUrl"] = "http://callu-voice-2:8090",
                ["apiToken"] = "tok",
                ["callbackUrl"] = "http://callu-api:5095",
            },
        });

        Assert.Empty(world.Sent);
    }

    /// <summary>A carrier that authenticates under a name of its own is the reason the column exists.</summary>
    [PostgresFact]
    public async Task TheAuthUserReachesTheVoiceService()
    {
        var world = await ArrangeAsync();

        await world.Trunks.UpdateTrunkAsync(world.TrunkId, new UpdateSipTrunkRequest
        {
            Name = "Carrier A",
            Server = "sip.carrier.example",
            Port = 5060,
            Username = "900001",
            AuthUser = "auth-900001",
            IsEnabled = true,
        });

        var body = await Assert.Single(world.Sent).Content!.ReadAsStringAsync();
        Assert.Contains("\"auth_username\":\"auth-900001\"", body, StringComparison.Ordinal);
    }

    /// <summary>A save that says nothing about a field has not asked for it to be cleared.</summary>
    // Otherwise touching the trunk for an unrelated reason logs the carrier out of its auth account.
    [PostgresFact]
    public async Task AnUpdateThatSaysNothingAboutTheAuthUserKeepsIt()
    {
        var world = await ArrangeAsync();

        await world.Trunks.UpdateTrunkAsync(world.TrunkId, new UpdateSipTrunkRequest
        {
            Name = "Carrier A",
            Server = "sip.carrier.example",
            Port = 5060,
            Username = "900001",
            AuthUser = "auth-900001",
            IsEnabled = true,
        });

        await world.Trunks.UpdateTrunkAsync(world.TrunkId, new UpdateSipTrunkRequest
        {
            Name = "Carrier A, renamed",
            Server = "sip.carrier.example",
            Port = 5060,
            Username = "900001",
            IsEnabled = true,
        });

        await using var db = PostgresFixture.Context(world.ConnectionString);
        var stored = await db.Set<SipTrunkSettings>().AsNoTracking().FirstAsync(t => t.Id == world.TrunkId);

        Assert.Equal("auth-900001", stored.AuthUser);
    }

    /// <summary>Switching the trunk row off is a documented way to remove a carrier.</summary>
    // An omitted flag that reads as "on" puts the carrier back on the next unrelated save.
    [PostgresFact]
    public async Task AnUpdateThatSaysNothingAboutEnabledLeavesTheTrunkOff()
    {
        var world = await ArrangeAsync();

        await world.Trunks.UpdateTrunkAsync(world.TrunkId, new UpdateSipTrunkRequest
        {
            Name = "Carrier A",
            Server = "sip.carrier.example",
            Port = 5060,
            Username = "900001",
            IsEnabled = false,
        });

        await world.Trunks.UpdateTrunkAsync(world.TrunkId, new UpdateSipTrunkRequest
        {
            Name = "Carrier A",
            Server = "sip.carrier.example",
            Port = 5060,
            Username = "900001",
        });

        await using var db = PostgresFixture.Context(world.ConnectionString);
        var stored = await db.Set<SipTrunkSettings>().AsNoTracking().FirstAsync(t => t.Id == world.TrunkId);

        Assert.False(stored.IsEnabled);
    }

    /// <summary>The transport is the same shape: an omitted flag is not a request to change it.</summary>
    // Read as "off" it silently moves a carrier back to UDP, and the registration stops.
    [PostgresFact]
    public async Task AnUpdateThatSaysNothingAboutTheTransportKeepsIt()
    {
        var world = await ArrangeAsync();

        await world.Trunks.UpdateTrunkAsync(world.TrunkId, new UpdateSipTrunkRequest
        {
            Name = "Carrier A",
            Server = "sip.carrier.example",
            Port = 5060,
            Username = "900001",
            UseTcp = true,
            IsEnabled = true,
        });

        await world.Trunks.UpdateTrunkAsync(world.TrunkId, new UpdateSipTrunkRequest
        {
            Name = "Carrier A, renamed",
            Server = "sip.carrier.example",
            Port = 5060,
            Username = "900001",
            IsEnabled = true,
        });

        await using var db = PostgresFixture.Context(world.ConnectionString);
        var stored = await db.Set<SipTrunkSettings>().AsNoTracking().FirstAsync(t => t.Id == world.TrunkId);

        Assert.True(stored.UseTcp);
    }

    /// <summary>A voice service holds one carrier, so a second provider naming it cannot work.</summary>
    // Both would push their own and the sweep would replace one with the other every few minutes.
    [PostgresFact]
    public async Task ASecondProviderOnTheSameVoiceServiceIsRefused()
    {
        var world = await ArrangeAsync();

        var refused = await Assert.ThrowsAsync<Callu.Shared.Exceptions.ValidationException>(
            () => world.Providers.CreateProviderAsync(new CreateProviderRequest
            {
                Name = "Backup voice",
                ProviderType = CalluVoiceProvider.TypeName,
                Priority = 2,
                SipTrunkId = world.TrunkId,
                Config = new Dictionary<string, object>
                {
                    // The same service, written the way a second operator would write it.
                    ["baseUrl"] = "http://Callu-Voice:8090/",
                    ["apiToken"] = "tok",
                    ["callbackUrl"] = "http://callu-api:5095",
                },
            }));

        Assert.Contains("Self-hosted voice", refused.Message, StringComparison.Ordinal);
    }

    private sealed record World(
        string ConnectionString,
        Guid TrunkId,
        Guid ProviderId,
        ISipTrunkService Trunks,
        ICommunicationProviderService Providers,
        List<HttpRequestMessage> Sent);

    private async Task<World> ArrangeAsync(HttpStatusCode answer = HttpStatusCode.OK)
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddSingleton(Substitute.For<ICurrentUserService>());
        services.AddPersistenceModule(cs, enableParameterLogging: false);
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddScoped<IAuditLogService, AuditLogService>();

        var sp = services.BuildServiceProvider().CreateScope().ServiceProvider;
        var keyring = sp.GetRequiredService<IDataProtectionProvider>();
        var trunkProtector = new SipTrunkPasswordProtector(keyring, NullLogger<SipTrunkPasswordProtector>.Instance);
        var secrets = new ProviderSecretProtector(keyring, NullLogger<ProviderSecretProtector>.Instance);

        Guid trunkId, providerId;
        await using (var db = PostgresFixture.Context(cs))
        {
            var trunk = new SipTrunkSettings
            {
                Name = "Carrier A",
                Server = "sip.carrier.example",
                Port = 5060,
                Username = "900001",
                Password = trunkProtector.Protect("s3cr3t"),
                IsEnabled = true,
            };
            db.Add(trunk);
            await db.SaveChangesAsync();
            trunkId = trunk.Id;

            var provider = new CommunicationProvider
            {
                Name = "Self-hosted voice",
                ProviderType = CalluVoiceProvider.TypeName,
                ConfigJson = JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["baseUrl"] = "http://callu-voice:8090",
                    ["apiToken"] = secrets.Protect("tok"),
                    ["callbackUrl"] = "http://callu-api:5095",
                }),
                SipTrunkId = trunkId,
                IsEnabled = true,
                Priority = 1,
                Capabilities = CommunicationCapability.VoiceCalls,
            };
            db.Add(provider);
            await db.SaveChangesAsync();
            providerId = provider.Id;
        }

        var sent = new List<HttpRequestMessage>();
        var registry = Substitute.For<ICommunicationProviderRegistry>();
        registry.GetAvailableProviderTypes().Returns([CalluVoiceProvider.TypeName]);

        // Rebuilt on every ask, and for whichever id is asked about — a provider created during
        // the test has to be answerable too, or a push to it goes nowhere and proves nothing.
        registry.GetConfiguredProviderAsync(Arg.Any<Guid>()).Returns(call =>
            BuildVoiceProviderAsync(cs, call.Arg<Guid>(), keyring, secrets, trunkProtector, sent, answer));

        var trunks = new SipTrunkService(
            sp.GetRequiredService<ISipTrunkSettingsRepository>(),
            sp.GetRequiredService<ICommunicationProviderRepository>(),
            sp.GetRequiredService<ITransactionManager>(),
            trunkProtector,
            sp.GetRequiredService<IAuditLogService>(),
            registry,
            NullLogger<SipTrunkService>.Instance);

        var providers = new CommunicationProviderService(
            sp.GetRequiredService<ICommunicationProviderRepository>(),
            sp.GetRequiredService<ICapabilityProviderMappingRepository>(),
            sp.GetRequiredService<ITransactionManager>(),
            registry,
            secrets,
            sp.GetRequiredService<IAuditLogService>(),
            NullLogger<CommunicationProviderService>.Instance);

        return new World(cs, trunkId, providerId, trunks, providers, sent);
    }

    private static async Task<ICommunicationProvider?> BuildVoiceProviderAsync(
        string connectionString,
        Guid providerId,
        IDataProtectionProvider keyring,
        ProviderSecretProtector secrets,
        SipTrunkPasswordProtector trunkProtector,
        List<HttpRequestMessage> sent,
        HttpStatusCode answer)
    {
        await using var db = PostgresFixture.Context(connectionString);
        var row = await db.Set<CommunicationProvider>().AsNoTracking()
            .Include(p => p.SipTrunk)
            .FirstOrDefaultAsync(p => p.Id == providerId && !p.IsDeleted);
        if (row is null) return null;

        var provider = new CalluVoiceProvider(
            new StubHttpClientFactory(new RecordingHandler(sent, answer)),
            Substitute.For<ITtsTemplateService>(),
            secrets,
            trunkProtector,
            keyring,
            NullLogger<CalluVoiceProvider>.Instance);

        await provider.InitializeAsync(row.ConfigJson ?? "{}", row.SipTrunk);
        return provider;
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class RecordingHandler(List<HttpRequestMessage> seen, HttpStatusCode answer) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            seen.Add(new HttpRequestMessage(request.Method, request.RequestUri)
            {
                Content = new StringContent(body),
            });
            return new HttpResponseMessage(answer) { Content = new StringContent("{}") };
        }
    }
}


/// <summary>Watching the reconciliation actually fire: the carrier goes back on its own.</summary>
// The voice service holds no carrier across a restart, so without a sweep the only symptom of a
// container recreate is a quiet night — the panel still shows the carrier, and no page goes out.
[Collection(PostgresCollection.Name)]
public class VoiceCarrierReconciliationQuartzJobTests(PostgresFixture pg)
{
    /// <summary>The container came back with nothing; the sweep notices and sends the carrier again.</summary>
    [PostgresFact]
    public async Task ACarrierTheVoiceServiceHasLostIsPutBack()
    {
        var world = await ArrangeAsync(reports: CalluVoiceTrunk.None.Fingerprint());

        await world.Job.Execute(JobContext());

        var put = Assert.Single(world.Sent, r => r.Method == HttpMethod.Put);
        var body = await put.Content!.ReadAsStringAsync();
        Assert.Contains("\"host\":\"sip.carrier.example\"", body, StringComparison.Ordinal);
        Assert.Contains("\"password\":\"s3cr3t\"", body, StringComparison.Ordinal);
    }

    /// <summary>A restore is the one thing here an operator has to be able to find afterwards.</summary>
    [PostgresFact]
    public async Task ARestoredCarrierIsWrittenWhereAnOperatorLooks()
    {
        var world = await ArrangeAsync(reports: CalluVoiceTrunk.None.Fingerprint());

        await world.Job.Execute(JobContext());

        await using var db = PostgresFixture.Context(world.ConnectionString);
        var rows = await db.Set<AuditLog>().AsNoTracking()
            .Where(a => a.ResourceType == "CommunicationProvider")
            .ToListAsync();

        Assert.Contains(rows, r => (r.Summary ?? "").Contains("restored", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Every other sweep must be silent, or the one that mattered is buried.</summary>
    // This runs every few minutes forever; a row or a reload per sweep is noise nobody can read past.
    [PostgresFact]
    public async Task ASweepThatFoundNothingWrongWritesNothingAndSendsNothing()
    {
        var world = await ArrangeAsync(reports: null);

        await world.Job.Execute(JobContext());

        Assert.DoesNotContain(world.Sent, r => r.Method == HttpMethod.Put);

        await using var db = PostgresFixture.Context(world.ConnectionString);
        var rows = await db.Set<AuditLog>().AsNoTracking()
            .Where(a => a.ResourceType == "CommunicationProvider")
            .ToListAsync();

        Assert.Empty(rows);
    }

    /// <summary>A provider with no trunk chosen has said nothing about a carrier, so the sweep says nothing either.</summary>
    // Reading it as "no carrier" would make the sweep the thing that deletes a working trunk, every
    // few minutes, for as long as the selector stayed empty.
    [PostgresFact]
    public async Task AProviderWithNoTrunkChosenIsNotSweptAtAll()
    {
        var world = await ArrangeAsync(reports: CalluVoiceTrunk.None.Fingerprint(), linkTrunk: false);

        await world.Job.Execute(JobContext());

        Assert.Empty(world.Sent);
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

    /// <summary>reports: what GET /trunk answers with; null means "exactly the carrier Callu holds".</summary>
    private async Task<World> ArrangeAsync(string? reports, bool linkTrunk = true)
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

        Guid trunkId;
        await using (var db = PostgresFixture.Context(cs))
        {
            var trunk = new SipTrunkSettings
            {
                Name = "Carrier A",
                Server = "sip.carrier.example",
                Port = 5060,
                Username = "900001",
                Password = trunkProtector.Protect("s3cr3t"),
                IsEnabled = true,
            };
            db.Add(trunk);
            await db.SaveChangesAsync();
            trunkId = trunk.Id;

            db.Add(new CommunicationProvider
            {
                Name = "Self-hosted voice",
                ProviderType = CalluVoiceProvider.TypeName,
                ConfigJson = JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["baseUrl"] = "http://callu-voice:8090",
                    ["apiToken"] = secrets.Protect("tok"),
                    ["callbackUrl"] = "http://callu-api:5095",
                }),
                SipTrunkId = linkTrunk ? trunkId : null,
                IsEnabled = true,
                Priority = 1,
                Capabilities = CommunicationCapability.VoiceCalls,
            });
            await db.SaveChangesAsync();
        }

        var configured = new SipTrunkSettings
        {
            Server = "sip.carrier.example", Port = 5060, Username = "900001", IsEnabled = true,
        };
        var answer = reports ?? CalluVoiceTrunk.From(configured, "s3cr3t").Fingerprint();

        services.AddSingleton<IHttpClientFactory>(new StubHttpClientFactory(new TrunkHandler(sent, answer)));
        var withHttp = services.BuildServiceProvider();

        var job = new VoiceCarrierReconciliationQuartzJob(
            withHttp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<VoiceCarrierReconciliationQuartzJob>.Instance);

        return new World(cs, job, sent);
    }

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
