using System.Reflection;
using Callu.Api.Controllers;
using Callu.Infrastructure.Providers.CalluVoice;
using Microsoft.AspNetCore.Mvc;

namespace Callu.Tests;

/// <summary>
/// The one address in this feature that is written down in more than one place: the route the API
/// answers callu-voice on, the path the operator is told to configure, and the URL the dial actually
/// sends. If they drift, every callback lands on a 404 that callu-voice retries a few times and then
/// forgets — the responder hears the incident acknowledged and it stays open, with nothing logged
/// anywhere an operator looks.
/// </summary>
public class CalluVoiceCallbackRouteParityTests
{
    private static string RouteTemplate()
    {
        var prefix = typeof(CalluVoiceCallbackController)
            .GetCustomAttribute<RouteAttribute>()!.Template;

        var action = typeof(CalluVoiceCallbackController)
            .GetMethod(nameof(CalluVoiceCallbackController.ReceiveCallback))!
            .GetCustomAttribute<HttpPostAttribute>()!.Template!;

        return $"/{prefix.Trim('/')}/{action.Trim('/')}";
    }

    /// <summary>The path Callu builds every callback URL from is the route the controller answers on.</summary>
    [Fact]
    public void TheAdvertisedPath_IsTheRouteTheControllerAnswersOn() =>
        Assert.Equal(CalluVoiceCallbackController.CallbackPath, RouteTemplate());

    /// <summary>
    /// Nothing between the operator's address and the route is left to be typed: whatever shape gets
    /// through the gate, the URL that goes out to callu-voice lands on the route above.
    /// </summary>
    [Fact]
    public void TheUrlSentToTheService_LandsOnThatRoute()
    {
        var built = new Uri(CalluVoiceCallbackTokenProtector.CallbackUrlFor("https://callu.example.com", "T"));

        Assert.Equal(RouteTemplate(), built.AbsolutePath);
        Assert.Null(CalluVoiceConfig.RefuseCallbackUrl("https://callu.example.com"));
    }

    /// <summary>Both halves of the product name the same path, so there is only ever one to change.</summary>
    [Fact]
    public void TheProviderSideAndTheApiSide_NameTheSamePath() =>
        Assert.Equal(CalluVoiceConfig.CallbackPath, CalluVoiceCallbackController.CallbackPath);

    /// <summary>
    /// The refusal an operator gets when the key is missing has to name the path, because nothing else
    /// does: there is no field for this provider in the settings UI to carry a hint.
    /// </summary>
    [Fact]
    public void TheRefusalTellsTheOperatorWhichPathToPointAt()
    {
        var refusal = CalluVoiceConfig.RefuseCallbackUrl(null);

        Assert.NotNull(refusal);
        Assert.Contains(CalluVoiceConfig.CallbackPath, refusal, StringComparison.Ordinal);
    }

    /// <summary>The token is a query parameter, never a route segment — the path of an /api/ request reaches disk.</summary>
    [Fact]
    public void TheRouteCarriesNoTokenSegment()
    {
        Assert.DoesNotContain("{", RouteTemplate(), StringComparison.Ordinal);

        var token = typeof(CalluVoiceCallbackController)
            .GetMethod(nameof(CalluVoiceCallbackController.ReceiveCallback))!
            .GetParameters()
            .Single(p => p.Name == "token");

        var fromQuery = token.GetCustomAttribute<FromQueryAttribute>();

        Assert.NotNull(fromQuery);
        Assert.Equal(CalluVoiceCallbackTokenProtector.TokenQueryKey, fromQuery.Name);
    }
}
