using System.Text.RegularExpressions;

namespace Callu.Tests;

/// <summary>Every write that needs a rematerialize must raise the recovery flag inside the transaction that commits it.</summary>
public class RematerializeFlagGuardTests
{
    /// <summary>The components that may rematerialize without flagging: they commit no schedule change of their own.</summary>
    private static readonly string[] ExemptFiles =
        ["ScheduleMaterializer.cs", "IScheduleMaterializer.cs", "ScheduleMaterializationQuartzJob.cs"];

    /// <summary>
    /// Files that CALL the materializer, read as code — see <see cref="SourceScanner"/>. A file that
    /// only names it in a comment has not opened the crash window this guard is about.
    /// </summary>
    private static List<string> FilesThatTriggerARematerialize() =>
        SourceScanner.ProductFiles()
            .Where(f => !ExemptFiles.Contains(Path.GetFileName(f)))
            .Where(f => Regex.IsMatch(SourceScanner.Code(f), @"\bRematerializeScheduleAsync\s*\("))
            .ToList();

    /// <summary>Every path that commits a schedule change and rematerializes also raises the recovery flag.</summary>
    [Fact]
    public void EveryPathThatRematerializes_AlsoRaisesTheRecoveryFlag()
    {
        var triggers = FilesThatTriggerARematerialize();

        // Guard the premise: a scanner that found nothing would pass vacuously.
        Assert.True(triggers.Count >= 2,
            $"Expected to find the known rematerialize call sites; found {triggers.Count}.");

        var missing = triggers
            .Where(f => !SourceScanner.Code(f).Contains("NeedsRematerializeSince", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(missing.Count == 0,
            "These files commit a schedule change and then rematerialize, without raising "
            + "NeedsRematerializeSince inside the committing transaction: " + string.Join(", ", missing)
            + ". Crash between the commit and the rematerialize and the occurrence table keeps "
            + "expanding the OLD rotation, silently, until the 03:00 UTC job — and a re-save of the "
            + "same plan short-circuits to a no-op over the stale rota.");
    }

    /// <summary>The known rematerialize call sites, pinned so a new path into the occurrence table gets looked at.</summary>
    [Fact]
    public void TheKnownRematerializeCallSites_AreTheExpectedOnes()
    {
        var files = FilesThatTriggerARematerialize()
            .Select(Path.GetFileName)
            .Order()
            .ToList();

        Assert.True(
            files is ["OnCallMembershipCascade.cs", "RotationService.cs", "ScheduleService.cs"],
            "The set of paths that rematerialize a schedule changed: " + string.Join(", ", files)
            + ". Check the new one raises NeedsRematerializeSince in the same transaction as its "
            + "write, and that it goes through OnCallMembershipCascade if it is removing someone "
            + "from the rota.");
    }
}
