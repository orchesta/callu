using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Callu.Domain.Enums;
using Callu.Shared.Models.Notifications;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Factory for creating Notification entity records with consistent defaults.
/// Shared across all channel dispatchers.
/// </summary>
public static class NotificationFactory
{
    public static Domain.Entities.Notification Create(
        string userId,
        NotificationPayload payload,
        string? incidentUrl,
        NotificationType type,
        int retryGeneration = 0)
    {
        return new Domain.Entities.Notification
        {
            Id = Guid.NewGuid(),
            IncidentId = payload.IncidentId,
            UserId = userId,
            Type = type,
            Title = $"[{payload.Severity}] {payload.Title}",
            Message = $"{payload.EventType}: {payload.Description ?? $"Escalation Level {payload.EscalationLevel}"}",
            ActionUrl = incidentUrl,
            IsSent = false,
            RetryCount = 0,
            DedupeKey = ComputeDedupeKey(userId, payload, type, retryGeneration),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Canonical idempotency key shared by every dispatcher. Matches the partial
    /// unique index <c>IX_Notifications_DedupeKey</c> (WHERE DedupeKey IS NOT NULL).
    /// SHA-256 → 32 hex chars (128 bits) fits well under the 200-char column.
    /// retryGeneration lets operator-initiated re-pages coexist with idempotency
    /// for the original page (e.g. voice-timeout re-dial uses CallLog.AttemptNumber).
    /// </summary>
    public static string ComputeDedupeKey(
        string userId,
        NotificationPayload payload,
        NotificationType type,
        int retryGeneration = 0)
    {
        var incidentPart = payload.IncidentId == Guid.Empty ? "none" : payload.IncidentId.ToString("N");
        var canonical = string.Join('|',
            incidentPart,
            (userId ?? string.Empty).ToLowerInvariant(),
            ((int)type).ToString(CultureInfo.InvariantCulture),
            ((int)payload.EventType).ToString(CultureInfo.InvariantCulture),
            payload.EscalationLevel.ToString(CultureInfo.InvariantCulture),
            retryGeneration.ToString(CultureInfo.InvariantCulture));

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes, 0, 16);
    }
}
