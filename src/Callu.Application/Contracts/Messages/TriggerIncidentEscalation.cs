namespace Callu.Application.Contracts.Messages;

/// <summary>
/// Published after a new incident is committed when an escalation policy applies.
/// Consumed by the worker to call <see cref="Callu.Application.Services.IEscalationOrchestrator.TriggerEscalationAsync"/>.
/// </summary>
public record TriggerIncidentEscalation(
    Guid IncidentId,
    Guid EscalationPolicyId);
