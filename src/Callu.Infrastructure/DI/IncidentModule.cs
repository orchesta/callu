using Callu.Application.Plugins;
using Callu.Infrastructure.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Callu.Infrastructure.DI;

/// <summary>
/// Incident management services — CRUD, queries, notes, event dispatching
/// </summary>
internal static class IncidentModule
{
    internal static IServiceCollection AddIncidentModule(this IServiceCollection services)
    {
        services.AddScoped<Application.Services.IIncidentService, Services.IncidentService>();

        services.AddScoped<Application.Services.IIncidentNoteService, Services.IncidentNoteService>();

        services.AddScoped<Application.Services.IIncidentQueryService, Services.IncidentQueryService>();

        services.AddScoped<IIncidentEventDispatcher, IncidentEventDispatcher>();

        return services;
    }
}
