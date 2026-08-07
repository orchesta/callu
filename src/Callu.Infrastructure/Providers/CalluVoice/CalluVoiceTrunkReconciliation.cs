namespace Callu.Infrastructure.Providers.CalluVoice;

/// <summary>What one comparison of Callu's carrier against the voice service's found.</summary>
public sealed record CalluVoiceTrunkReconciliation
{
    public required CalluVoiceTrunkOutcome Outcome { get; init; }
    public string? Error { get; init; }

    /// <summary>The voice service is holding the carrier Callu has. Nothing happened and nothing is written.</summary>
    public static CalluVoiceTrunkReconciliation InAgreement() =>
        new() { Outcome = CalluVoiceTrunkOutcome.InAgreement };

    /// <summary>It was holding something else — or nothing — and Callu put the carrier back.</summary>
    public static CalluVoiceTrunkReconciliation Restored() =>
        new() { Outcome = CalluVoiceTrunkOutcome.Restored };

    /// <summary>Whether the carrier is there could not be established, so nothing was sent.</summary>
    public static CalluVoiceTrunkReconciliation CouldNotRead(string? error) =>
        new() { Outcome = CalluVoiceTrunkOutcome.CouldNotRead, Error = error };

    /// <summary>The carrier was known to be wrong and could not be put back.</summary>
    public static CalluVoiceTrunkReconciliation CouldNotRestore(string? error) =>
        new() { Outcome = CalluVoiceTrunkOutcome.CouldNotRestore, Error = error };
}

public enum CalluVoiceTrunkOutcome
{
    InAgreement,
    Restored,
    CouldNotRead,
    CouldNotRestore,
}
