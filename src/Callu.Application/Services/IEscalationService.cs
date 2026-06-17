using Callu.Shared.Models.Escalations;

namespace Callu.Application.Services;

/// <summary>
/// Service for managing escalation policies and steps
/// </summary>
public interface IEscalationService
{
    Task<IEnumerable<EscalationDto>> GetEscalationPoliciesAsync(CancellationToken cancellationToken = default);
    Task<EscalationDetailDto?> GetEscalationPolicyByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<EscalationDto>> GetEscalationPoliciesByTeamAsync(Guid teamId, CancellationToken cancellationToken = default);
    Task<EscalationDto> CreateEscalationPolicyAsync(CreateEscalationRequest request, CancellationToken cancellationToken = default);
    Task<bool> UpdateEscalationPolicyAsync(Guid id, UpdateEscalationRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteEscalationPolicyAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IEnumerable<EscalationStepDto>> GetEscalationStepsAsync(Guid policyId, CancellationToken cancellationToken = default);
    Task<EscalationStepDto> AddEscalationStepAsync(Guid policyId, CreateEscalationStepRequest request, CancellationToken cancellationToken = default);
    Task<bool> UpdateStepAsync(Guid policyId, Guid stepId, UpdateStepRequest request, CancellationToken cancellationToken = default);
    Task<bool> RemoveEscalationStepAsync(Guid stepId, CancellationToken cancellationToken = default);
    Task<bool> ReorderStepsAsync(Guid policyId, IEnumerable<Guid> stepIds, CancellationToken cancellationToken = default);
}
