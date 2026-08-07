using Callu.Application.Services;
using Callu.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Services;

/// <summary>THE one implementation of taking a person off the on-call surface, shared by account deletion and team removal.</summary>
// Two phases because a rematerialize cannot join the caller's transaction: DetachAsync runs inside it and flags the affected
// schedules, RepairAsync regenerates them after the commit, so a crash in between is still repairable.
internal static class OnCallMembershipCascade
{
    /// <summary>Detaches a user from the on-call surface inside the caller's transaction, returning the schedules left stale.</summary>
    // A null teamId means every team (the account is gone); a set one scopes to that team's schedules and policies only.
    public static async Task<OnCallDetachment> DetachAsync(
        ApplicationDbContext db,
        string userId,
        Guid? teamId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        // Scope by schedule/policy id rather than by navigation: keeps the query provider-agnostic
        // and the intent explicit.
        List<Guid>? teamScheduleIds = null;
        List<Guid>? teamStepIds = null;

        if (teamId is { } scopedTeamId)
        {
            teamScheduleIds = await db.Schedules
                .Where(s => s.TeamId == scopedTeamId && !s.IsDeleted)
                .Select(s => s.Id)
                .ToListAsync(cancellationToken);

            var teamPolicyIds = await db.EscalationPolicies
                .Where(p => p.TeamId == scopedTeamId && !p.IsDeleted)
                .Select(p => p.Id)
                .ToListAsync(cancellationToken);

            teamStepIds = await db.EscalationSteps
                .Where(s => teamPolicyIds.Contains(s.EscalationPolicyId))
                .Select(s => s.Id)
                .ToListAsync(cancellationToken);
        }

        var rotationQuery = db.ScheduleRotations.Where(r => r.UserId == userId && !r.IsDeleted);
        if (teamScheduleIds is not null)
            rotationQuery = rotationQuery.Where(r => teamScheduleIds.Contains(r.ScheduleId));

        var rotations = await rotationQuery.ToListAsync(cancellationToken);
        foreach (var rotation in rotations)
        {
            rotation.IsDeleted = true;
            rotation.UpdatedAt = now;
        }

        // EscalationStepUser is a pure junction with no soft-delete flag: an orphaned GUID here does
        // not page anyone, it silently shrinks the step's target list.
        var targetQuery = db.EscalationStepUsers
            .IgnoreQueryFilters()
            .Where(e => e.UserId == userId);
        if (teamStepIds is not null)
            targetQuery = targetQuery.Where(e => teamStepIds.Contains(e.EscalationStepId));

        var escalationTargets = await targetQuery.ToListAsync(cancellationToken);
        if (escalationTargets.Count > 0)
            db.EscalationStepUsers.RemoveRange(escalationTargets);

        // Occurrences outlive the rotation row until the schedule is rematerialized, so a schedule
        // that still names this user is either this removal's work or unfinished work from an
        // earlier attempt. Either way it needs regenerating — which is what makes this re-entrant.
        var occurrenceQuery = db.ScheduleOccurrences.Where(o => o.UserId == userId);
        if (teamScheduleIds is not null)
            occurrenceQuery = occurrenceQuery.Where(o => teamScheduleIds.Contains(o.ScheduleId));

        var staleScheduleIds = await occurrenceQuery
            .Select(o => o.ScheduleId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var affectedScheduleIds = rotations
            .Select(r => r.ScheduleId)
            .Concat(staleScheduleIds)
            .Distinct()
            .ToList();

        // The recovery flag, raised in the SAME transaction as the rotation soft-delete. Without it,
        // a crash between this commit and the rematerialize below leaves the occurrence table
        // expanding a deleted rotation with nothing recording that it is stale.
        if (affectedScheduleIds.Count > 0)
        {
            var schedules = await db.Schedules
                .Where(s => affectedScheduleIds.Contains(s.Id))
                .ToListAsync(cancellationToken);

            foreach (var schedule in schedules)
                schedule.NeedsRematerializeSince = now;
        }

        return new OnCallDetachment(rotations.Count, escalationTargets.Count, affectedScheduleIds);
    }

    /// <summary>Regenerates the affected schedules and drops their on-call cache, after the caller's transaction committed.</summary>
    // Fails loudly: the membership change is durable while the schedule still lists the person, and the flag lets a later save repair it.
    public static async Task RepairAsync(
        IScheduleMaterializer materializer,
        HybridCache cache,
        ILogger logger,
        string userId,
        IReadOnlyList<Guid> scheduleIds,
        CancellationToken cancellationToken)
    {
        List<Exception>? failures = null;

        foreach (var scheduleId in scheduleIds)
        {
            try
            {
                await materializer.RematerializeScheduleAsync(
                    scheduleId, IScheduleMaterializer.DefaultHorizon, cancellationToken);
                await cache.RemoveAsync($"oncall:{scheduleId}", cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex,
                    "User {UserId} was detached but schedule {ScheduleId} still lists them: rematerialize failed. "
                    + "The schedule is flagged (NeedsRematerializeSince); re-issue the removal or wait for the daily job",
                    userId, scheduleId);
                (failures ??= []).Add(ex);
            }
        }

        if (failures is not null)
            throw new AggregateException(
                $"User {userId} was detached but {failures.Count} schedule(s) could not be rematerialized; "
                + "re-issue the removal to repair them.",
                failures);
    }
}

/// <summary>What <see cref="OnCallMembershipCascade.DetachAsync"/> actually had to undo.</summary>
internal readonly record struct OnCallDetachment(
    int RotationsRemoved,
    int EscalationTargetsRemoved,
    IReadOnlyList<Guid> AffectedScheduleIds)
{
    /// <summary>Whether the detach touched anything at all — the caller's idempotency signal.</summary>
    public bool TouchedAnything =>
        RotationsRemoved > 0 || EscalationTargetsRemoved > 0 || AffectedScheduleIds.Count > 0;
}
