using Microsoft.Extensions.DependencyInjection;

namespace Callu.Infrastructure.DI;

/// <summary>
/// Email services — SMTP delivery, settings management, templates
/// </summary>
internal static class EmailModule
{
    internal static IServiceCollection AddEmailModule(this IServiceCollection services)
    {
        services.AddScoped<Application.Services.IEmailService, Services.SmtpEmailService>();

        services.AddScoped<Application.Services.ISmtpSettingsService, Services.SmtpSettingsService>();
        services.AddScoped<Application.Services.IFirebaseSettingsService, Services.FirebaseSettingsService>();
        services.AddScoped<Application.Services.IPushDeviceService, Services.PushDeviceService>();
        services.AddScoped<Application.Services.IMobilePushSender, Push.FcmMobilePushSender>();

        services.AddHttpClient("fcm", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddScoped<Application.Services.IEmailTemplateService, Services.EmailTemplateService>();

        // B4: the editor's templates feed the live pipeline (DB first, file fallback).
        services.AddScoped<Application.Services.IDbEmailTemplateResolver, Services.DbEmailTemplateResolver>();

        return services;
    }
}
