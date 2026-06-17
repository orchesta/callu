/**
 * Incident Domain Types
 * Mirrors BE DTOs from Callu.Shared.Models.Incidents
 */

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

/**
 * BE: IncidentCreateOutcome enum. The HTTP response shape is `IncidentCreateResult`:
 *   { outcome: "Created" | "Suppressed", incident?: IncidentDto, reason?: string }
 * "Created" → 201; "Suppressed" → 202 Accepted (maintenance window matched).
 */
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
