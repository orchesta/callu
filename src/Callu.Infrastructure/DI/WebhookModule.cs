using Microsoft.Extensions.DependencyInjection;

namespace Callu.Infrastructure.DI;

/// <summary>
/// Webhook services — capture, templates, processing, alert rules
/// </summary>
internal static class WebhookModule
{
    internal static IServiceCollection AddWebhookModule(this IServiceCollection services)
    {
        services.AddScoped<Application.Services.IWebhookCaptureService, Services.WebhookCaptureService>();
        services.AddScoped<Application.Services.IWebhookTemplateService, Services.WebhookTemplateService>();
        services.AddScoped<Application.Services.IWebhookProcessingService, Services.WebhookProcessingService>();
        services.AddScoped<Application.Services.IWebhookConfigService, Services.WebhookConfigService>();
        services.AddSingleton<Application.Services.IWebhookSignatureVerifier, Services.HmacWebhookSignatureVerifier>();
        services.AddSingleton<Services.IWebhookPayloadParser, Services.WebhookPayloadParser>();

        services.AddScoped<Application.Services.IAlertRuleService, Services.AlertRuleService>();
        services.AddScoped<Application.Services.IAlertRuleEngine, Services.AlertRuleEngine>();

        return services;
    }
}
