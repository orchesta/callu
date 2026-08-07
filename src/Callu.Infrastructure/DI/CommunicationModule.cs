using System.Net;
using System.Net.Sockets;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Infrastructure.Configuration;
using Callu.Infrastructure.Persistence.Voximplant;
using Callu.Infrastructure.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace Callu.Infrastructure.DI;

/// <summary>
/// Communication providers, HttpClients, and provider registry
/// </summary>
internal static class CommunicationModule
{
    internal static IServiceCollection AddCommunicationModule(this IServiceCollection services, bool disableSsl)
    {
        var voximplantBuilder = services.AddHttpClient("Voximplant");
        var verimorBuilder = services.AddHttpClient("Verimor");
        var webhookBuilder = services.AddHttpClient("WebhookDispatch");

        // No resilience handler: the shared policy caps an attempt at 15s, and callu-voice renders every
        // prompt before it answers POST /calls. The provider bounds each request itself and never retries.
        var calluVoiceBuilder = services.AddHttpClient(Providers.CalluVoice.CalluVoiceProvider.HttpClientName);

        var httpSmsBuilder = services.AddHttpClient("HttpSms");
        httpSmsBuilder.ConfigurePrimaryHttpMessageHandler(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<CommunicationSettingsOptions>>().Value;
            if (!settings.AllowPrivateSmsEndpoint)
                return CreatePublicIpPinnedHandler(acceptAnyServerCertificate: disableSsl);

            var handler = new HttpClientHandler { AllowAutoRedirect = false };
            if (disableSsl)
                handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            return handler;
        });
        httpSmsBuilder.AddStandardResilienceHandler(ConfigureNonIdempotentProvider);

