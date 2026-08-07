namespace Callu.Application.Services;

/// <summary>Cross-incident voice-call flood control: answers "was there recent voice-call activity for this
/// user from a DIFFERENT incident?" so the dispatcher can defer instead of dialing.</summary>
public interface IVoiceCallCoalescingGuard
{
    /// <summary>UTC time until which a new voice call to <paramref name="userId"/> should be deferred, or null when
    /// no cooldown applies. The retry sweep must pass <paramref name="includeInFlight"/> false.</summary>
    Task<DateTime?> GetDeferralUntilAsync(string userId, Guid? excludeIncidentId, bool includeInFlight = true, CancellationToken cancellationToken = default);
}

public sealed class VoiceCallCoalescingOptions
{
    /// <summary>Minimum spacing, in seconds, between voice calls to the same user from different incidents;
    /// 0 disables the cooldown (config key <c>Callu:Notifications:VoiceCallCooldownSeconds</c>).</summary>
    public int VoiceCallCooldownSeconds { get; set; } = 0;
}
