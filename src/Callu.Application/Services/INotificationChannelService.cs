using Callu.Shared.Models.Notifications;

namespace Callu.Application.Services;

public interface INotificationChannelService
{
    Task<List<NotificationChannelDto>> GetAllAsync(CancellationToken ct = default);
    Task<NotificationChannelDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<NotificationChannelDto> CreateAsync(CreateNotificationChannelRequest request, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, UpdateNotificationChannelRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<bool> ToggleAsync(Guid id, CancellationToken ct = default);
    Task<bool> TestAsync(Guid id, string message, CancellationToken ct = default);

    /// <summary>
    /// Dispatch a notification to all enabled channels that match the incident criteria and lifecycle trigger.
    /// </summary>
    Task DispatchIncidentNotificationAsync(
        Guid incidentId,
        string title,
        string severity,
        Guid? serviceId,
        NotificationChannelDispatchEvent dispatchEvent = NotificationChannelDispatchEvent.IncidentCreated,
        CancellationToken ct = default);

    /// <summary>
    /// Re-fires outbound channel deliveries that hit a transient failure and whose backoff
    /// has elapsed. Invoked by the NotificationChannelDeliveryRetryQuartzJob.
    /// </summary>
    Task ProcessDueRetriesAsync(CancellationToken ct = default);
}
