using Microsoft.Extensions.DependencyInjection;

namespace Callu.Infrastructure.DI;

/// <summary>
/// Scheduling services — schedules, rotations, on-call, escalations
/// </summary>
internal static class SchedulingModule
{
    internal static IServiceCollection AddSchedulingModule(this IServiceCollection services)
    {
        services.AddScoped<Application.Services.IEscalationService, Services.EscalationService>();

        services.AddScoped<Application.Services.IScheduleService, Services.ScheduleService>();

        services.AddScoped<Application.Services.IRotationService, Services.RotationService>();

        services.AddScoped<Application.Services.IOnCallService, Services.OnCallService>();

        services.AddScoped<Application.Services.IEscalationOrchestrator, Services.EscalationOrchestrator>();

        services.AddScoped<Application.Services.IOnCallOverrideService, Services.OnCallOverrideService>();

        services.AddScoped<Application.Services.IScheduleMaterializer, Services.ScheduleMaterializer>();

        return services;
    }
}
