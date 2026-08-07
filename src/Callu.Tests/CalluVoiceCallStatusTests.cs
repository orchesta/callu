using System.Text.RegularExpressions;
using Callu.Domain.Enums;
using Callu.Infrastructure.Providers.CalluVoice;

namespace Callu.Tests;

/// <summary>
/// The callu-voice status contract, one row per status: how the leg is recorded, whether it is over,
/// and whether the retry chain stands down. A chain stands down only where a human took the page.
/// </summary>
public class CalluVoiceCallStatusTests
{
    /// <summary>Every status callu-voice can report, with what actually happened to the call.</summary>
    [Theory]
    // The call left; nobody has answered.
    [InlineData("alerting", CallStatus.Initiated, false, false)]
    // Someone picked up and answering-machine detection said it was not a machine. Nothing pressed yet.
    [InlineData("connected", CallStatus.Connected, false, false)]
    // The acknowledge key, after the confirmation was played.
    [InlineData("acknowledged", CallStatus.Acknowledged, true, true)]
    // The escalate key: a human heard it and handed it on.
    [InlineData("escalated", CallStatus.Escalated, true, true)]
    // The conference key: a human asked to be bridged in. Nothing is bridged yet.
    [InlineData("conference_requested", CallStatus.ConferenceRequested, true, true)]
    // A machine answered, so the incident detail was never played.
    [InlineData("voicemail", CallStatus.Voicemail, true, false)]
    // The channel went away before any key: never answered, or hung up mid-announcement.
    [InlineData("no_answer", CallStatus.NoAnswer, true, false)]
    // Answered, prompted, re-prompted to the limit, and still no key.
    [InlineData("silence_timeout", CallStatus.SilenceTimeout, true, false)]
    // The hard cap measured from origination ran out.
    [InlineData("timeout", CallStatus.Timeout, true, false)]
    // A live call cut by the voice service shutting down. Not a dial that failed to leave.
    [InlineData("failed", CallStatus.Failed, true, false)]
    public void EachStatusIsRecordedForWhatActuallyHappened(
        string status, CallStatus expected, bool endsTheCall, bool standsDownRetryChain)
    {
        var outcome = CalluVoiceCallStatus.Map(status);

        Assert.Equal(expected, outcome.Status);
        Assert.Equal(endsTheCall, outcome.EndsTheCall);
        Assert.Equal(standsDownRetryChain, outcome.StandsDownRetryChain);
        Assert.True(CalluVoiceCallStatus.IsKnown(status));
    }

