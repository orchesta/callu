using Callu.Application.Common.Interfaces;
using Callu.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Callu.Infrastructure.DI;

/// <summary>
/// Core business services — identity, profile, settings, teams, catalogs, reporting, etc.
/// </summary>
internal static class CoreServicesModule
{
    internal static IServiceCollection AddCoreServicesModule(this IServiceCollection services)
    {
        services.AddSingleton<IJwtTokenService, JwtTokenService>();

        services.AddScoped<IAuthService, Services.AuthService>();

        services.AddScoped<Application.Services.IProfileService, Services.ProfileService>();

        services.AddScoped<Application.Services.IUserManagementService, Services.UserManagementService>();

        services.AddSingleton<Application.Services.ILocalizationService, Services.LocalizationService>();

        services.AddScoped<Application.Services.ITimeZoneService, Services.TimeZoneService>();

        services.AddSingleton<Application.Common.Interfaces.IAccessTokenRevocationStore, Services.AccessTokenRevocationStore>();

        services.AddScoped<Application.Services.ITeamService, Services.TeamService>();

        services.AddScoped<Application.Services.IServiceManagementService, Services.ServiceManagementService>();

        services.AddScoped<Application.Services.IServiceCatalogService, Services.ServiceCatalogService>();

        services.AddScoped<Application.Services.ICallLogService, Services.CallLogService>();

        services.AddScoped<Application.Services.IReportingService, Services.ReportingService>();
        services.AddScoped<Application.Services.IUptimeCalculator, Services.UptimeCalculator>();
        services.AddScoped<Application.Services.IServiceStatusCascadeEngine, Services.ServiceStatusCascadeEngine>();

        services.AddScoped<Application.Services.IPostmortemService, Services.PostmortemService>();

        services.AddScoped<Application.Services.IRunbookService, Services.RunbookService>();

        services.AddScoped<Application.Services.IMaintenanceWindowService, Services.MaintenanceWindowService>();

        services.AddScoped<Application.Services.IAuditLogService, Services.AuditLogService>();

        services.AddScoped<Application.Services.IOrganizationSettingsService, Services.OrganizationSettingsService>();

        return services;
    }
}
