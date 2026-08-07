using System.Text.RegularExpressions;
using Callu.Domain.Entities;
using Callu.Domain.Enums;

namespace Callu.Tests;

/// <summary>Standing a voice retry chain down means standing all of it down: the deadline and the stuck-dial marker go together.</summary>
public class VoiceStandDownGuardTests
{
    /// <summary>Assignments that END a chain: `x.NextRetryAt = null` and its set-based ExecuteUpdate form.</summary>
    private static readonly Regex StandsAChainDown =
        new(@"\.NextRetryAt\s*=\s*null|NextRetryAt\s*,\s*\(DateTime\?\)\s*null", RegexOptions.Compiled);

    /// <summary>Files that end a CallLog retry chain by hand, read as code and scoped to CallLog on purpose.</summary>
    private static List<string> FilesThatStandAVoiceChainDownByHand() =>
        SourceScanner.ProductFiles(includeMigrations: false)
            .Where(f =>
            {
                var code = SourceScanner.Code(f);
                return code.Contains("CallLog", StringComparison.Ordinal) && StandsAChainDown.IsMatch(code);
            })
            .ToList();

    /// <summary>
    /// THE guard. Clear a voice retry's deadline by hand and you must clear its stuck marker in the
    /// same breath — or call <c>StandDownRetryChain()</c>, which is the same thing with one name.
    /// </summary>
    [Fact]
    public void EveryPathThatEndsAVoiceRetryChain_AlsoClearsTheStuckMarker()
    {
        var offenders = FilesThatStandAVoiceChainDownByHand()
            .Where(f => !SourceScanner.Code(f).Contains("DialOutFailingSince", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These files end a voice retry chain without clearing DialOutFailingSince: "
            + string.Join(", ", offenders)
            + ". A stale marker charges the NEXT stuck period for time it never spent: the chain gives up "
            + "on its first failed dial, having called nobody, and tells the timeline a provider had been "
            + "refusing for an hour. Use CallLog.StandDownRetryChain().");
    }

    /// <summary>The one allowed hand-written stand-down is the acknowledgement's set-based UPDATE, which loads no entities.</summary>
    [Fact]
    public void TheOnlyHandWrittenStandDown_IsTheAcknowledgementUpdate()
    {
        var files = FilesThatStandAVoiceChainDownByHand()
            .Select(Path.GetFileName)
            .Order()
            .ToList();

        Assert.True(
            files is ["IncidentService.cs"],
            "The set of paths that end a voice retry chain by hand changed: " + string.Join(", ", files)
            + ". Tracked-entity paths must call CallLog.StandDownRetryChain() instead of clearing "
            + "NextRetryAt themselves.");
    }

    /// <summary>The entity method itself: it must take the whole chain down, not just the deadline.</summary>
    [Fact]
    public void StandDownRetryChain_ClearsTheDeadlineAndTheMarkerAndTheKind()
    {
        var callLog = new CallLog
        {
            PhoneNumber = "+905550001122",
            Status = CallStatus.NoAnswer,
            FailureReason = "no answer",
            AttemptNumber = 2,
            NextRetryAt = DateTime.UtcNow.AddMinutes(5),
            DialOutFailingSince = DateTime.UtcNow.AddHours(-1),
            DialOutFailureKind = VoiceDialOutFailureKind.ProviderRefused
        };

        callLog.StandDownRetryChain();

        Assert.Null(callLog.NextRetryAt);
        Assert.Null(callLog.DialOutFailingSince);
        Assert.Null(callLog.DialOutFailureKind);

        // Call history is not the chain's to rewrite: this row still describes the call it always did.
        Assert.Equal(CallStatus.NoAnswer, callLog.Status);
        Assert.Equal(2, callLog.AttemptNumber);
        Assert.Equal("no answer", callLog.FailureReason);
    }
}
