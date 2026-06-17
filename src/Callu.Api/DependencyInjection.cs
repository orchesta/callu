namespace Callu.Api;

using FluentValidation;
using Callu.Api.Services;
using Callu.Application.Common.Interfaces;

/// <summary>
/// API layer dependency injection extensions
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Add API layer services (authorization policies, current user context)
    /// </summary>
    public static IServiceCollection AddApiServices(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, HttpContextCurrentUserService>();

        services.AddValidatorsFromAssemblyContaining<Callu.Api.Validators.InitialSetupRequestValidator>();

        services.AddAuthorizationCore(options =>
        {
            options.AddPolicy(Policies.CanManageUsers, policy => policy.RequireClaim("CanManageUsers", "true"));
            options.AddPolicy(Policies.CanManageTeams, policy => policy.RequireClaim("CanManageTeams", "true"));
            options.AddPolicy(Policies.CanManageSettings, policy => policy.RequireClaim("CanManageSettings", "true"));
            options.AddPolicy(Policies.CanManageIntegrations, policy => policy.RequireClaim("CanManageIntegrations", "true"));
            options.AddPolicy(Policies.CanManageBilling, policy => policy.RequireClaim("CanManageBilling", "true"));
            options.AddPolicy(Policies.CanManageIncidents, policy => policy.RequireClaim("CanManageIncidents", "true"));
            options.AddPolicy(Policies.CanAcknowledgeIncidents, policy => policy.RequireClaim("CanAcknowledgeIncidents", "true"));
            options.AddPolicy(Policies.CanResolveIncidents, policy => policy.RequireClaim("CanResolveIncidents", "true"));
            options.AddPolicy(Policies.CanManageServices, policy => policy.RequireClaim("CanManageServices", "true"));
            options.AddPolicy(Policies.CanManageSchedules, policy => policy.RequireClaim("CanManageSchedules", "true"));
            options.AddPolicy(Policies.CanManageEscalations, policy => policy.RequireClaim("CanManageEscalations", "true"));
            options.AddPolicy(Policies.CanManageWebhooks, policy => policy.RequireClaim("CanManageWebhooks", "true"));
            options.AddPolicy(Policies.CanViewReports, policy => policy.RequireClaim("CanViewReports", "true"));
            options.AddPolicy(Policies.CanViewAuditLog, policy => policy.RequireClaim("CanViewAuditLog", "true"));
            options.AddPolicy(Policies.CanViewServices, policy => policy.RequireClaim("CanViewServices", "true"));
            options.AddPolicy(Policies.CanViewEscalations, policy => policy.RequireClaim("CanViewEscalations", "true"));
            options.AddPolicy(Policies.CanViewTeams, policy => policy.RequireClaim("CanViewTeams", "true"));
            options.AddPolicy(Policies.CanViewSchedules, policy => policy.RequireClaim("CanViewSchedules", "true"));
            options.AddPolicy(Policies.CanViewIncidents, policy => policy.RequireClaim("CanViewIncidents", "true"));
            options.AddPolicy(Policies.CanViewCallLogs, policy => policy.RequireClaim("CanViewCallLogs", "true"));
            options.AddPolicy(Policies.CanViewRunbooks, policy => policy.RequireClaim("CanViewRunbooks", "true"));
            options.AddPolicy(Policies.CanManageRunbooks, policy => policy.RequireClaim("CanManageRunbooks", "true"));
            options.AddPolicy(Policies.CanViewPostmortems, policy => policy.RequireClaim("CanViewPostmortems", "true"));
            options.AddPolicy(Policies.CanManagePostmortems, policy => policy.RequireClaim("CanManagePostmortems", "true"));
        });

        return services;
    }
}

/// <summary>
/// Authorization policy names
/// </summary>
public static class Policies
{
    public const string CanManageUsers = nameof(CanManageUsers);
    public const string CanManageTeams = nameof(CanManageTeams);
    public const string CanManageSettings = nameof(CanManageSettings);
    public const string CanManageIntegrations = nameof(CanManageIntegrations);
    public const string CanManageBilling = nameof(CanManageBilling);
    public const string CanManageIncidents = nameof(CanManageIncidents);
    public const string CanAcknowledgeIncidents = nameof(CanAcknowledgeIncidents);
    public const string CanResolveIncidents = nameof(CanResolveIncidents);
    public const string CanManageServices = nameof(CanManageServices);
    public const string CanManageSchedules = nameof(CanManageSchedules);
    public const string CanManageEscalations = nameof(CanManageEscalations);
    public const string CanManageWebhooks = nameof(CanManageWebhooks);
    public const string CanViewReports = nameof(CanViewReports);
    public const string CanViewAuditLog = nameof(CanViewAuditLog);
    public const string CanViewServices = nameof(CanViewServices);
    public const string CanViewEscalations = nameof(CanViewEscalations);
    public const string CanViewTeams = nameof(CanViewTeams);
    public const string CanViewSchedules = nameof(CanViewSchedules);
    public const string CanViewIncidents = nameof(CanViewIncidents);
    public const string CanViewCallLogs = nameof(CanViewCallLogs);
    public const string CanViewRunbooks = nameof(CanViewRunbooks);
    public const string CanManageRunbooks = nameof(CanManageRunbooks);
    public const string CanViewPostmortems = nameof(CanViewPostmortems);
    public const string CanManagePostmortems = nameof(CanManagePostmortems);
}
