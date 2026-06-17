using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using FluentValidation;
using Mapster;
using Callu.Application.Services;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Shared.Models.Escalations;
using Callu.Shared.Extensions;
using Callu.Domain.Entities;
using Callu.Infrastructure.Identity;
using Callu.Infrastructure.Persistence.Transactions;

namespace Callu.Infrastructure.Services;

public class EscalationService(
    IEscalationPolicyRepository policyRepo,
    IEscalationStepRepository stepRepo,
    ITransactionManager transactionManager,
    UserManager<ApplicationUser> userManager,
    IValidator<CreateEscalationRequest> createValidator,
    IValidator<CreateEscalationStepRequest> stepValidator,
    ILogger<EscalationService> logger) : IEscalationService
{
    public async Task<IEnumerable<EscalationDto>> GetEscalationPoliciesAsync(CancellationToken cancellationToken = default)
    {
        var policies = await policyRepo.GetQueryable()
            .AsNoTracking()
            .Where(p => !p.IsDeleted)
            .Include(p => p.Team)
            .Include(p => p.Steps.Where(s => !s.IsDeleted))
                .ThenInclude(s => s.Schedule)
            .Include(p => p.Steps.Where(s => !s.IsDeleted))
                .ThenInclude(s => s.Team)
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
            .Include(p => p.Steps.Where(s => !s.IsDeleted))
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);
            
        return policies.Select(p => p.Adapt<EscalationDto>());
    }

    public async Task<EscalationDto> CreateEscalationPolicyAsync(CreateEscalationRequest request, CancellationToken cancellationToken = default)
    {
        var validationResult = await createValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(validationResult.Errors);
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
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            
            await policyRepo.AddAsync(policy, cancellationToken);
            
            return policy.Adapt<EscalationDto>();
        }, cancellationToken);
    }

    public async Task<bool> UpdateEscalationPolicyAsync(Guid id, UpdateEscalationRequest request, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var policy = await policyRepo.FindSingleAsync(p => p.Id == id && !p.IsDeleted, cancellationToken);
            if (policy == null) return false;
            
            if (request.Name != null) policy.Name = request.Name;
            if (request.Description != null) policy.Description = request.Description;
            if (request.IsActive.HasValue) policy.IsActive = request.IsActive.Value;
            if (request.TeamId.HasValue) policy.TeamId = request.TeamId.Value;
            
            policy.UpdatedAt = DateTime.UtcNow;
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
            return true;
        }, cancellationToken);
    }

    public async Task<IEnumerable<EscalationStepDto>> GetEscalationStepsAsync(Guid policyId, CancellationToken cancellationToken = default)
    {
        var policy = await GetEscalationPolicyByIdAsync(policyId, cancellationToken);
        return policy?.Steps ?? Enumerable.Empty<EscalationStepDto>();
    }

    public async Task<EscalationStepDto> AddEscalationStepAsync(Guid policyId, CreateEscalationStepRequest request, CancellationToken cancellationToken = default)
    {
        var validationResult = await stepValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(validationResult.Errors);
        }

        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var distinctUserIds = request.NotifyUserIds?
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Select(u => u.Trim())
                .Distinct()
                .ToList();

            var step = new EscalationStep
            {
                Id = Guid.NewGuid(),
                EscalationPolicyId = policyId,
                Level = request.Level,
                Title = request.Title,
                Description = request.Description,
                DelayMinutes = request.DelayMinutes,
                ScheduleId = request.ScheduleId,
                TeamId = request.TeamId,
                NotifyAllTeamMembers = request.NotifyAllTeamMembers,
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

            step.ScheduleId = request.ScheduleId;
            step.TeamId = request.TeamId;

            var distinctUserIds = request.NotifyUserIds?
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Select(u => u.Trim())
                .Distinct()
                .ToList();

            var desired = distinctUserIds?.ToHashSet() ?? new HashSet<string>();
            var existing = step.TargetedUsers.ToDictionary(u => u.UserId);

            var toRemove = existing.Values.Where(e => !desired.Contains(e.UserId)).ToList();
            foreach (var row in toRemove)
                step.TargetedUsers.Remove(row);

            var now = DateTime.UtcNow;
            foreach (var uid in desired.Where(d => !existing.ContainsKey(d)))
                step.TargetedUsers.Add(new EscalationStepUser { EscalationStepId = step.Id, UserId = uid, CreatedAt = now });

            if (request.NotifyAllTeamMembers.HasValue) step.NotifyAllTeamMembers = request.NotifyAllTeamMembers.Value;
            step.UpdatedAt = now;

            logger.LogInformation("Updated escalation step {StepId} for policy {PolicyId}", stepId, policyId);
            return true;
        }, cancellationToken);
    }

    public async Task<bool> RemoveEscalationStepAsync(Guid stepId, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var step = await stepRepo.FindSingleAsync(s => s.Id == stepId && !s.IsDeleted, cancellationToken);
            if (step == null) return false;
            
            step.IsDeleted = true;
            step.UpdatedAt = DateTime.UtcNow;
            return true;
        }, cancellationToken);
    }

    public async Task<bool> ReorderStepsAsync(Guid policyId, IEnumerable<Guid> stepIds, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var steps = await stepRepo.GetQueryable()
                .Where(s => s.EscalationPolicyId == policyId && !s.IsDeleted)
                .ToListAsync(cancellationToken);

            var inputIds = stepIds.ToList();
            var inputSet = inputIds.ToHashSet();
            var existingIds = steps.Select(s => s.Id).ToHashSet();

            if (!inputSet.SetEquals(existingIds))
            {
                var missing = existingIds.Except(inputSet).ToList();
                var unknown = inputSet.Except(existingIds).ToList();
                throw new InvalidOperationException(
                    $"Reorder input must list every step exactly once. Missing: [{string.Join(",", missing)}], unknown: [{string.Join(",", unknown)}]");
            }

            var orderMap = inputIds.Select((id, index) => new { id, index }).ToDictionary(x => x.id, x => x.index);

            foreach (var step in steps)
            {
                step.Level = orderMap[step.Id] + 1;
                step.UpdatedAt = DateTime.UtcNow;
            }

            return true;
        }, cancellationToken);
    }
}
