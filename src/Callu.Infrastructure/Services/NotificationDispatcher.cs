using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Application.Services;
using Callu.Domain.Enums;
using Callu.Shared.Models.Notifications;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Routes notifications to channel-specific dispatchers. Handles per-user preferences,
/// quiet hours, on-call lookup, and the retry queue. Channel delivery is delegated to
/// INotificationChannelDispatcher implementations.
/// </summary>
public class NotificationDispatcher : INotificationDispatcher
{
    private readonly INotificationRepository _notificationRepo;
    private readonly INotificationPreferenceRepository _prefRepo;
    private readonly ITeamMemberRepository _teamMemberRepo;
    private readonly ITransactionManager _transactionManager;
    private readonly IUserContactRepository _userContacts;
    private readonly IOnCallService _onCallService;
    private readonly IOrganizationSettingsService _organizationSettingsService;
    private readonly INotificationPushService? _pushService;
    private readonly IDateTimeZoneProvider _tzProvider;
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<NotificationDispatcher> _logger;
    private readonly Dictionary<NotificationType, INotificationChannelDispatcher> _channelDispatchers;
    private const int MaxRetryCount = 3;

    public NotificationDispatcher(
        INotificationRepository notificationRepo,
        INotificationPreferenceRepository prefRepo,
        ITeamMemberRepository teamMemberRepo,
        ITransactionManager transactionManager,
        IUserContactRepository userContacts,
        IOnCallService onCallService,
        IOrganizationSettingsService organizationSettingsService,
        IDateTimeZoneProvider tzProvider,
        IEnumerable<INotificationChannelDispatcher> channelDispatchers,
        ApplicationDbContext dbContext,
        ILogger<NotificationDispatcher> logger,
        INotificationPushService? pushService = null)
    {
        _logger = logger;
        _notificationRepo = notificationRepo;
        _prefRepo = prefRepo;
        _teamMemberRepo = teamMemberRepo;
        _transactionManager = transactionManager;
        _userContacts = userContacts;
        _onCallService = onCallService;
        _organizationSettingsService = organizationSettingsService;
        _tzProvider = tzProvider;
        _dbContext = dbContext;
        _pushService = pushService;
        _channelDispatchers = channelDispatchers.ToDictionary(d => d.Channel);
    }

