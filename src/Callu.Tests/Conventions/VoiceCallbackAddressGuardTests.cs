using System.Text.RegularExpressions;

namespace Callu.Tests.Conventions;

/// <summary>Every address we hand an operator for callu-api names the port the API actually listens on.</summary>
// The self-hosted voice service posts call status to an address the operator types in from the docs.
// A wrong port there is refused at the socket, so the phone rings, nobody presses anything that Callu
// can hear, and the incident shows no call at all.
public class VoiceCallbackAddressGuardTests
{
    private static DirectoryInfo RepositoryRoot() => SourceScanner.Root().Parent!;

    /// <summary>Where an operator is told the address: docs, compose, the form's hint and its placeholder.</summary>
    private static readonly string[] OperatorFacingFiles =
    [
        "docker-compose.yml",
        "src/Callu.Web/src/shared/locales/en.json",
        "src/Callu.Web/src/shared/locales/tr.json",
        "src/Callu.Web/src/features/communications/components/ProviderSection.tsx",
    ];

    private static int ApiListeningPort()
    {
        var dockerfile = File.ReadAllText(
            Path.Combine(SourceScanner.Root().FullName, "Callu.Api", "Dockerfile"));

        var match = Regex.Match(dockerfile, @"ASPNETCORE_URLS=http://\+:(\d+)");
        Assert.True(match.Success, "Callu.Api/Dockerfile no longer sets ASPNETCORE_URLS to a single http port");

        return int.Parse(match.Groups[1].Value);
    }

    [Fact]
    public void EveryAddressWeGiveAnOperator_UsesThePortTheApiListensOn()
    {
        var expected = ApiListeningPort();

        var wrong = new List<string>();
        foreach (var relative in OperatorFacingFiles)
        {
            var path = Path.Combine(RepositoryRoot().FullName, relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"{relative} has moved; this guard no longer covers it");

            wrong.AddRange(Regex.Matches(File.ReadAllText(path), @"//callu-api:(\d+)")
                .Where(m => m.Groups[1].Value != expected.ToString())
                .Select(m => $"{relative} says callu-api:{m.Groups[1].Value}, the API listens on {expected}"));
        }

        Assert.Empty(wrong);
    }
}
