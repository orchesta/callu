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

        services.AddScoped<Application.Services.IEmailTemplateService, Services.EmailTemplateService>();

        return services;
    }
}