    /// <summary>Standing a chain down is the one irreversible call here, so the set that does it is pinned.</summary>
    [Fact]
    public void OnlyAKeypressByAHumanStandsTheRetryChainDown()
    {
        var standDown = CalluVoiceCallStatus.Known
            .Where(s => CalluVoiceCallStatus.Map(s).StandsDownRetryChain)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["acknowledged", "conference_requested", "escalated"], standDown);
    }

    /// <summary>A conference was asked for, not created; recording it as created claims a bridge that does not exist.</summary>
    [Fact]
    public void AConferenceRequestIsNotAConferenceThatExists() =>
        Assert.NotEqual(CallStatus.ConferenceCreated, CalluVoiceCallStatus.Map("conference_requested").Status);

    /// <summary>
    /// Asking to be brought in owns the incident, exactly as pressing acknowledge does — the responder
    /// answered, listened and acted, and leaving it Open pages the person who just took the call.
    /// Handing it on does not: escalating is a request for somebody ELSE, so the incident stays put.
    /// </summary>
    [Fact]
    public void OnlyAKeyThatMeansIHaveThisAcknowledgesTheIncident()
    {
        var acknowledging = CalluVoiceCallStatus.Known
            .Where(s => CalluVoiceCallStatus.Map(s).AcknowledgesTheIncident)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["acknowledged", "conference_requested"], acknowledging);
    }

    /// <summary>An unrecognized status must never acknowledge on a guess.</summary>
    [Fact]
    public void AStatusFromAFutureVersionAcknowledgesNothing() =>
        Assert.False(CalluVoiceCallStatus.Unrecognized.AcknowledgesTheIncident);

    /// <summary>An unrecognized status keeps paging and reaches the timeline rather than reading as healthy.</summary>
    [Fact]
    public void AStatusFromAFutureVersion_KeepsPagingInsteadOfLookingHealthy()
    {
        var outcome = CalluVoiceCallStatus.Map("some_status_added_later");

        Assert.False(CalluVoiceCallStatus.IsKnown("some_status_added_later"));
        Assert.Equal(CalluVoiceCallStatus.Unrecognized, outcome);
        Assert.Equal(CallStatus.Failed, outcome.Status);
        Assert.True(outcome.EndsTheCall);
        Assert.False(outcome.StandsDownRetryChain);
        Assert.NotEqual(CallStatus.Connected, outcome.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AMissingStatusIsNotAHealthyOne(string? status)
    {
        Assert.False(CalluVoiceCallStatus.IsKnown(status));
        Assert.Equal(CalluVoiceCallStatus.Unrecognized, CalluVoiceCallStatus.Map(status));
    }

    [Fact]
    public void MapReadsWhatIsSent_WhateverItsCasingAndPadding()
    {
        Assert.Equal(CallStatus.Acknowledged, CalluVoiceCallStatus.Map("ACKNOWLEDGED").Status);
        Assert.Equal(CallStatus.SilenceTimeout, CalluVoiceCallStatus.Map("  silence_timeout  ").Status);
    }

    /// <summary>CallLog.Status is stored as its number, so a member may be appended but never renumbered.</summary>
    [Theory]
    [InlineData(CallStatus.Initiated, 0)]
    [InlineData(CallStatus.Connected, 1)]
    [InlineData(CallStatus.Acknowledged, 2)]
    [InlineData(CallStatus.Escalated, 3)]
    [InlineData(CallStatus.Failed, 4)]
    [InlineData(CallStatus.NoAnswer, 5)]
    [InlineData(CallStatus.Voicemail, 6)]
    [InlineData(CallStatus.Timeout, 7)]
    [InlineData(CallStatus.ConferenceCreated, 8)]
    [InlineData(CallStatus.SilenceTimeout, 9)]
    [InlineData(CallStatus.ConferenceRequested, 10)]
    public void EveryCallStatusKeepsItsNumber(CallStatus status, int expected) =>
        Assert.Equal(expected, (int)status);

    // ------------------------------------------------------------------ the contract, at its source

    /// <summary>The status declarations in callu-voice's own domain model.</summary>
    private static readonly Regex StatusConstant =
        new("""^\s*Status[A-Za-z]+\s+Status\s*=\s*"(?<name>[a-z_]+)"\s*$""",
            RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>callu-voice's types.go, from CALLU_VOICE_SOURCE or a sibling checkout; null if neither is there.</summary>
    private static string? ContractFile()
    {
        var configured = Environment.GetEnvironmentVariable("CALLU_VOICE_SOURCE");
        var repo = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(SourceScanner.Root().Parent?.Parent?.FullName ?? ".", "callu-voice")
            : configured;

        var file = Path.Combine(repo, "voice-api", "internal", "voice", "types.go");
        return File.Exists(file) ? file : null;
    }

    /// <summary>
    /// THE guard: the table above is this repo's copy of a vocabulary owned by another one. A new
    /// status there with no row here is a callback Callu would have to guess at.
    /// </summary>
    [Fact]
    public void TheTableSaysExactlyWhatCalluVoiceCanSend()
    {
        // Only a checkout that has callu-voice beside it can compare the two. A standalone one has
        // nothing to read, and failing it for that would be failing on the plumbing.
        var file = ContractFile();
        if (file is null) return;

        var declared = StatusConstant.Matches(File.ReadAllText(file))
            .Select(m => m.Groups["name"].Value)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(declared.Count >= 10,
            $"only {declared.Count} status constants were found in {file}; the declaration shape changed and "
            + "this guard stopped reading the contract it exists to read.");

        Assert.Equal(
            declared,
            CalluVoiceCallStatus.Known.Order(StringComparer.Ordinal).ToList());
    }
}
