/**
 * Audit Log Types — mirrors the BE AuditLogDto (Callu.Shared.Models.Audit.AuditLogDto),
 * serialized camelCase. The /audit-logs endpoint returns a flat, newest-first array.
 */

/** AuditAction values from the BE enum, serialized as strings. */
export const AUDIT_ACTIONS = [
  'Created', 'Updated', 'Deleted', 'Viewed',
  'Login', 'Logout', 'LoginFailed', 'PasswordChanged',
  'RoleAssigned', 'RoleRemoved', 'SettingsChanged',
  'Escalated', 'Reassigned', 'Acknowledged', 'Resolved', 'Closed', 'Reopened',
  'Exported', 'OverrideCreated', 'OverrideCancelled',
  'EscalationExhausted', 'EscalationNobodyReached', 'EscalationTargetsUnpageable',
  'EscalationChannelsSilent', 'EscalationChannelsPartiallySilent',
  'EscalationDispatchFailed', 'EscalationDispatchFailing', 'EscalationDispatchPartiallyFailed',
  'EscalationTriggerFailed', 'DispatchFailed', 'Suppressed', 'MarkedUsed', 'Cascaded',
  'Submitted', 'Rejected', 'Published', 'Locked',
  'IntegrityVerified', 'IntegrityBroken',
  'VoiceCallLost', 'VoiceCallNeverConfirmed', 'ConferenceInviteReachedNobody',
] as const;

export type AuditActionValue = (typeof AUDIT_ACTIONS)[number];

/** AuditActorType values from the BE enum, serialized as strings. */
export const AUDIT_ACTOR_TYPES = ['User', 'Service', 'System', 'Admin', 'External', 'Unknown'] as const;

export type AuditActorTypeValue = (typeof AUDIT_ACTOR_TYPES)[number];

/** AuditOutcome values from the BE enum, serialized as strings. */
export const AUDIT_OUTCOMES = ['Success', 'Failure', 'Partial', 'Unknown'] as const;

export type AuditOutcomeValue = (typeof AUDIT_OUTCOMES)[number];

export interface AuditLogEntry {
  id: string;
  createdAt: string;
  actorId?: string;
  actorDisplayName?: string;
  actorType: AuditActorTypeValue | string;
  action: AuditActionValue | string;
  eventName: string;
  eventCategory: string;
  outcome: AuditOutcomeValue | string;
  resourceType: string;
  resourceId?: string;
  summary?: string;
  changeBefore?: string;
  changeAfter?: string;
  requestIpAddress?: string;
  requestUserAgent?: string;
  requestRoute?: string;
}

export interface AuditLogFilters {
  entityName?: string;
  entityId?: string;
  count?: number;
}

/** What the search endpoint narrows the trail down by. */
export interface AuditLogSearchFilter {
  /** Inclusive lower bound, ISO-8601 UTC. */
  from?: string;
  /** Exclusive upper bound, ISO-8601 UTC. */
  to?: string;
  actorId?: string;
  action?: AuditActionValue;
  resourceType?: string;
  /** Narrows to any of the listed resource types. */
  resourceTypes?: string[];
  resourceId?: string;
  requestIpAddress?: string;
  /** Matches the actor name, the summary, or the request route. */
  query?: string;
  /** Reads oldest first, for a trail meant to be read as a sequence of events. */
  sortAscending?: boolean;
  page?: number;
  pageSize?: number;
}

/** The answer from replaying the audit hash chain. */
export interface AuditChainVerdict {
  intact: boolean;
  checkedCount: number;
  firstBrokenSequence: number | null;
  reason: string | null;
}
