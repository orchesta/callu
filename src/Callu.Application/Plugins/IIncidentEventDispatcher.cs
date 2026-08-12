namespace Callu.Application.Plugins;

/// <summary>
/// Outcome of an ACK dispatch, as far as the delivery ledger is concerned.
/// </summary>
public enum AckDispatchOutcome
{
    /// <summary>An attempt row was written; it now carries the delivery state (including any retry schedule).</summary>
    Recorded,

    /// <summary>Nothing was sent and nothing will be: no service, ACK disabled, no URL, or no template.</summary>
    Skipped,

    /// <summary>The attempt could not be recorded (persistence failed); the delivery state is unknown and the caller should retry.</summary>
    NotRecorded
}

/// <summary>
/// Dispatches incident events (ACK) to external systems
/// </summary>
public interface IIncidentEventDispatcher
{
    /// <summary>Send ACK to an external system using the service's inline ACK configuration.</summary>
    /// <returns>How the attempt landed in the <c>WebhookDelivery</c> ledger; this method does not throw.</returns>
    Task<AckDispatchOutcome> SendServiceAckAsync(Guid incidentId, string ackType, CancellationToken cancellationToken = default);

    /// <summary>Runs an operator-defined action once, synchronously; exactly one attempt, never retried.</summary>
    Task<Callu.Shared.Models.Services.ServiceActionExecutionResult> ExecuteManualActionAsync(
        Guid incidentId, Guid actionId, string actorUserId, CancellationToken cancellationToken = default);
}
