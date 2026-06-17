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
}
