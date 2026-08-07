using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using FluentValidation;
using Callu.Application.Services;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Shared.Models.Schedules;
using Callu.Shared.Extensions;
using Callu.Domain.Entities;
using Callu.Infrastructure.Identity;
using Callu.Infrastructure.Persistence.Transactions;
using NodaTime;
using Callu.Domain.Enums;

namespace Callu.Infrastructure.Services;

/// <summary>Schedule CRUD.</summary>
// A timezone or team change forces a rematerialize: the first moves every shift's UTC instant, the second changes the roster
// on-call reads are filtered by. SaveSchedulePlanAsync is the batch path — one transaction, one rematerialize at the end.
public class ScheduleService(
    IScheduleRepository scheduleRepo,
    IScheduleOccurrenceRepository occurrenceRepo,
    IScheduleRotationRepository rotationRepo,
    IEscalationStepRepository escalationStepRepo,
    ITeamMemberRepository teamMemberRepo,
    ITransactionManager transactionManager,
    UserManager<ApplicationUser> userManager,
    IValidator<CreateScheduleRequest> createScheduleValidator,
    IValidator<SaveSchedulePlanRequest> savePlanValidator,
    IScheduleMaterializer materializer,
    IDateTimeZoneProvider tzProvider,
    IOnCallOverrideService overrideService,
    IAuditLogService auditLogService,
    HybridCache cache,
    ILogger<ScheduleService> logger,
    IOnCallService onCallService) : IScheduleService
{
    public async Task<IEnumerable<ScheduleDto>> GetSchedulesAsync(CancellationToken cancellationToken = default)
    {
        var schedules = await scheduleRepo.GetQueryable()
            .AsNoTracking()
            .Where(s => !s.IsDeleted)
            .Include(s => s.Team)
            .Include(s => s.Rotations)
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);

        var result = new List<ScheduleDto>();
        foreach (var schedule in schedules)
        {
            var currentOnCall = await GetCurrentOnCallUserNameAsync(schedule.Id);
            result.Add(new ScheduleDto
            {
                Id = schedule.Id,
                Name = schedule.Name,
                Description = schedule.Description,
                TeamId = schedule.TeamId,
                TeamName = schedule.Team?.Name,
                Timezone = schedule.Timezone,
                CurrentOnCallUser = currentOnCall,
                RotationCount = schedule.Rotations.Count(r => !r.IsDeleted),
                CreatedAt = schedule.CreatedAt
            });
        }
        return result;
    }

    public async Task<ScheduleDetailDto?> GetScheduleByIdAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        var schedule = await scheduleRepo.GetQueryable()
            .AsNoTracking()
            .Include(s => s.Team)
            .Include(s => s.Rotations)
            .FirstOrDefaultAsync(s => s.Id == scheduleId && !s.IsDeleted, cancellationToken);

        if (schedule == null) return null;

        var rotationDtos = new List<ScheduleRotationDto>();
        foreach (var rotation in schedule.Rotations.Where(r => !r.IsDeleted).OrderBy(r => r.Order))
        {
            var user = await userManager.FindByIdAsync(rotation.UserId);
            var userName = GetUserDisplayName(user);
            rotationDtos.Add(new ScheduleRotationDto
            {
                Id = rotation.Id,
                ScheduleId = rotation.ScheduleId,
                UserId = rotation.UserId,
                UserName = userName,
                UserInitials = GetInitials(userName),
                HandoverStartLocal = rotation.HandoverStartLocal,
                ShiftLengthMinutes = rotation.ShiftLengthMinutes,
                RecurrenceType = rotation.RecurrenceType,
                RecurrenceIntervalDays = rotation.RecurrenceIntervalDays,
                OwnershipDays = rotation.OwnershipDays,
                RecurrenceEndDate = rotation.RecurrenceEndDate,
                IsPrimary = rotation.IsPrimary,
                Order = rotation.Order
            });
        }

        var currentOnCall = await GetCurrentOnCallUserNameAsync(schedule.Id);

        return new ScheduleDetailDto
        {
            Id = schedule.Id,
            Name = schedule.Name,
            Description = schedule.Description,
            TeamId = schedule.TeamId,
            TeamName = schedule.Team?.Name,
            Timezone = schedule.Timezone,
            CurrentOnCallUser = currentOnCall,
            RotationCount = schedule.Rotations.Count(r => !r.IsDeleted),
            CreatedAt = schedule.CreatedAt,
            Rotations = rotationDtos,
            // Filled from the override service: the field shipped hardcoded empty, so every caller
            // that trusted it believed a schedule had no overrides even while one was rerouting pages.
            Overrides = (await overrideService.GetOverridesAsync(schedule.Id, cancellationToken)).ToList()
        };
    }

    public async Task<IEnumerable<ScheduleDto>> GetSchedulesByTeamAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        var schedules = await scheduleRepo.GetQueryable()
            .AsNoTracking()
            .Where(s => s.TeamId == teamId && !s.IsDeleted)
            .Include(s => s.Team)
            .Include(s => s.Rotations)
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);

        var result = new List<ScheduleDto>();
        foreach (var schedule in schedules)
        {
            var currentOnCall = await GetCurrentOnCallUserNameAsync(schedule.Id);
            result.Add(new ScheduleDto
            {
                Id = schedule.Id,
                Name = schedule.Name,
                Description = schedule.Description,
                TeamId = schedule.TeamId,
                TeamName = schedule.Team?.Name,
                Timezone = schedule.Timezone,
                CurrentOnCallUser = currentOnCall,
                RotationCount = schedule.Rotations.Count(r => !r.IsDeleted),
                CreatedAt = schedule.CreatedAt
            });
        }
        return result;
    }

    public async Task<ScheduleDto> CreateScheduleAsync(CreateScheduleRequest request, CancellationToken cancellationToken = default)
    {
        var validationResult = await createScheduleValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
            throw new ValidationException(validationResult.Errors);

        EnsureKnownTimezone(request.Timezone);

        var dto = await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var schedule = new Schedule
            {
                Id = Guid.NewGuid(),
                Name = request.Name,
                Description = request.Description,
                TeamId = request.TeamId,
                Timezone = request.Timezone,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await scheduleRepo.AddAsync(schedule, cancellationToken);

            return new ScheduleDto
            {
                Id = schedule.Id,
                Name = schedule.Name,
                Description = schedule.Description,
                TeamId = schedule.TeamId,
                Timezone = schedule.Timezone,
                RotationCount = 0,
                CreatedAt = schedule.CreatedAt
            };
        }, cancellationToken);

        await materializer.RematerializeScheduleAsync(dto.Id, IScheduleMaterializer.DefaultHorizon, cancellationToken);
        return dto;
    }

    public async Task<bool> UpdateScheduleAsync(Guid scheduleId, UpdateScheduleRequest request, CancellationToken cancellationToken = default)
    {
        var rematerialize = false;
        var updated = await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var schedule = await scheduleRepo.FindSingleAsync(s => s.Id == scheduleId && !s.IsDeleted, cancellationToken);
            if (schedule == null) return false;

            if (request.Name != null) schedule.Name = request.Name;
            if (request.Description != null) schedule.Description = request.Description;
            if (request.Timezone != null && schedule.Timezone != request.Timezone)
            {
                EnsureKnownTimezone(request.Timezone);
                schedule.Timezone = request.Timezone;
                rematerialize = true;
            }
            if (request.TeamId.HasValue && schedule.TeamId != request.TeamId.Value)
            {
                var storedUserIds = await rotationRepo.GetQueryable()
                    .AsNoTracking()
                    .Where(r => r.ScheduleId == scheduleId && !r.IsDeleted)
                    .Select(r => r.UserId)
                    .Distinct()
                    .ToListAsync(cancellationToken);

                await EnsureUsersBelongToTeamAsync(request.TeamId.Value, storedUserIds, cancellationToken);
                schedule.TeamId = request.TeamId.Value;
                rematerialize = true;
            }

            var now = DateTime.UtcNow;
            rematerialize = FlagIfRematerializeNeeded(schedule, rematerialize, now);
            schedule.UpdatedAt = now;

            return true;
        }, cancellationToken);

        if (!updated) return false;

        if (rematerialize)
        {
            try
            {
                await RematerializeAfterCommitAsync(scheduleId, cancellationToken);
            }
            finally
            {
                await cache.RemoveAsync($"oncall:{scheduleId}", cancellationToken);
            }
        }
        return true;
    }

    /// <summary>Applies the whole plan — settings and every rotation change — in one transaction, rematerializing once at the end.</summary>
    // The per-rotation endpoints rematerialize after each call, so a re-ordered rota goes live half-moved. Membership is checked
    // against the plan being written, not what is stored, so moving a schedule to another team and re-staffing it in one save works.
    public async Task<bool> SaveSchedulePlanAsync(
        Guid scheduleId, SaveSchedulePlanRequest request, CancellationToken cancellationToken = default)
    {
        var validationResult = await savePlanValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
            throw new ValidationException(validationResult.Errors);

        if (request.Timezone != null)
            EnsureKnownTimezone(request.Timezone);

        var outcome = await transactionManager.ExecuteInTransactionAsync(
            () => ApplyPlanAsync(scheduleId, request, cancellationToken), cancellationToken);

        if (outcome == PlanOutcome.NotFound)
            return false;

        // The rota decides who is on call, so an auditor has to be able to see when it moved.
        await auditLogService.LogAsync(
            null, AuditAction.Updated, "Schedule", scheduleId.ToString(),
            newValues: $"rotations={request.Rotations?.Count ?? 0}; timezone={request.Timezone ?? "(unchanged)"}",
            description: outcome == PlanOutcome.Rematerialize ? "Plan saved and rematerialized" : "Plan saved",
            cancellationToken: cancellationToken);

        try
        {
            if (outcome == PlanOutcome.Rematerialize)
                await RematerializeAfterCommitAsync(scheduleId, cancellationToken);
        }
        finally
        {
            // The cached on-call status carries the schedule's name as well as who is on it, so it is
            // stale after any accepted save — including one whose rematerialize then failed.
            await cache.RemoveAsync($"oncall:{scheduleId}", cancellationToken);
        }
        return true;
    }

    /// <summary>Rematerializes the schedule after the plan's transaction has committed.</summary>
    // It cannot join that transaction, so NeedsRematerializeSince — written inside it — is what closes the window: it survives a
    // crash and forces a regeneration on the next save even when no field moved. The failure still propagates.
    private async Task RematerializeAfterCommitAsync(Guid scheduleId, CancellationToken cancellationToken)
    {
        try
        {
            await materializer.RematerializeScheduleAsync(scheduleId, IScheduleMaterializer.DefaultHorizon, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Schedule {ScheduleId}: the plan committed but the rematerialize failed — the stored "
                + "occurrences still expand the PREVIOUS plan and the schedule is flagged "
                + "(NeedsRematerializeSince) for recovery by the next save or the daily materialization job",
                scheduleId);
            throw;
        }
    }

    /// <summary>Marks the occurrence table as owing this schedule a regeneration, and reports whether one is now due.</summary>
    // A flag left by an earlier failed rematerialize counts, so re-saving an unchanged plan retries it. The timestamp is re-stamped
    // each time because the materializer clears only the exact value it read.
    private static bool FlagIfRematerializeNeeded(Schedule schedule, bool rematerialize, DateTime now)
    {
        if (!rematerialize && schedule.NeedsRematerializeSince is null)
            return false;

        schedule.NeedsRematerializeSince = now;
        return true;
    }

    private enum PlanOutcome { NotFound, Applied, Rematerialize }

    private async Task<PlanOutcome> ApplyPlanAsync(
        Guid scheduleId, SaveSchedulePlanRequest request, CancellationToken cancellationToken)
    {
        var schedule = await scheduleRepo.FindSingleAsync(s => s.Id == scheduleId && !s.IsDeleted, cancellationToken);
        if (schedule == null) return PlanOutcome.NotFound;

        var stored = await rotationRepo.GetQueryable()
            .Where(r => r.ScheduleId == scheduleId && !r.IsDeleted)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var touched = false;
        var rematerialize = false;

        if (request.Name != null && schedule.Name != request.Name)
        {
            schedule.Name = request.Name;
            touched = true;
        }

        if (request.Description != null && schedule.Description != request.Description)
        {
            schedule.Description = request.Description;
            touched = true;
        }

        if (request.Timezone != null && schedule.Timezone != request.Timezone)
        {
            schedule.Timezone = request.Timezone;
            touched = true;
            rematerialize = true;
        }

        var targetTeamId = request.TeamId ?? schedule.TeamId;
        var teamChanged = targetTeamId != schedule.TeamId;

        // Validate against what the schedule will look like after this save: the incoming rotation
        // list when one was sent, the stored one otherwise. Checking the stored rotations while a
        // new roster is being written in the same request is what made a team move impossible.
        if (teamChanged || request.Rotations != null)
        {
            var plannedUserIds = request.Rotations != null
                ? request.Rotations.Select(r => r.UserId)
                : stored.Select(r => r.UserId);
            await EnsureUsersBelongToTeamAsync(targetTeamId, plannedUserIds, cancellationToken);
        }

        if (teamChanged)
        {
            schedule.TeamId = targetTeamId;
            touched = true;
            rematerialize = true;
        }

        if (request.Rotations != null &&
            await ApplyRotationsAsync(scheduleId, stored, request.Rotations, now, cancellationToken))
        {
            touched = true;
            rematerialize = true;
        }

        // Also true when an earlier save left the flag standing: its rematerialize never landed, so
        // this save has to run one even if it moved nothing itself.
        rematerialize = FlagIfRematerializeNeeded(schedule, rematerialize, now);
        if (rematerialize)
            touched = true;

        if (touched)
            schedule.UpdatedAt = now;

        return rematerialize ? PlanOutcome.Rematerialize : PlanOutcome.Applied;
    }

    /// <summary>Reconciles the stored rotations with the plan's list and reports whether anything moved.</summary>
    // Field-by-field, so a plan restating the current rota writes nothing and triggers no rematerialize.
    private async Task<bool> ApplyRotationsAsync(
        Guid scheduleId,
        List<ScheduleRotation> stored,
        IReadOnlyList<SchedulePlanRotation> planned,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var changed = false;
        var kept = new HashSet<Guid>();

        foreach (var item in planned)
        {
            if (item.Id is { } rotationId)
            {
                var existing = stored.FirstOrDefault(r => r.Id == rotationId)
                    ?? throw new Callu.Shared.Exceptions.NotFoundException("ScheduleRotation", rotationId);

                kept.Add(existing.Id);
                if (UpdateRotationFields(existing, item))
                {
                    existing.UpdatedAt = now;
                    changed = true;
                }
                continue;
            }

            var rotation = new ScheduleRotation
            {
                Id = Guid.NewGuid(),
                ScheduleId = scheduleId,
                CreatedAt = now,
                UpdatedAt = now
            };
            UpdateRotationFields(rotation, item);
            await rotationRepo.AddAsync(rotation, cancellationToken);
            changed = true;
        }

        foreach (var rotation in stored.Where(r => !kept.Contains(r.Id)))
        {
            rotation.IsDeleted = true;
            rotation.UpdatedAt = now;
            changed = true;
        }

        if (changed)
            logger.LogInformation(
                "Schedule {ScheduleId}: rotation plan applied — {Planned} planned, {Removed} removed",
                scheduleId, planned.Count, stored.Count(r => !kept.Contains(r.Id)));

        return changed;
    }

    /// <summary>Writes every field as given (not a patch) and reports whether any of them moved.</summary>
    private static bool UpdateRotationFields(ScheduleRotation rotation, SchedulePlanRotation item)
    {
        var changed =
            !string.Equals(rotation.UserId, item.UserId, StringComparison.Ordinal) ||
            rotation.HandoverStartLocal != item.HandoverStartLocal ||
            rotation.ShiftLengthMinutes != item.ShiftLengthMinutes ||
            rotation.IsPrimary != item.IsPrimary ||
            rotation.Order != item.Order ||
            rotation.RecurrenceType != item.RecurrenceType ||
            rotation.RecurrenceIntervalDays != item.RecurrenceIntervalDays ||
            rotation.OwnershipDays != item.OwnershipDays ||
            rotation.RecurrenceEndDate != item.RecurrenceEndDate;

        if (!changed) return false;

        rotation.UserId = item.UserId;
        rotation.HandoverStartLocal = item.HandoverStartLocal;
        rotation.ShiftLengthMinutes = item.ShiftLengthMinutes;
        rotation.IsPrimary = item.IsPrimary;
        rotation.Order = item.Order;
        rotation.RecurrenceType = item.RecurrenceType;
        rotation.RecurrenceIntervalDays = item.RecurrenceIntervalDays;
        rotation.OwnershipDays = item.OwnershipDays;
        rotation.RecurrenceEndDate = item.RecurrenceEndDate;
        return true;
    }

    public async Task<bool> DeleteScheduleAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        var deleted = await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var schedule = await scheduleRepo.GetQueryable()
                .Include(s => s.Rotations)
                .Where(s => !s.IsDeleted)
                .FirstOrDefaultAsync(s => s.Id == scheduleId, cancellationToken);
            if (schedule == null) return false;

            // Refuse while a live escalation step still names this schedule; otherwise the step
            // points at a deleted rota and pages nobody.
            var referencing = await escalationStepRepo.GetQueryable()
                .AsNoTracking()
                .Where(s => s.ScheduleId == scheduleId && !s.IsDeleted && !s.EscalationPolicy.IsDeleted)
                .Select(s => s.EscalationPolicy.Name)
                .Distinct()
                .Take(6)
                .ToListAsync(cancellationToken);

            if (referencing.Count > 0)
                throw new Callu.Shared.Exceptions.BusinessRuleException(
                    "This schedule is still a paging target of the escalation policy/policies: "
                    + string.Join(", ", referencing)
                    + ". Repoint or remove those steps first — deleting it would leave them paging nobody.");

            var now = DateTime.UtcNow;
            schedule.IsDeleted = true;
            schedule.UpdatedAt = now;

            foreach (var rotation in schedule.Rotations)
            {
                rotation.IsDeleted = true;
                rotation.UpdatedAt = now;
            }

            await occurrenceRepo.GetQueryable()
                .Where(o => o.ScheduleId == scheduleId)
                .ExecuteDeleteAsync(cancellationToken);

            return true;
        }, cancellationToken);

        if (deleted)
            await cache.RemoveAsync($"oncall:{scheduleId}", cancellationToken);
        return deleted;
    }

    #region Private Helpers

    private void EnsureKnownTimezone(string id)
    {
        if (tzProvider.GetZoneOrNull(id) is null)
            throw new ValidationException($"Unknown IANA timezone: '{id}'");
    }

    /// <summary>Rejects a save whose rotation members are outside the schedule's team roster.</summary>
    // On-call reads filter by that roster, so such a rotation is silently dropped and the schedule ends up with nobody on call.
    private async Task EnsureUsersBelongToTeamAsync(
        Guid teamId, IEnumerable<string> userIds, CancellationToken cancellationToken)
    {
        var wanted = userIds.Distinct(StringComparer.Ordinal).ToList();
        if (wanted.Count == 0) return;

        var memberIds = await teamMemberRepo.GetQueryable()
            .AsNoTracking()
            .Where(tm => tm.TeamId == teamId && !tm.IsDeleted)
            .Select(tm => tm.UserId)
            .ToListAsync(cancellationToken);

        var members = new HashSet<string>(memberIds, StringComparer.Ordinal);
        var outsiders = wanted.Where(id => !members.Contains(id)).ToList();
        if (outsiders.Count > 0)
            throw new ValidationException(
                $"Cannot save this schedule against team {teamId}: {outsiders.Count} rotation member(s) are not in that team. " +
                "Add them to the team, or pick members who are already in it.");
    }

    private async Task<string?> GetCurrentOnCallUserNameAsync(Guid scheduleId, CancellationToken ct = default)
    {
        var status = await onCallService.GetCurrentOnCallAsync(scheduleId, cancellationToken: ct);
        return status?.PrimaryUserName;
    }

    private static string? GetUserDisplayName(ApplicationUser? user)
    {
        if (user == null) return null;
        return StringExtensions.FormatDisplayName(user.FirstName, user.LastName, user.Email);
    }

    private static string? GetInitials(string? name) => name.GetInitials();

    #endregion
}
