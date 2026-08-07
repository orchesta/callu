using Callu.Infrastructure.Providers.CalluVoice;
using Microsoft.AspNetCore.DataProtection;

namespace Callu.Tests;

/// <summary>
/// The only thing that authenticates a callu-voice callback: the service sends no bearer, no
/// signature and no timestamp, so a call comes out of a token this installation sealed or nowhere.
/// </summary>
public class CalluVoiceCallbackTokenTests
{
    private const string Phone = "+905321234567";

    private static CalluVoiceCallbackTokenProtector New(IDataProtectionProvider? provider = null) =>
        new(provider ?? new EphemeralDataProtectionProvider());

    [Fact]
    public void AnIssuedToken_ResolvesToTheCallItWasMintedFor()
    {
        var protector = New();
        var incidentId = Guid.NewGuid();

        var token = protector.Issue(incidentId, "call-1", Phone);

        Assert.NotNull(token);
        Assert.True(protector.TryResolve(token, out var ticket));
        Assert.Equal(incidentId, ticket.IncidentId);
        Assert.Equal("call-1", ticket.CallId);
        Assert.Equal(Phone, ticket.PhoneNumber);
    }

    /// <summary>The token travels in a URL that ends up in logs at both ends, so it may not read as the incident.</summary>
    [Fact]
    public void AnIssuedToken_DoesNotLeakTheIncidentOrTheNumberItWasDialledFor()
    {
        var incidentId = Guid.NewGuid();
        var token = New().Issue(incidentId, "call-1", Phone)!;

        Assert.DoesNotContain(incidentId.ToString("N"), token, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("905321234567", token, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoCallsOnOneIncident_GetTokensThatResolveToDifferentCalls()
    {
        var protector = New();
        var incidentId = Guid.NewGuid();

        Assert.True(protector.TryResolve(protector.Issue(incidentId, "call-1", Phone), out var first));
        Assert.True(protector.TryResolve(protector.Issue(incidentId, "call-2", Phone), out var second));

        Assert.Equal("call-1", first.CallId);
        Assert.Equal("call-2", second.CallId);
    }

    /// <summary>A call with nothing to bind to gets no token, so nothing can be acknowledged with it.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoCallId_MeansNoToken(string? callId) =>
        Assert.Null(New().Issue(Guid.NewGuid(), callId, Phone));

    [Fact]
    public void NoIncident_MeansNoToken() =>
        Assert.Null(New().Issue(Guid.Empty, "call-1", Phone));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-token")]
    [InlineData("00000000000000000000000000000000")]
    public void NothingThisInstallationSealed_Resolves(string? token) =>
        Assert.False(New().TryResolve(token, out _));

    [Fact]
    public void ATamperedToken_DoesNotResolve()
    {
        var protector = New();
        var token = protector.Issue(Guid.NewGuid(), "call-1", Phone)!;

        var last = token[^1];
        var tampered = token[..^1] + (last == 'A' ? 'B' : 'A');

        Assert.False(protector.TryResolve(tampered, out _));
    }

    /// <summary>Another installation's token must not acknowledge an incident here.</summary>
    [Fact]
    public void ATokenFromAnotherKeyring_DoesNotResolve()
    {
        var issued = New().Issue(Guid.NewGuid(), "call-1", Phone);

        Assert.False(New().TryResolve(issued, out _));
    }

    [Fact]
    public void AnExpiredToken_DoesNotResolve()
    {
        var provider = new EphemeralDataProtectionProvider();
        var expired = provider
            .CreateProtector(CalluVoiceCallbackTokenProtector.Purpose)
            .ToTimeLimitedDataProtector()
            .Protect($"{Guid.NewGuid():N}|call-1|{Phone}", DateTimeOffset.UtcNow.AddSeconds(-1));

        Assert.False(New(provider).TryResolve(expired, out _));
    }

    /// <summary>The service caps a call at two minutes by default and retries its callback for about half a minute.</summary>
    [Fact]
    public void TheLifetimeOutlastsACallAndItsCallbackRetries() =>
        Assert.True(CalluVoiceCallbackTokenProtector.Lifetime >= TimeSpan.FromMinutes(5));

    [Fact]
    public void ThePhoneNumberSurvivesEvenIfItCarriesTheSeparator()
    {
        var protector = New();

        Assert.True(protector.TryResolve(protector.Issue(Guid.NewGuid(), "call-1", "+90 532|123"), out var ticket));
        Assert.Equal("+90 532|123", ticket.PhoneNumber);
    }

    /// <summary>Callu owns the path: the operator supplies the address and the token rides in the query.</summary>
    [Theory]
    [InlineData("https://callu.example.com")]
    [InlineData("https://callu.example.com/")]
    [InlineData("  http://callu:8080  ")]
    [InlineData("https://callu.example.com/api/callu-voice/callback")]
    public void TheCallbackUrlIsTheRouteTheApiServes_WithTheTokenInItsQuery(string configured)
    {
        var built = new Uri(CalluVoiceCallbackTokenProtector.CallbackUrlFor(configured, "T"));

        Assert.Equal(CalluVoiceConfig.CallbackPath, built.AbsolutePath);
        Assert.Equal($"?{CalluVoiceCallbackTokenProtector.TokenQueryKey}=T", built.Query);
    }

    /// <summary>
    /// The path of every request through /api/ is written to the bundled nginx access log and the query
    /// is not, so a bearer credential in the path is a bearer credential on disk for a month.
    /// </summary>
    [Fact]
    public void TheTokenIsNeverInThePath()
    {
        var token = New().Issue(Guid.NewGuid(), "call-1", Phone)!;
        var built = new Uri(CalluVoiceCallbackTokenProtector.CallbackUrlFor("https://callu.example.com", token));

        Assert.DoesNotContain(token, built.AbsolutePath, StringComparison.Ordinal);
        Assert.Equal(token, System.Web.HttpUtility.ParseQueryString(built.Query)
            [CalluVoiceCallbackTokenProtector.TokenQueryKey]);
    }

    /// <summary>A call with nothing to authenticate it still goes to the route, so it is refused there rather than answered elsewhere.</summary>
    [Fact]
    public void NoTokenStillMeansTheRouteTheApiServes() =>
        Assert.Equal(
            "https://callu.example.com" + CalluVoiceConfig.CallbackPath,
            CalluVoiceCallbackTokenProtector.CallbackUrlFor("https://callu.example.com", token: null));
}
