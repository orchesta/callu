using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Callu.Infrastructure.Services;

/// <summary>DB-backed <see cref="IVoiceCallCoalescingGuard"/>, reading the user's most recent voice-call activity.</summary>
// Rows of excludeIncidentId are ignored so same-incident re-dials are never throttled. The residual two-host race is accepted:
// the worst outcome is two concurrent calls instead of N.
public class VoiceCallCoalescingGuard(
    INotificationRepository notificationRepo,
    IOptions<VoiceCallCoalescingOptions> options) : IVoiceCallCoalescingGuard
{
    public async Task<DateTime?> GetDeferralUntilAsync(
        string userId, Guid? excludeIncidentId, bool includeInFlight = true, CancellationToken cancellationToken = default)
    {
        var cooldownSeconds = options.Value.VoiceCallCooldownSeconds;
        if (cooldownSeconds <= 0) return null;

        var cutoff = DateTime.UtcNow.AddSeconds(-cooldownSeconds);

        var query = notificationRepo.GetQueryable()
            .Where(n => n.Type == NotificationType.VoiceCall &&
                        n.UserId == userId &&
                        !n.IsDeleted);

        if (excludeIncidentId is not null)
            query = query.Where(n => n.IncidentId != excludeIncidentId);

        // Anchor: a delivered call's SentAt; optionally an in-flight call's LastAttemptAt
        // (initial dispatch only — see the interface note on the retry sweep's batch claim).
        var anchor = await query
            .Where(n =>
                (n.DeliveryStatus == NotificationDeliveryStatus.Delivered && n.SentAt >= cutoff) ||
                (includeInFlight &&
                 (n.DeliveryStatus == NotificationDeliveryStatus.Sending ||
                  n.DeliveryStatus == NotificationDeliveryStatus.Retrying) && n.LastAttemptAt >= cutoff))
            .Select(n => n.DeliveryStatus == NotificationDeliveryStatus.Delivered ? n.SentAt : n.LastAttemptAt)
            .OrderByDescending(t => t)
            .FirstOrDefaultAsync(cancellationToken);

        if (anchor is null) return null;

        return anchor.Value.AddSeconds(cooldownSeconds);
    }
}
