using Microsoft.Extensions.DependencyInjection;
using FluentValidation;

namespace Callu.Application;

/// <summary>
/// Dependency injection extensions for Application layer
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Add application services to the DI container
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<Callu.Application.Validators.CreateEscalationRequestValidator>();
        services.AddValidatorsFromAssemblyContaining<Callu.Shared.Models.Auth.Validators.LoginRequestValidator>();

        return services;
    }
}
