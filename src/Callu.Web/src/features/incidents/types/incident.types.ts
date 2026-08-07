/**
 * Incident Domain Types
 * Mirrors BE DTOs from Callu.Shared.Models.Incidents
 */

import type { EscalationExhaustionBehavior } from '@/features/escalations/types/escalation.types';

export enum IncidentSeverity {
  Critical = 'Critical',
  High = 'High',
  Medium = 'Medium',
  Low = 'Low',
}

export enum IncidentStatus {
  Open = 'Open',
  Acknowledged = 'Acknowledged',
  Investigating = 'Investigating',
  Mitigated = 'Mitigated',
  Resolved = 'Resolved',
  Closed = 'Closed',
}

/** Lightweight incident for list views (BE: IncidentListItemDto) */
export interface IncidentListItem {
  id: string;
  title: string;
  severity: string;
  status: string;
  startedAt: string;
  acknowledgedAt?: string;
  resolvedAt?: string;
  serviceName?: string;
  teamName?: string;
  acknowledgedBy?: string;
  resolvedBy?: string;
}

/** Full incident detail (BE: IncidentDto extends IncidentListItemDto) */
export interface IncidentDto extends IncidentListItem {
  description?: string;
  serviceId?: string;
  teamId?: string;
  createdAt: string;
}

/** Incident note (BE: IncidentNoteDto) */
export interface IncidentNoteDto {
  id: string;
  incidentId: string;
  content: string;
  isInternal: boolean;
  isPinned: boolean;
  createdBy?: string;
  /** Author's display name; absent when the user can no longer be resolved. */
  createdByName?: string;
  createdAt: string;
  updatedAt?: string;
}

export interface ActiveConferenceDto {
  roomId: string;
  roomToken?: string;
  status: string;
  participantCount: number;
  userParticipantToken?: string;
  expiresAt: string;
}

/** BE: CreateIncidentRequest */
export interface CreateIncidentRequest {
  title: string;
  description?: string;
  severity: string;
  serviceId?: string;
  teamId?: string;
  externalAlertId?: string;
}

/** Mirrors the backend IncidentCreateOutcome: "Created" answers 201, "Suppressed" answers 202 when a
 * maintenance window matched and nothing was written. */
export type IncidentCreateOutcome = 'Created' | 'Suppressed';

/** BE: IncidentCreateResult — envelope the controller returns from POST /incidents. */
export interface IncidentCreateResult {
  outcome: IncidentCreateOutcome;
  incident: IncidentDto | null;
  reason?: string;
}

/**
 * BE: WebhookDeliveryDto. One row per outbound ACK attempt (incident
 * acknowledge/resolve callback). Status: Pending | Succeeded | Failed | Retrying.
 */
export interface WebhookDeliveryDto {
  id: string;
  incidentId: string;
  serviceId?: string;
  url: string;
  ackType?: string;
  httpStatus?: number;
  error?: string;
  attemptCount: number;
  attemptedAt: string;
  nextRetryAt?: string;
  status: 'Pending' | 'Succeeded' | 'Failed' | 'Retrying';
  responseBodySample?: string;
}

/** BE: UpdateIncidentRequest */
export interface UpdateIncidentRequest {
  title?: string;
  description?: string;
  severity?: string;
  status?: string;
  serviceId?: string;
  teamId?: string;
}

/** BE: CreateIncidentNoteRequest */
export interface CreateIncidentNoteRequest {
  content: string;
  isInternal?: boolean;
}

/** BE: UpdateIncidentNoteRequest */
export interface UpdateIncidentNoteRequest {
  content: string;
  isPinned?: boolean;
}

/** Mirrors BE: IncidentFilter */
export interface IncidentFilter {
  status?: string;
  severity?: string;
  serviceId?: string;
  teamId?: string;
  searchQuery?: string;
  page?: number;
  pageSize?: number;
}

export interface IncidentMetrics {
  open: number;
  acknowledged: number;
  resolved: number;
  healthRate: number;
  mtta: string;
  mttr: string;
}

export interface IncidentTimelineEvent {
  id: string;
  incidentId: string;
  eventType: string;
  title: string;
  description?: string;
  actorName?: string;
  createdAt: string;
}

/** Mirrors BE: IncidentEscalationRunState */
export type IncidentEscalationRunState =
  | 'NotConfigured'
  | 'Waiting'
  | 'Running'
  | 'Stopped'
  | 'Exhausted';

/** Mirrors BE: IncidentEscalationStepState */
export type IncidentEscalationStepState = 'Pending' | 'Current' | 'Passed';

/** Mirrors BE: IncidentEscalationStepDto */
export interface IncidentEscalationStep {
  id: string;
  level: number;
  title: string;
  delayMinutes: number;
  scheduleId?: string | null;
  scheduleName?: string | null;
  teamId?: string | null;
  teamName?: string | null;
  notifyAllTeamMembers: boolean;
  notifyBothOnCall: boolean;
  notifyUserNames: string[];
  state: IncidentEscalationStepState;
  /** Null on a passed step means the run predates the timeline carrying the step id. */
  pagedAt?: string | null;
}

/** Mirrors BE: IncidentEscalationDto */
export interface IncidentEscalation {
  policyId?: string | null;
  policyName?: string | null;
  runState: IncidentEscalationRunState;
  startedAt?: string | null;
  currentStepId?: string | null;
  /** Computed server-side; never re-derive it from delayMinutes here. */
  nextStepDueAt?: string | null;
  /** Decides whether the last step really is the last page, or the run starts over. */
  exhaustionBehavior: EscalationExhaustionBehavior;
  maxRepeatCycles: number;
  cyclesCompleted: number;
  steps: IncidentEscalationStep[];
}