    public async Task<int> NotifyUsersAsync(IEnumerable<string> userIds, NotificationPayload payload, CancellationToken cancellationToken = default)
    {
        var baseUrl = await _organizationSettingsService.GetPublicBaseUrlAsync(cancellationToken);

        return await _transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var incidentUrl = $"{baseUrl}/incidents/{payload.IncidentId}";

            var reached = 0;

            foreach (var userId in userIds)
            {
                var contact = await _userContacts.GetContactByIdAsync(userId, cancellationToken);
                if (contact is null) continue;

                var prefs = await _prefRepo.FindSingleAsync(p => p.UserId == userId, cancellationToken);

                var emailEnabled = prefs?.EmailEnabled ?? true;
                var smsEnabled = prefs?.SmsEnabled ?? false;
                var voiceEnabled = prefs?.VoiceEnabled ?? false;
                var pushEnabled = prefs?.PushEnabled ?? true;

                if (prefs != null && IsInQuietHours(prefs) && payload.EventType != NotificationEventType.EscalationStep)
                {
                    _logger.LogInformation(
                        "Skipping notification for {UserId} — quiet hours active",
                        userId);
                    continue;
                }

                var attempted = false;

                if (emailEnabled && _channelDispatchers.TryGetValue(NotificationType.Email, out var emailDispatcher))
                {
                    await SafeDispatchAsync(emailDispatcher, NotificationType.Email, userId, contact.Email, contact.PhoneNumber, payload, incidentUrl, cancellationToken);
                    attempted = true;
                }

                if (smsEnabled && !string.IsNullOrEmpty(contact.PhoneNumber) && _channelDispatchers.TryGetValue(NotificationType.Sms, out var smsDispatcher))
                {
                    await SafeDispatchAsync(smsDispatcher, NotificationType.Sms, userId, contact.Email, contact.PhoneNumber, payload, incidentUrl, cancellationToken);
                    attempted = true;
                }

                if (voiceEnabled && !string.IsNullOrEmpty(contact.PhoneNumber) && _channelDispatchers.TryGetValue(NotificationType.VoiceCall, out var voiceDispatcher))
                {
                    await SafeDispatchAsync(voiceDispatcher, NotificationType.VoiceCall, userId, contact.Email, contact.PhoneNumber, payload, incidentUrl, cancellationToken);
                    attempted = true;
                }

                if (pushEnabled && _pushService != null)
                {
                    var pushNotification = NotificationFactory.Create(userId, payload, incidentUrl, NotificationType.Push);
                    await _notificationRepo.AddAsync(pushNotification, cancellationToken);
                    await PushToUserAsync(pushNotification, cancellationToken);
                    attempted = true;
                }

                if (attempted) reached++;
            }

            return reached;
        }, cancellationToken);
    }

    private async Task SafeDispatchAsync(
        INotificationChannelDispatcher dispatcher,
        NotificationType channel,
        string userId,
        string? email,
        string? phone,
        NotificationPayload payload,
        string incidentUrl,
        CancellationToken cancellationToken)
    {
        try
        {
            await dispatcher.DispatchAsync(userId, email, phone, payload, incidentUrl, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Channel dispatch failed: channel={Channel} user={UserId} incident={IncidentId}",
                channel, userId, payload.IncidentId);
        }
    }

    public async Task<int> NotifyOnCallAsync(Guid scheduleId, NotificationPayload payload, CancellationToken cancellationToken = default)
    {
        var onCallStatus = await _onCallService.GetCurrentOnCallAsync(scheduleId, cancellationToken);

        if (onCallStatus == null || string.IsNullOrEmpty(onCallStatus.PrimaryUserId))
        {
            NotificationDispatcherLog.NoOnCallUserFound(_logger, scheduleId);
            return 0;
        }

        var userIds = new List<string> { onCallStatus.PrimaryUserId };
        if (payload.IncludeSecondaryOnCall && !string.IsNullOrEmpty(onCallStatus.SecondaryUserId))
            userIds.Add(onCallStatus.SecondaryUserId);

        return await NotifyUsersAsync(userIds, payload, cancellationToken);
    }

    public async Task<int> NotifyTeamAsync(Guid teamId, NotificationPayload payload, bool notifyAllMembers, CancellationToken cancellationToken = default)
    {
        if (notifyAllMembers)
        {
            var memberIds = await _teamMemberRepo.GetQueryable()
                .Where(m => m.TeamId == teamId && !m.IsDeleted)
                .Select(m => m.UserId)
                .Distinct()
                .ToListAsync(cancellationToken);

            if (memberIds.Count == 0)
            {
                _logger.LogWarning("NotifyTeam: team {TeamId} has no members; step skipped", teamId);
                return 0;
            }
            return await NotifyUsersAsync(memberIds, payload, cancellationToken);
        }

        var status = await _onCallService.GetCurrentOnCallForTeamAsync(teamId, cancellationToken);
        if (status is null || string.IsNullOrEmpty(status.PrimaryUserId))
        {
            _logger.LogWarning("NotifyTeam: team {TeamId} has no active on-call; step skipped", teamId);
            return 0;
        }
        return await NotifyUsersAsync(new[] { status.PrimaryUserId }, payload, cancellationToken);
    }

    public async Task ProcessNotificationQueueAsync(CancellationToken cancellationToken = default)
    {
        var baseUrl = await _organizationSettingsService.GetPublicBaseUrlAsync(cancellationToken);

        await _transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var failedNotifications = await _dbContext.Notifications
                .FromSqlInterpolated($"""
                    SELECT *, xmin FROM "Notifications"
                    WHERE NOT "IsDeleted"
                      AND "DeliveryStatus" IN (0, 3)
                      AND "RetryCount" < {MaxRetryCount}
                      AND ("NextRetryAt" IS NULL OR "NextRetryAt" <= NOW())
                    ORDER BY "CreatedAt"
                    LIMIT 50
                    FOR UPDATE SKIP LOCKED
                    """)
                .Include(n => n.Incident)
                .ToListAsync(cancellationToken);

            if (!failedNotifications.Any())
            {
                return false;
            }

            NotificationDispatcherLog.ProcessingFailedNotifications(_logger, failedNotifications.Count);

            foreach (var notification in failedNotifications)
            {
                notification.MarkSending();
                notification.UpdatedAt = DateTime.UtcNow;

                try
                {
                    if (_channelDispatchers.TryGetValue(notification.Type, out var dispatcher))
                    {
                        await dispatcher.RetryAsync(notification, baseUrl, cancellationToken);
                    }
                    else
                    {
                        notification.MarkFailed($"Unsupported channel for retry: {notification.Type}");
                    }
                }
                catch (Exception ex)
                {
                    notification.MarkFailed($"Retry: {ex.Message}");
                    _logger.LogWarning(ex, "[RETRY] Failed notification {NotificationId} (attempt {Attempt})",
                        notification.Id, notification.RetryCount);
                }
            }

            return true;
        }, cancellationToken);
    }

    public async Task<(bool Success, string Message)> SendTestNotificationAsync(
        string userId, string channel, CancellationToken cancellationToken = default)
    {
        var contact = await _userContacts.GetContactByIdAsync(userId, cancellationToken);
        if (contact is null)
            return (false, "User not found");

        var channelType = channel.ToLowerInvariant() switch
        {
            "email" => NotificationType.Email,
            "sms" => NotificationType.Sms,
            "voice" => NotificationType.VoiceCall,
            _ => (NotificationType?)null
        };

        if (channelType == null)
            return (false, $"Unknown channel: {channel}");

        if (_channelDispatchers.TryGetValue(channelType.Value, out var dispatcher))
        {
            return await dispatcher.SendTestAsync(userId, contact.Email, contact.PhoneNumber, cancellationToken);
        }

        return (false, $"No dispatcher registered for channel: {channel}");
    }

    #region Helpers

    /// <summary>
    /// Push real-time notification to user via SignalR (if available)
    /// </summary>
    private async Task PushToUserAsync(Domain.Entities.Notification notification, CancellationToken cancellationToken)
    {
        if (_pushService == null) return;

        try
        {
            var dto = new NotificationItemDto
            {
                Id = notification.Id,
                Title = notification.Title,
                Message = notification.Message,
                Type = MapType(notification.Type),
                ActionUrl = notification.ActionUrl,
                IsRead = false,
                CreatedAt = notification.CreatedAt,
                TimeAgo = "just now"
            };
            await _pushService.PushNotificationAsync(notification.UserId, dto, cancellationToken);
        }
        catch (Exception ex)
        {
            NotificationDispatcherLog.RealtimePushFailed(_logger, ex, notification.UserId);
        }
    }

    private static string MapType(Domain.Enums.NotificationType type) => type switch
    {
        Domain.Enums.NotificationType.VoiceCall => "escalation",
        _ => "info"
    };

    private bool IsInQuietHours(Domain.Entities.NotificationPreference prefs)
    {
        if (string.IsNullOrEmpty(prefs.QuietHoursStart) || string.IsNullOrEmpty(prefs.QuietHoursEnd))
            return false;

        if (!TimeOnly.TryParse(prefs.QuietHoursStart, out var start) ||
            !TimeOnly.TryParse(prefs.QuietHoursEnd, out var end))
            return false;

        var zone = _tzProvider.GetZoneOrNull(prefs.Timezone);
        if (zone is null)
        {
            _logger.LogWarning("QuietHours: unknown timezone '{Timezone}' for user {UserId} — not applying quiet hours",
                prefs.Timezone, prefs.UserId);
            return false;
        }

        var nowInZone = SystemClock.Instance.GetCurrentInstant().InZone(zone);
        var userNow = new TimeOnly(nowInZone.Hour, nowInZone.Minute, nowInZone.Second);

        if (start > end)
            return userNow >= start || userNow < end;
        return userNow >= start && userNow < end;
    }

    #endregion
}
