using Callu.Shared.Models.AlertRules;

namespace Callu.Application.Services;

/// <summary>
/// CRUD operations for alert automation rules
/// </summary>
public interface IAlertRuleService
{
    /// <summary>
    /// Get all alert rules ordered by priority
    /// </summary>
    Task<List<AlertRuleDto>> GetRulesAsync(CancellationToken ct = default);

    /// <summary>
    /// Get a specific alert rule by ID
    /// </summary>
    Task<AlertRuleDto?> GetRuleAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Create a new alert rule
    /// </summary>
    Task<AlertRuleDto> CreateRuleAsync(CreateAlertRuleRequest request, CancellationToken ct = default);

    /// <summary>
    /// Update an existing alert rule
    /// </summary>
    Task<bool> UpdateRuleAsync(Guid id, UpdateAlertRuleRequest request, CancellationToken ct = default);

    /// <summary>
    /// Delete an alert rule
    /// </summary>
    Task<bool> DeleteRuleAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Toggle a rule's enabled state
    /// </summary>
    Task<bool> ToggleRuleAsync(Guid id, CancellationToken ct = default);
}
