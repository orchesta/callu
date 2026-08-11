using System.Text.RegularExpressions;

namespace Callu.Tests.Conventions;

/// <summary>Each catch listed here swallows a failure on purpose, and each one has to say so.</summary>
// Swallowing is deliberate: the alternative is crashing a path that pages people. The log line is the
// only symptom left, and deleting one breaks no build.
public class SilentCatchGuardTests
{
    /// <summary>File, method, and what an operator loses if the line goes away.</summary>
    public record Swallow(string File, string Method, string Cost);

    public static readonly Swallow[] Inventory =
    [
        new("AlertRuleService.cs", "MapToDto",
            "an alert rule is listed with no conditions and no actions, and nothing says why"),
        new("AlertRuleEngine.cs", "MatchesConditions",
            "a rule with unreadable conditions silently matches nothing and never fires"),
        new("AlertRuleEngine.cs", "ExecuteActions",
            "a rule matches but none of its actions run"),
        new("AlertRuleEngine.cs", "ShouldSuppressPagingAsync",
            "a rule cannot be read as a suppression, so the incident pages when it maybe should not have"),
        new("WebhookPayloadParser.cs", "MapSeverity",
            "a custom severity mapping is skipped and the alert arrives at the built-in severity"),
        new("WebhookPayloadParser.cs", "DetermineState",
            "every delivery is read as open, so a resolved alert never closes its incident"),
        new("HealthCheckExecutor.cs", "AddCustomHeaders",
            "a probe goes out without its auth header and the target answers as if it were down"),
        new("TtsTemplateService.cs", "MapToDto",
            "a spoken template is listed empty, so a call in that language falls back to built-in wording"),
        new("MaintenanceWindowService.cs", "ParseAffectedServiceIds",
            "a maintenance window covers no service and suppresses nothing"),
        new("NotificationChannelService.cs", "DeserializeConfig",
            "a channel is treated as unconfigured and delivers nothing"),
        new("NotificationChannelService.cs", "DeserializeServiceFilter",
            "a channel is read as unfiltered and matches every service"),
    ];

    public static TheoryData<Swallow> Cases()
    {
        var data = new TheoryData<Swallow>();
        foreach (var swallow in Inventory) data.Add(swallow);
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void TheSwallowedFailureIsStillWrittenDown(Swallow swallow)
    {
        var bodies = SourceMembers.MethodBodies(SourceMembers.Read(swallow.File));

        Assert.True(
            bodies.TryGetValue(swallow.Method, out var body),
            $"{swallow.File} no longer has a method named {swallow.Method}; this guard stopped covering it. "
            + "Either restore the name or move the entry to whatever replaced it.");

        Assert.True(
            Regex.IsMatch(body!, @"\bcatch\b"),
            $"{swallow.File}.{swallow.Method} no longer catches anything. If the swallow is genuinely gone, "
            + "drop this entry — but if it moved, the log line has to move with it.");

        Assert.True(
            Regex.IsMatch(body!, @"logger\s*\.\s*Log(Warning|Error|Critical|Information)"),
            $"{swallow.File}.{swallow.Method} swallows a failure without writing anything down. "
            + $"Cost of the missing line: {swallow.Cost}.");
    }

    /// <summary>TtsDefaults hands its load failures back, and both hosts write them down.</summary>
    [Fact]
    public void ALanguageThatFailsToLoad_IsHandedBackToTheHostThatLogsIt()
    {
        var loader = SourceMembers.Read("TtsDefaults.cs");

        Assert.True(
            Regex.IsMatch(loader, @"IReadOnlyList<TtsDefaultsLoadFailure>\s+Initialize"),
            "TtsDefaults.Initialize no longer returns its load failures, so a language that fails to load "
            + "is invisible again — a call in that language would quietly speak the built-in wording.");

        foreach (var host in new[] { "Callu.Api", "Callu.Worker" })
        {
            var program = File.ReadAllText(Path.Combine(SourceScanner.Root().FullName, host, "Program.cs"));

            Assert.True(
                Regex.IsMatch(program, @"TtsDefaults\.Initialize", RegexOptions.None)
                && Regex.IsMatch(program, @"foreach[^\r\n]*TtsDefaults\.Initialize"),
                $"{host}/Program.cs calls TtsDefaults.Initialize without reading what failed to load.");

            Assert.True(
                Regex.IsMatch(program, @"Log(Error|Warning|Critical)[^;]*LanguageCode", RegexOptions.Singleline),
                $"{host}/Program.cs no longer logs the languages that failed to load.");
        }
    }
}
