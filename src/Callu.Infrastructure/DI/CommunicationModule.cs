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

        var httpSmsBuilder = services.AddHttpClient("HttpSms");
        httpSmsBuilder.ConfigurePrimaryHttpMessageHandler(() =>
        {
            var handler = new HttpClientHandler { AllowAutoRedirect = false };
            if (disableSsl)
                handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            return handler;
        });
        httpSmsBuilder.AddStandardResilienceHandler(options =>
        {
            options.Retry.MaxRetryAttempts = 2;
            options.AttemptTimeout = new HttpTimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
        });

        webhookBuilder.ConfigurePrimaryHttpMessageHandler(() =>
        {
            var handler = new HttpClientHandler { AllowAutoRedirect = false, MaxAutomaticRedirections = 0 };
            if (disableSsl)
                handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            return handler;
        });

        services.AddHttpClient("HealthCheck", client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "CalluApp-HealthCheck/1.0");
        })
        .ConfigurePrimaryHttpMessageHandler(() =>
        {
            var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                MaxAutomaticRedirections = 0,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                MaxConnectionsPerServer = 10,
            };
            handler.ConnectCallback = async (context, ct) =>
            {
                var host = context.DnsEndPoint.Host;
                var addresses = await Dns.GetHostAddressesAsync(host, ct);
                var ip = Array.Find(addresses, UrlSanitizer.IsPublicIp)
                    ?? throw new HttpRequestException($"Blocked: '{host}' did not resolve to a public IP address.");

                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(ip, context.DnsEndPoint.Port), ct);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            };
            return handler;
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
        }

        voximplantBuilder.AddStandardResilienceHandler();
        verimorBuilder.AddStandardResilienceHandler();
        webhookBuilder.AddStandardResilienceHandler(options =>
        {
            options.Retry.MaxRetryAttempts = 2;
            options.AttemptTimeout = new HttpTimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
        });

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

        services.AddScoped<Application.Providers.ICommunicationProviderLifecycle, Providers.Voximplant.VoximplantProviderLifecycle>();

        services.AddScoped<Application.Events.ICommunicationEventDispatcher, Events.CommunicationEventDispatcher>();

        services.AddTransient<Providers.Voximplant.VoximplantProvider>();
        services.AddTransient<Providers.Verimor.VerimorProvider>();
        services.AddTransient<Providers.HttpSms.HttpSmsProvider>();

        services.AddSingleton<Application.Providers.ICommunicationProviderRegistry>(sp =>
        {
            var registry = new Providers.CommunicationProviderRegistry(
                sp,
                sp.GetRequiredService<IOptions<CommunicationSettingsOptions>>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Providers.CommunicationProviderRegistry>>());

            registry.RegisterProviderType("voximplant", typeof(Providers.Voximplant.VoximplantProvider));
            registry.RegisterProviderType("verimor", typeof(Providers.Verimor.VerimorProvider));
            registry.RegisterProviderType("http-sms", typeof(Providers.HttpSms.HttpSmsProvider));

            return registry;
        });

        return services;
    }
}