        webhookBuilder.ConfigurePrimaryHttpMessageHandler(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<CommunicationSettingsOptions>>().Value;
            return CreateIpPinnedHandler(
                acceptAnyServerCertificate: disableSsl,
                allowPrivate: settings.AllowPrivateWebhookEndpoint);
        });

        services.AddHttpClient("HealthCheck", client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "CalluApp-HealthCheck/1.0");
        })
        .ConfigurePrimaryHttpMessageHandler(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<CommunicationSettingsOptions>>().Value;
            return CreateIpPinnedHandler(
                acceptAnyServerCertificate: false,
                allowPrivate: settings.AllowPrivateHealthCheckEndpoint);
        });

        if (disableSsl)
        {
            voximplantBuilder.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            });
            verimorBuilder.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            });
            calluVoiceBuilder.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            });
        }

        // Voximplant StartScenarios (GET) places a real call and Verimor send.json bills an SMS;
        // an HTTP-layer retry on a slow response duplicates them. Retries stay with the Quartz jobs.
        voximplantBuilder.AddStandardResilienceHandler(ConfigureNonIdempotentProvider);
        verimorBuilder.AddStandardResilienceHandler(ConfigureNonIdempotentProvider);
        // Also non-idempotent: a POST that timed out may already have been processed, and the
        // receiver cannot tell a retry from a new event. Retrying is the WebhookDelivery ladder's job.
        webhookBuilder.AddStandardResilienceHandler(ConfigureNonIdempotentProvider);

        services.AddScoped<Application.Services.ICommunicationProviderService, Services.CommunicationProviderService>();
        services.AddScoped<Application.Services.ISipTrunkService, Services.SipTrunkService>();
        services.AddScoped<Application.Services.IVoximplantManagementService, Services.VoximplantManagementService>();
        services.AddScoped<ICallTokenFactoryRepository, CallTokenFactoryRepository>();
        services.AddScoped<IVoximplantScenarioKeyValidator, VoximplantScenarioKeyValidator>();
        services.AddScoped<IVoximplantCallReadPersistence, VoximplantCallReadPersistence>();
        services.AddScoped<IVoximplantVoiceCallbackPersistence, VoximplantVoiceCallbackPersistence>();

        services.AddScoped<Providers.Voximplant.VoximplantCallDataService>();
        services.AddScoped<Application.Services.ICallDataService>(sp => sp.GetRequiredService<Providers.Voximplant.VoximplantCallDataService>());

        services.AddSingleton<Providers.Voximplant.VoxSipPasswordProtector>();

        services.AddSingleton<Providers.ProviderSecretProtector>();

        services.AddSingleton<Providers.Voximplant.SipTrunkPasswordProtector>();

        services.AddOptions<Application.Services.VoximplantReplayGuardOptions>()
            .Configure(o => o.WindowSeconds = 300);
        services.AddSingleton<Application.Services.IVoximplantReplayGuard, Providers.Voximplant.VoximplantReplayGuard>();

        services.AddScoped<Application.Services.IVideoConferenceService, Services.VideoConferenceService>();
        services.AddScoped<Application.Services.ITtsTemplateService, Services.TtsTemplateService>();
        services.AddScoped<Application.Services.IRecipientLanguageResolver, Services.RecipientLanguageResolver>();

        services.AddScoped<Application.Providers.ICommunicationProviderLifecycle, Providers.Voximplant.VoximplantProviderLifecycle>();

        services.AddScoped<Application.Events.ICommunicationEventDispatcher, Events.CommunicationEventDispatcher>();

        services.AddTransient<Providers.Voximplant.VoximplantProvider>();
        services.AddTransient<Providers.Verimor.VerimorProvider>();
        services.AddTransient<Providers.HttpSms.HttpSmsProvider>();
        services.AddTransient<Providers.CalluVoice.CalluVoiceProvider>();

        services.AddSingleton<Application.Providers.ICommunicationProviderRegistry>(sp =>
        {
            var registry = new Providers.CommunicationProviderRegistry(
                sp,
                sp.GetRequiredService<IOptions<CommunicationSettingsOptions>>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Providers.CommunicationProviderRegistry>>());

            registry.RegisterProviderType("voximplant", typeof(Providers.Voximplant.VoximplantProvider));
            registry.RegisterProviderType("verimor", typeof(Providers.Verimor.VerimorProvider));
            registry.RegisterProviderType("http-sms", typeof(Providers.HttpSms.HttpSmsProvider));
            registry.RegisterProviderType("callu-voice", typeof(Providers.CalluVoice.CalluVoiceProvider));

            return registry;
        });

        return services;
    }

    private static void ConfigureNonIdempotentProvider(HttpStandardResilienceOptions options)
    {
        options.Retry.ShouldHandle = _ => ValueTask.FromResult(false);
        options.AttemptTimeout = new HttpTimeoutStrategyOptions
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    private static SocketsHttpHandler CreatePublicIpPinnedHandler(bool acceptAnyServerCertificate)
        => CreateIpPinnedHandler(acceptAnyServerCertificate, allowPrivate: false);

    private static SocketsHttpHandler CreateIpPinnedHandler(bool acceptAnyServerCertificate, bool allowPrivate)
    {
        var handler = new SocketsHttpHandler
        {
            // No MaxAutomaticRedirections here: the flag above already refuses redirects, and the
            // setter rejects 0 — which threw while building the handler, so every outbound request
            // failed before it was sent.
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            MaxConnectionsPerServer = 10,
        };

        if (acceptAnyServerCertificate)
            handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;

        handler.ConnectCallback = async (context, ct) =>
        {
            var host = context.DnsEndPoint.Host;
            var addresses = await Dns.GetHostAddressesAsync(host, ct);

            // Every allowed address, in order — not just the first, so an HA gateway with one dead
            // node still connects. The per-address check is unchanged, so the SSRF guarantee holds.
            var allowed = Array.FindAll(addresses, a => UrlSanitizer.IsAllowedTargetIp(a, allowPrivate));
            if (allowed.Length == 0)
                throw new HttpRequestException($"Blocked: '{host}' did not resolve to an allowed IP address.");

            Exception? last = null;
            foreach (var ip in allowed)
            {
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(ip, context.DnsEndPoint.Port), ct);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (Exception ex)
                {
                    socket.Dispose();
                    if (ct.IsCancellationRequested) throw;
                    last = ex;
                }
            }

            throw new HttpRequestException(
                $"Could not connect to any allowed address for '{host}' ({allowed.Length} tried).", last);
        };

        return handler;
    }
}
