using Callu.Domain.Entities;

namespace Callu.Application.Services;

/// <summary>
/// Engine that evaluates alert automation rules against incidents.
/// Called from IncidentService after incident creation/update.
/// </summary>
public interface IAlertRuleEngine
{
    /// <summary>
    /// Evaluate all active rules against the given incident and execute matching actions.
    /// Returns the number of rules that were triggered.
    /// </summary>
    Task<int> EvaluateAsync(Incident incident, CancellationToken ct = default);

    /// <summary>Side-effect-free pre-pass run inside the incident-creation transaction: the name of the first
    /// matching enabled rule whose <c>SuppressNotification</c> action opts into <c>SuppressPaging</c>, or null.</summary>
    Task<string?> ShouldSuppressPagingAsync(Incident incident, CancellationToken ct = default);
}
