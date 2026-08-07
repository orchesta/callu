using Callu.Infrastructure.Configuration;
using Callu.Infrastructure.DI;
using Callu.Infrastructure.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Callu.Infrastructure;

/// <summary>
/// Dependency injection extensions for Infrastructure layer.
/// Delegates to domain-specific modules for service registration.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Add all infrastructure services to the DI container
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection") 
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found");

        var disableSsl = configuration.GetValue<bool>("CommunicationSettings:DisableSslValidation");

        services.Configure<CommunicationSettingsOptions>(
            configuration.GetSection(CommunicationSettingsOptions.SectionName));

        services.Configure<RetentionOptions>(
            configuration.GetSection(RetentionOptions.SectionName));

        services.Configure<Audit.AuditArchiveOptions>(
            configuration.GetSection(Audit.AuditArchiveOptions.SectionName));

        services.Configure<Audit.AuditSyslogOptions>(
            configuration.GetSection(Audit.AuditSyslogOptions.SectionName));

        services.Configure<Audit.AuditSigningOptions>(
            configuration.GetSection(Audit.AuditSigningOptions.SectionName));
        services.AddScoped<Application.Services.IAuditSigningKeyService, Audit.AuditSigningKeyService>();

        services.AddSingleton<CalluMetrics>();
        services.AddHttpContextAccessor();
        services.AddCachingModule(configuration);
        services.AddCommunicationModule(disableSsl);

        var isDevelopment = string.Equals(
            configuration["ASPNETCORE_ENVIRONMENT"] ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
            "Development",
            StringComparison.OrdinalIgnoreCase);
        services.AddPersistenceModule(connectionString, isDevelopment);

        services.AddCoreServicesModule();
        services.AddTracingQueryModule(configuration);
        services.AddIncidentModule();
        services.AddSchedulingModule();
        services.AddNotificationModule();
        services.AddWebhookModule();
        services.AddEmailModule();
        services.AddStatusPageModule();

        return services;
    }
}
