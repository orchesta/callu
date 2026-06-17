namespace Callu.Application.Plugins;

/// <summary>
/// Dispatches incident events (ACK) to external systems
/// </summary>
public interface IIncidentEventDispatcher
{
    /// <summary>
    /// Send ACK to external system using Service's inline ACK configuration
    /// </summary>
    /// <param name="incidentId">Incident ID</param>
    /// <param name="ackType">Type of ACK (acknowledge, resolve)</param>
    Task SendServiceAckAsync(Guid incidentId, string ackType, CancellationToken cancellationToken = default);
}
