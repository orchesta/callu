using Callu.Domain.Enums;

namespace Callu.Shared.Models.Notifications;

/// <summary>One channel of one user's page that could not be written to the notification store.</summary>
public readonly record struct DispatchChannelFailure(string UserId, NotificationType Channel);

/// <summary>One channel of one user's page that the store accepted and that then sent nothing.</summary>
public readonly record struct DispatchChannelSilence(
    string UserId,
    NotificationType Channel,
    string? Reason,
    bool IsPermanent = true);

/// <summary>One channel of one user's page that is queued but not yet sent.</summary>
public readonly record struct DispatchChannelDeferral(
    string UserId,
    NotificationType Channel,
    string? Reason,
    DateTime NextAttemptAt);

/// <summary>An on-call responder with no channel capable of paging them; <c>Reason</c> names what is missing.</summary>
public readonly record struct DispatchUnpageableTarget(string UserId, string Reason);

/// <summary>A channel the deployment could have used, left untried because the user turned it off.</summary>
// Never attempted, so it produces no failure and no silence — without its own record the operator
// who just configured a provider sees no trace of it at all.
public readonly record struct DispatchChannelOptedOut(string UserId, NotificationType Channel);

/// <summary>What a page actually achieved, keeping the zero-reached outcomes apart from each other.</summary>
public readonly record struct NotificationDispatchResult(
    int Reached,
    int Failed,
    IReadOnlyList<DispatchChannelFailure>? ChannelFailures = null,
    int Silent = 0,
    IReadOnlyList<DispatchChannelSilence>? ChannelSilences = null,
    int Unpageable = 0,
    IReadOnlyList<DispatchUnpageableTarget>? UnpageableTargets = null,
    IReadOnlyList<DispatchChannelDeferral>? Deferrals = null,
    IReadOnlyList<DispatchChannelOptedOut>? OptedOutChannels = null)
{
    /// <summary>Nothing to page and nothing went wrong.</summary>
    public static readonly NotificationDispatchResult Nobody = new(0, 0);

    /// <summary>Users for whom NOTHING was queued because every channel's claim failed.</summary>
    public bool DispatchFailed => Reached == 0 && (Failed > 0 || FailedChannelCount > 0);

    /// <summary>Somebody had a paging channel and every one of those channels sent nothing.</summary>
    public bool ChannelsSilent => NothingWasQueued && Silent > 0;

    /// <summary>
    /// Somebody was on call and nothing could have paged them — no phone number, push only, every
    /// paging channel off. The rota is fine and the providers are fine; the responder's profile is not.
    /// </summary>
    public bool TargetsUnpageable => NothingWasQueued && Unpageable > 0;

    /// <summary>Nobody was there to page at all — no claim failed and no channel was even asked.</summary>
    public bool NobodyToPage => NothingWasQueued && Silent == 0 && Unpageable == 0;

    /// <summary>Nothing is in flight, and it is not because the store broke.</summary>
    private bool NothingWasQueued => Reached == 0 && Failed == 0 && FailedChannelCount == 0;

    /// <summary>Channel claims lost across all users, including users reached on another channel.</summary>
    public int FailedChannelCount => ChannelFailures?.Count ?? 0;

    /// <summary>Channels that sent nothing across all users, including users reached on another channel.</summary>
    public int SilentChannelCount => ChannelSilences?.Count ?? 0;

    /// <summary>Pages that are queued but not yet sent, across all users.</summary>
    public int DeferredChannelCount => Deferrals?.Count ?? 0;

    /// <summary>Whether every silent channel is silent for good, with no page still coming.</summary>
    public bool AllSilencesPermanent =>
        ChannelSilences is null || ChannelSilences.All(s => s.IsPermanent);

    /// <summary>
    /// Someone was paged, but at least one channel was not. The step still advances (a page did go
    /// out), so this is the only chance the operator gets to learn that a target was dropped.
    /// </summary>
    public bool PartiallyFailed => Reached > 0 && FailedChannelCount > 0;

    /// <summary>Someone was paged, but at least one channel sent nothing.</summary>
    public bool PartiallySilent => Reached > 0 && SilentChannelCount > 0;

    /// <summary>
    /// Someone was paged, and somebody ELSE on the same step could not be paged by anything. Same
    /// shape as <see cref="PartiallySilent"/>, different remedy: fix the profile, not the provider.
    /// </summary>
    public bool PartiallyUnpageable => Reached > 0 && Unpageable > 0;

    /// <summary>At least one page is queued and has not been sent yet.</summary>
    public bool HasDeferredPages => DeferredChannelCount > 0;

    /// <summary>Compact description of the lost channels for the timeline / audit record.</summary>
    public string DescribeChannelFailures() =>
        ChannelFailures is not { Count: > 0 } failures
            ? "none"
            : string.Join(", ", failures.Select(f => $"{f.Channel}→{f.UserId}"));

    /// <summary>
    /// Compact description of the silent channels, carrying each row's OWN reason — this is what
    /// turns "nobody was paged" into something an operator can act on at 3am.
    /// </summary>
    /// <summary>Channels that were available but switched off in the responder's preferences.</summary>
    public string DescribeOptedOutChannels() =>
        OptedOutChannels is not { Count: > 0 } optedOut
            ? "none"
            : string.Join("; ", optedOut
                .GroupBy(o => o.Channel)
                .Select(g => $"{g.Key} (off for {g.Count()} responder(s))"));

    public string DescribeChannelSilences() =>
        ChannelSilences is not { Count: > 0 } silences
            ? "none"
            : string.Join("; ", silences.Select(
                s => $"{s.Channel}→{s.UserId}: {s.Reason ?? "no reason recorded"}"));

    /// <summary>Compact description of the queued-but-not-yet-sent pages, with when each is due.</summary>
    public string DescribeDeferrals() =>
        Deferrals is not { Count: > 0 } deferrals
            ? "none"
            : string.Join("; ", deferrals.Select(
                d => $"{d.Channel}→{d.UserId}: {d.Reason ?? "no reason recorded"} (next attempt {d.NextAttemptAt:HH:mm:ss} UTC)"));

    /// <summary>Compact description of the on-call responders nothing could have paged.</summary>
    public string DescribeUnpageableTargets() =>
        UnpageableTargets is not { Count: > 0 } targets
            ? "none"
            : string.Join("; ", targets.Select(t => $"{t.UserId}: {t.Reason}"));
}
