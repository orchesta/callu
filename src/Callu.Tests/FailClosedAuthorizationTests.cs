using System.Reflection;
using Callu.Api;
using Callu.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Callu.Tests;

/// <summary>Every HTTP action carries explicit authorization intent rather than relying on the fail-closed fallback.</summary>
public class FailClosedAuthorizationTests
{
    private static readonly Assembly ApiAssembly = typeof(HealthController).Assembly;

    public static IEnumerable<object[]> ActionMethods()
    {
        foreach (var controller in ApiAssembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract))
        {
            foreach (var method in controller
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any()))
            {
                yield return new object[] { controller, method };
            }
        }
    }

    [Theory]
    [MemberData(nameof(ActionMethods))]
    public void EveryAction_HasExplicitAuthorizationIntent(Type controller, MethodInfo method)
    {
        var covered =
            controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any() ||
            method.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any() ||
            controller.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any() ||
            method.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any();

        Assert.True(covered,
            $"{controller.Name}.{method.Name} has no [Authorize]/[AllowAnonymous] and would be denied by the fail-closed FallbackPolicy. Add explicit intent.");
    }

    [Theory]
    [InlineData(typeof(HealthController))]
    [InlineData(typeof(SetupController))]
    [InlineData(typeof(WebhooksController))]
    [InlineData(typeof(VoximplantCallbackController))]
    [InlineData(typeof(CalluVoiceCallbackController))]
    public void IntentionallyPublicControllers_CarryClassAllowAnonymous(Type controller)
    {
        Assert.NotEmpty(controller.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true));
    }

    [Fact]
    public async Task AuthorizationFallbackPolicy_IsFailClosed_RequiringAuthenticatedUser()
    {
        var services = new ServiceCollection();
        services.AddApiServices();
        await using var provider = services.BuildServiceProvider();

        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();
        var fallback = await policyProvider.GetFallbackPolicyAsync();

        Assert.NotNull(fallback);
        Assert.Contains(fallback!.Requirements, r => r is DenyAnonymousAuthorizationRequirement);
    }
}
