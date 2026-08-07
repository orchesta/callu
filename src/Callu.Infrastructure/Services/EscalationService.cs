using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using FluentValidation;
using Mapster;
using Npgsql;
using Callu.Application.Services;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Shared.Exceptions;
using Callu.Shared.Models.Escalations;
using Callu.Shared.Extensions;
using Callu.Domain.Entities;
using Callu.Infrastructure.Identity;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Domain.Enums;

namespace Callu.Infrastructure.Services;

public class EscalationService(
    IEscalationPolicyRepository policyRepo,
    IEscalationStepRepository stepRepo,
    ITransactionManager transactionManager,
    UserManager<ApplicationUser> userManager,
    IValidator<CreateEscalationRequest> createValidator,
    IValidator<CreateEscalationStepRequest> stepValidator,
    IAuditLogService auditLogService,
    ILogger<EscalationService> logger) : IEscalationService
{
    private const string PolicyEntity = "EscalationPolicy";
    public async Task<IEnumerable<EscalationDto>> GetEscalationPoliciesAsync(CancellationToken cancellationToken = default)
    {
        var policies = await policyRepo.GetQueryable()
            .AsNoTracking()
            .Where(p => !p.IsDeleted)
            .Include(p => p.Team)
            .Include(p => p.Steps.Where(s => !s.IsDeleted).OrderBy(s => s.Level))
                .ThenInclude(s => s.Schedule)
            .Include(p => p.Steps.Where(s => !s.IsDeleted).OrderBy(s => s.Level))
                .ThenInclude(s => s.Team)
            .Include(p => p.Steps.Where(s => !s.IsDeleted).OrderBy(s => s.Level))
                .ThenInclude(s => s.TargetedUsers)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);

        return policies.Select(p => p.Adapt<EscalationDto>());
    }

    public async Task<EscalationDetailDto?> GetEscalationPolicyByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var policy = await policyRepo.GetQueryable()
            .AsNoTracking()
            .Include(p => p.Team)
            .Include(p => p.Steps)
                .ThenInclude(s => s.Schedule)
            .Include(p => p.Steps)
                .ThenInclude(s => s.Team)
            .Include(p => p.Steps)
                .ThenInclude(s => s.TargetedUsers)
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, cancellationToken);

        if (policy == null) return null;

        var stepDtos = new List<EscalationStepDto>();
        foreach (var step in policy.Steps.Where(s => !s.IsDeleted).OrderBy(s => s.Level))
        {
            var dto = step.Adapt<EscalationStepDto>();

            var sourceIds = step.TargetedUsers.Select(u => u.UserId).ToArray();

            var userNames = new List<string>();
            foreach (var uid in sourceIds)
            {
                var user = await userManager.FindByIdAsync(uid);
                if (user != null) userNames.Add(StringExtensions.FormatDisplayName(user.FirstName, user.LastName, user.Email) ?? "Unknown");
            }
            stepDtos.Add(dto with { NotifyUserNames = userNames });
        }
        
        var detail = policy.Adapt<EscalationDetailDto>();
        return detail with { Steps = stepDtos };
    }

    public async Task<IEnumerable<EscalationDto>> GetEscalationPoliciesByTeamAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        var policies = await policyRepo.GetQueryable()
            .AsNoTracking()
            .Where(p => p.TeamId == teamId && !p.IsDeleted)
            .Include(p => p.Team)
            .Include(p => p.Steps.Where(s => !s.IsDeleted).OrderBy(s => s.Level))
                .ThenInclude(s => s.Schedule)
            .Include(p => p.Steps.Where(s => !s.IsDeleted).OrderBy(s => s.Level))
                .ThenInclude(s => s.Team)
            .Include(p => p.Steps.Where(s => !s.IsDeleted).OrderBy(s => s.Level))
                .ThenInclude(s => s.TargetedUsers)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);
            
        return policies.Select(p => p.Adapt<EscalationDto>());
    }

    public async Task<EscalationDto> CreateEscalationPolicyAsync(CreateEscalationRequest request, CancellationToken cancellationToken = default)
    {
        var validationResult = await createValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            throw new FluentValidation.ValidationException(validationResult.Errors);
        }

        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var policy = new EscalationPolicy
            {
                Id = Guid.NewGuid(),
                Name = request.Name,
                Description = request.Description,
                TeamId = request.TeamId,
                IsActive = true,
                ExhaustionBehavior = request.ExhaustionBehavior ?? EscalationExhaustionBehavior.Stop,
                MaxRepeatCycles = request.MaxRepeatCycles ?? EscalationPolicy.DefaultMaxRepeatCycles,
                MaxRepeatDurationMinutes = request.MaxRepeatDurationMinutes ?? EscalationPolicy.DefaultMaxRepeatDurationMinutes,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            
            await policyRepo.AddAsync(policy, cancellationToken);

            // This policy decides who a page reaches, so every change to it is auditable.
            await auditLogService.LogAsync(
                null, AuditAction.Created, PolicyEntity, policy.Id.ToString(),
                newValues: $"name={policy.Name}; team={policy.TeamId}",
                cancellationToken: cancellationToken);

            return policy.Adapt<EscalationDto>();
        }, cancellationToken);
    }

    public async Task<bool> UpdateEscalationPolicyAsync(Guid id, UpdateEscalationRequest request, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var policy = await policyRepo.FindSingleAsync(p => p.Id == id && !p.IsDeleted, cancellationToken);
            if (policy == null) return false;
            
            var before = $"name={policy.Name}; team={policy.TeamId}; active={policy.IsActive}";

            if (request.Name != null) policy.Name = request.Name;
            if (request.Description != null) policy.Description = request.Description;
            if (request.IsActive.HasValue) policy.IsActive = request.IsActive.Value;
            if (request.TeamId.HasValue) policy.TeamId = request.TeamId.Value;
            if (request.ExhaustionBehavior.HasValue) policy.ExhaustionBehavior = request.ExhaustionBehavior.Value;
            if (request.MaxRepeatCycles.HasValue) policy.MaxRepeatCycles = request.MaxRepeatCycles.Value;
            if (request.MaxRepeatDurationMinutes.HasValue)
                policy.MaxRepeatDurationMinutes = request.MaxRepeatDurationMinutes.Value;

            policy.UpdatedAt = DateTime.UtcNow;

            await auditLogService.LogAsync(
                null, AuditAction.Updated, PolicyEntity, policy.Id.ToString(),
                oldValues: before,
                newValues: $"name={policy.Name}; team={policy.TeamId}; active={policy.IsActive}",
                cancellationToken: cancellationToken);

            return true;
        }, cancellationToken);
    }

    public async Task<bool> DeleteEscalationPolicyAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var policy = await policyRepo.GetQueryable()
                .Include(p => p.Steps)
                .Where(p => !p.IsDeleted)
                .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
            if (policy == null) return false;

            var now = DateTime.UtcNow;
            policy.IsDeleted = true;
            policy.UpdatedAt = now;
            foreach (var step in policy.Steps)
            {
                step.IsDeleted = true;
                step.UpdatedAt = now;
            }

            await auditLogService.LogAsync(
                null, AuditAction.Deleted, PolicyEntity, policy.Id.ToString(),
                oldValues: $"name={policy.Name}; steps={policy.Steps.Count}",
                cancellationToken: cancellationToken);

            return true;
        }, cancellationToken);
    }

    public async Task<IEnumerable<EscalationStepDto>> GetEscalationStepsAsync(Guid policyId, CancellationToken cancellationToken = default)
    {
        var policy = await GetEscalationPolicyByIdAsync(policyId, cancellationToken);
        return policy?.Steps ?? Enumerable.Empty<EscalationStepDto>();
    }

    /// <summary>
    /// Next free rung: MAX(Level) + 1 over the policy's live steps, matching the unique index's predicate.
    /// </summary>
    private async Task<int> NextLevelAsync(Guid policyId, CancellationToken cancellationToken)
    {
        var highest = await stepRepo.GetQueryable()
            .Where(s => s.EscalationPolicyId == policyId && !s.IsDeleted)
            .MaxAsync(s => (int?)s.Level, cancellationToken);

        return (highest ?? 0) + 1;
    }

    public async Task<EscalationStepDto> AddEscalationStepAsync(Guid policyId, CreateEscalationStepRequest request, CancellationToken cancellationToken = default)
    {
        var validationResult = await stepValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            throw new FluentValidation.ValidationException(validationResult.Errors);
        }

        try
        {
            return await AddStepCoreAsync(policyId, request, cancellationToken);
        }
        catch (DbUpdateException ex) when (IsEscalationStepLevelConflict(ex))
        {
            // Two concurrent adds read the same MAX(Level); the index makes that safe, this makes it
            // legible (the global handler's 409 says nothing about levels).
            throw new ConflictException(
                "Another step was added to this policy at the same time. Reload the policy and try again.");
        }
    }

    private static bool IsEscalationStepLevelConflict(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
        && pg.ConstraintName?.Contains("EscalationSteps", StringComparison.Ordinal) == true;

    private async Task<EscalationStepDto> AddStepCoreAsync(Guid policyId, CreateEscalationStepRequest request, CancellationToken cancellationToken)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var distinctUserIds = request.NotifyUserIds?
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Select(u => u.Trim())
                .Distinct()
                .ToList();

            var level = request.Level > 0
                ? request.Level
                : await NextLevelAsync(policyId, cancellationToken);

            var step = new EscalationStep
            {
                Id = Guid.NewGuid(),
                EscalationPolicyId = policyId,
                Level = level,
                Title = request.Title,
                Description = request.Description,
                DelayMinutes = request.DelayMinutes,
                ScheduleId = request.ScheduleId,
                TeamId = request.TeamId,
                NotifyAllTeamMembers = request.NotifyAllTeamMembers,
                NotifyBothOnCall = request.NotifyBothOnCall,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            if (distinctUserIds is { Count: > 0 })
            {
                var now = DateTime.UtcNow;
                foreach (var uid in distinctUserIds)
                {
                    step.TargetedUsers.Add(new EscalationStepUser
                    {
                        EscalationStepId = step.Id,
                        UserId = uid,
                        CreatedAt = now
                    });
                }
            }

            await stepRepo.AddAsync(step, cancellationToken);

            var dto = step.Adapt<EscalationStepDto>();

            var userNames = new List<string>();
            if (request.NotifyUserIds != null)
            {
                foreach (var uid in request.NotifyUserIds)
                {
                    var user = await userManager.FindByIdAsync(uid);
                    if (user != null) userNames.Add(StringExtensions.FormatDisplayName(user.FirstName, user.LastName, user.Email) ?? "Unknown");
                }
            }
            
            return dto with { NotifyUserNames = userNames };
        }, cancellationToken);
    }

    public async Task<bool> UpdateStepAsync(Guid policyId, Guid stepId, UpdateStepRequest request, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var step = await stepRepo.GetQueryable()
                .Include(s => s.TargetedUsers)
                .FirstOrDefaultAsync(s => s.Id == stepId && s.EscalationPolicyId == policyId && !s.IsDeleted, cancellationToken);
            if (step == null) return false;

            if (request.Title != null) step.Title = request.Title;
            if (request.Description != null) step.Description = request.Description;
            if (request.DelayMinutes.HasValue) step.DelayMinutes = request.DelayMinutes.Value;

            var now = DateTime.UtcNow;

            // Schedule / team / user list are one unit, applied only when named.
            if (request.ScheduleId.HasValue || request.TeamId.HasValue || request.NotifyUserIds is not null)
            {
                step.ScheduleId = request.ScheduleId;
                step.TeamId = request.TeamId;

                var desired = (request.NotifyUserIds ?? [])
                    .Where(u => !string.IsNullOrWhiteSpace(u))
                    .Select(u => u.Trim())
                    .ToHashSet();

                var existing = step.TargetedUsers.ToDictionary(u => u.UserId);

                foreach (var row in existing.Values.Where(e => !desired.Contains(e.UserId)).ToList())
                    step.TargetedUsers.Remove(row);

                foreach (var uid in desired.Where(d => !existing.ContainsKey(d)))
                    step.TargetedUsers.Add(new EscalationStepUser { EscalationStepId = step.Id, UserId = uid, CreatedAt = now });
            }

            if (request.NotifyAllTeamMembers.HasValue) step.NotifyAllTeamMembers = request.NotifyAllTeamMembers.Value;
            if (request.NotifyBothOnCall.HasValue) step.NotifyBothOnCall = request.NotifyBothOnCall.Value;
            step.UpdatedAt = now;

            logger.LogInformation("Updated escalation step {StepId} for policy {PolicyId}", stepId, policyId);
            return true;
        }, cancellationToken);
    }

    public async Task<bool> RemoveEscalationStepAsync(Guid policyId, Guid stepId, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            // Scoped to the policy, as UpdateStepAsync is: otherwise a mismatched pair would delete
            // a rung out of an unrelated ladder and answer 204.
            var step = await stepRepo.FindSingleAsync(
                s => s.Id == stepId && s.EscalationPolicyId == policyId && !s.IsDeleted, cancellationToken);
            if (step == null) return false;
            
            step.IsDeleted = true;
            step.UpdatedAt = DateTime.UtcNow;
            return true;
        }, cancellationToken);
    }

    /// <summary>
    /// Offset levels are parked above every live value before the final 1..N are assigned, because
    /// the unique index is checked per statement.
    /// </summary>
    private const int ReorderParkingOffset = 1_000_000;

    public async Task<bool> ReorderStepsAsync(Guid policyId, IEnumerable<Guid> stepIds, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var existingIds = await stepRepo.GetQueryable()
                .AsNoTracking()
                .Where(s => s.EscalationPolicyId == policyId && !s.IsDeleted)
                .Select(s => s.Id)
                .ToListAsync(cancellationToken);

            var inputIds = stepIds.ToList();
            var inputSet = inputIds.ToHashSet();
            var existingSet = existingIds.ToHashSet();

            if (!inputSet.SetEquals(existingSet))
            {
                var missing = existingSet.Except(inputSet).ToList();
                var unknown = inputSet.Except(existingSet).ToList();
                // 422 with the detail, not 500 with it discarded: a caller reordering after a
                // concurrent step deletion is holding a stale list, and the ids name the fix.
                throw new BusinessRuleException(
                    $"Reorder input must list every step exactly once. Missing: [{string.Join(",", missing)}], unknown: [{string.Join(",", unknown)}]");
            }

            // Both statements run inside the caller's transaction, so no half-renumbered policy.
            await stepRepo.GetQueryable()
                .Where(s => s.EscalationPolicyId == policyId && !s.IsDeleted)
                .ExecuteUpdateAsync(
                    u => u.SetProperty(s => s.Level, s => s.Level + ReorderParkingOffset),
                    cancellationToken);

            // Loaded AFTER the shift: ExecuteUpdateAsync bumps xmin behind the change tracker, so
            // entities read earlier would fail the trailing SaveChanges on concurrency.
            var steps = await stepRepo.GetQueryable()
                .Where(s => s.EscalationPolicyId == policyId && !s.IsDeleted)
                .ToListAsync(cancellationToken);

            var orderMap = inputIds.Select((id, index) => new { id, index }).ToDictionary(x => x.id, x => x.index);

            foreach (var step in steps)
            {
                // 1..N against parked rows at >= the offset: no intermediate state collides.
                step.Level = orderMap[step.Id] + 1;
                step.UpdatedAt = DateTime.UtcNow;
            }

            return true;
        }, cancellationToken);
    }
}
