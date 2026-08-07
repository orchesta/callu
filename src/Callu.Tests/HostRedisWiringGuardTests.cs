using System.Text.RegularExpressions;

namespace Callu.Tests;

/// <summary>Exactly one component wires Redis, and every host must call both of its halves.</summary>
public class HostRedisWiringGuardTests
{
    /// <summary>Projects that boot a process. Each must go through the shared Redis setup.</summary>
    private static readonly string[] HostProjects = ["Callu.Api", "Callu.Worker"];

    private const string SharedSetupFile = "CalluRedisSetup.cs";

    /// <summary>The named projects' code — comments and literals blanked; see <see cref="SourceScanner"/>.</summary>
    private static IEnumerable<string> SourceFiles(params string[] projects) => SourceScanner.Files(projects);

    /// <summary>
    /// Registration half: the multiplexer and the Data Protection key ring. A host that skips it
    /// either has no key ring at all or builds a second, unguarded connection.
    /// </summary>
    [Fact]
    public void EveryHost_RegistersRedis_ThroughTheSharedSetup()
    {
        var missing = HostProjects
            .Where(project => !SourceFiles(project).Any(f =>
                SourceScanner.Code(f).Contains("AddCalluRedis", StringComparison.Ordinal)))
            .ToList();

        Assert.True(missing.Count == 0,
            "These hosts do not register Redis through CalluRedisSetup.AddCalluRedis: "
            + string.Join(", ", missing)
            + ". Without it the host has no guarded multiplexer and no Data Protection key ring — on "
            + "the Worker that means stored SMTP and SIP passwords never decrypt and it pages nobody.");
    }

    /// <summary>The startup-guard half runs after Build() to reach the operator's log sinks, so it is a second call site.</summary>
    [Fact]
    public void EveryHost_RunsTheRedisStartupGuard()
    {
        var missing = HostProjects
            .Where(project => !SourceFiles(project).Any(f =>
                SourceScanner.Code(f).Contains("UseCalluRedisGuard", StringComparison.Ordinal)))
            .ToList();

        Assert.True(missing.Count == 0,
            "These hosts never run the Redis startup guard (CalluRedisSetup.UseCalluRedisGuard): "
            + string.Join(", ", missing)
            + ". A misconfigured Redis then costs them the Data Protection key ring in total silence.");
    }

    /// <summary>A second ConnectionMultiplexer.Connect would be a Redis connection answering to none of the guards.</summary>
    [Fact]
    public void OnlyTheSharedSetup_BuildsARedisConnection()
    {
        var offenders = SourceScanner.ProductFiles()
            .Where(f => Path.GetFileName(f) != SharedSetupFile)
            .Where(f => Regex.IsMatch(
                SourceScanner.Code(f),
                @"ConnectionMultiplexer\s*\.\s*Connect|ConfigurationOptions\s*\.\s*Parse"))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These files build their own Redis connection instead of going through CalluRedisSetup: "
            + string.Join(", ", offenders)
            + ". Every guard (fatal parse error, the passwordless warning, the multi-endpoint "
            + "warning) lives in the shared setup, and a hand-rolled connection has none of them.");
    }

    /// <summary>
    /// The guard above is only worth having if it can go red. This is the Worker's actual pre-fix
    /// wiring: it must be detected.
    /// </summary>
    [Fact]
    public void TheGuard_Detects_AHandRolledMultiplexer()
    {
        const string handRolled = """
            var dpRedisOptions = StackExchange.Redis.ConfigurationOptions.Parse(workerDpRedis.Trim());
            dpRedisOptions.AbortOnConnectFail = false;
            var dpRedisMux = StackExchange.Redis.ConnectionMultiplexer.Connect(dpRedisOptions);
            """;

        Assert.Matches(@"ConnectionMultiplexer\s*\.\s*Connect|ConfigurationOptions\s*\.\s*Parse", handRolled);
    }
}
