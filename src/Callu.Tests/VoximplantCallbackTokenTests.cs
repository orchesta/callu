using Callu.Api.Controllers;
using Microsoft.AspNetCore.DataProtection;

namespace Callu.Tests;

/// <summary>An incident comes out only of a callback token this host sealed, and nothing else resolves.</summary>
public class VoximplantCallbackTokenTests
{
    private static VoxCallbackTokenProtector New(IDataProtectionProvider? provider = null) =>
        new(provider ?? new EphemeralDataProtectionProvider());

    [Fact]
    public void Issued_Token_Resolves_To_Its_Incident()
    {
        var protector = New();
        var incidentId = Guid.NewGuid().ToString();

        var token = protector.Issue(incidentId);

        Assert.NotNull(token);
        Assert.True(protector.TryResolveIncident(token, out var resolved));
        Assert.Equal(incidentId, resolved);
    }

    /// <summary>The token is opaque: the incident it carries must not be readable/greppable.</summary>
    [Fact]
    public void Issued_Token_Does_Not_Leak_The_Incident_Id()
    {
        var incidentId = Guid.NewGuid().ToString();
        var token = New().Issue(incidentId);

        Assert.DoesNotContain(incidentId, token);
    }

    [Fact]
    public void Two_Incidents_Get_Distinguishable_Tokens()
    {
        var protector = New();
        var a = Guid.NewGuid().ToString();
        var b = Guid.NewGuid().ToString();

        Assert.True(protector.TryResolveIncident(protector.Issue(a), out var resolvedA));
        Assert.True(protector.TryResolveIncident(protector.Issue(b), out var resolvedB));

        Assert.Equal(a, resolvedA);
        Assert.Equal(b, resolvedB);
        Assert.NotEqual(resolvedA, resolvedB);
    }

    /// <summary>A test call carries no incident, so there is nothing to bind and no token.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Issue_Returns_Null_When_There_Is_No_Incident(string? incidentId) =>
        Assert.Null(New().Issue(incidentId));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_Token_Does_Not_Resolve(string? token)
    {
        Assert.False(New().TryResolveIncident(token, out var incidentId));
        Assert.Equal(string.Empty, incidentId);
    }

    /// <summary>An attacker who knows the scenario key still cannot mint a token.</summary>
    [Theory]
    [InlineData("not-a-token")]
    [InlineData("00000000000000000000000000000000")]
    public void Garbage_Token_Does_Not_Resolve(string token) =>
        Assert.False(New().TryResolveIncident(token, out _));

    [Fact]
    public void Tampered_Token_Does_Not_Resolve()
    {
        var protector = New();
        var token = protector.Issue(Guid.NewGuid().ToString())!;

        // Flip the last character — DataProtection authenticates the payload, so this must fail
        // closed rather than decrypt to some other incident.
        var last = token[^1];
        var tampered = token[..^1] + (last == 'A' ? 'B' : 'A');

        Assert.False(protector.TryResolveIncident(tampered, out _));
    }

    [Fact]
    public void Token_From_Another_Keyring_Does_Not_Resolve()
    {
        var issued = New().Issue(Guid.NewGuid().ToString());

        Assert.False(New().TryResolveIncident(issued, out _));
    }

    /// <summary>
    /// The token outlives the call (ring + the scenario's 2-minute cap) but not much more, so a
    /// captured token cannot be used to acknowledge that incident indefinitely.
    /// </summary>
    [Fact]
    public void Expired_Token_Does_Not_Resolve()
    {
        var provider = new EphemeralDataProtectionProvider();
        var expired = provider
            .CreateProtector(VoxCallbackTokenProtector.Purpose)
            .ToTimeLimitedDataProtector()
            .Protect(Guid.NewGuid().ToString(), DateTimeOffset.UtcNow.AddSeconds(-1));

        Assert.False(New(provider).TryResolveIncident(expired, out _));
    }

    [Fact]
    public void Lifetime_Covers_The_Scenarios_Maximum_Call()
    {
        // The scenario hangs up at 120s of talk time; the rest is ring + slack.
        Assert.True(VoxCallbackTokenProtector.Lifetime >= TimeSpan.FromMinutes(5));
    }
}
