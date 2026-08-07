using System.Text.RegularExpressions;

namespace Callu.Tests.Conventions;

/// <summary>The panel plays rendered speech from a blob URL, so the CSP has to allow blob media.</summary>
// The endpoint needs a bearer token, which an <audio src> cannot send, so the audio is fetched and
// turned into a blob. Without media-src the browser falls back to default-src and blocks it silently:
// the player renders, shows 0:00, and nothing in the server logs says anything is wrong.
public class CspAllowsRenderedAudioGuardTests
{
    [Fact]
    public void EveryContentSecurityPolicyAllowsBlobMedia()
    {
        var path = Path.Combine(
            SourceScanner.Root().FullName, "Callu.Web", "nginx.conf");
        Assert.True(File.Exists(path), "nginx.conf has moved; this guard no longer covers it");

        var policies = Regex.Matches(File.ReadAllText(path), @"Content-Security-Policy\s+""([^""]+)""")
            .Select(m => m.Groups[1].Value)
            .ToArray();

        Assert.NotEmpty(policies);

        var offenders = policies
            .Where(p => !Regex.IsMatch(p, @"media-src[^;]*\bblob:"))
            .Select(p => $"a policy has no blob: in media-src, so rendered audio will not play: {p[..Math.Min(80, p.Length)]}…")
            .ToArray();

        Assert.Empty(offenders);
    }
}
