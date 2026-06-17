/**
 * Incident API module — connects to IncidentsController (14 endpoints).
 *
 * Endpoints:
 *   GET    /api/v1/incidents                        → PagedResult<IncidentListItem>
 *   GET    /api/v1/incidents/:id                    → IncidentDto
 *   POST   /api/v1/incidents                        → IncidentDto
 *   PUT    /api/v1/incidents/:id                    → void (204)
 *   DELETE /api/v1/incidents/:id                    → void (204)
 *   POST   /api/v1/incidents/:id/acknowledge        → { message }
 *   POST   /api/v1/incidents/:id/resolve            → { message }
 *   POST   /api/v1/incidents/:id/escalate           → { message }
 *   PUT    /api/v1/incidents/:id/assign              → { message }
 *   GET    /api/v1/incidents/:id/notes              → IncidentNoteDto[]
 *   POST   /api/v1/incidents/:id/notes              → IncidentNoteDto
 *   PUT    /api/v1/incidents/notes/:noteId           → void (204)
 *   DELETE /api/v1/incidents/notes/:noteId           → void (204)
 *   GET    /api/v1/incidents/:id/timeline           → { incident, notes }
 */

import { apiClient } from '@/shared/api/client';
import type { PagedResult } from '@/shared/types/common.types';
import type {
    IncidentDto,
    IncidentListItem,
    IncidentNoteDto,
    IncidentTimelineEvent,
    ActiveConferenceDto,
    IncidentFilter,
    IncidentCreateResult,
    WebhookDeliveryDto,
    CreateIncidentRequest,
    UpdateIncidentRequest,
    CreateIncidentNoteRequest,
    UpdateIncidentNoteRequest,
} from '../types/incident.types';

const BASE = '/api/v1/incidents';

export const incidentApi = {
    /** Get paginated incidents with optional filters */
    getAll: (filter?: IncidentFilter) =>
        apiClient.get<PagedResult<IncidentListItem>>(BASE, {
            params: filter as Record<string, string | number | boolean | undefined>,
        }),

    /** Get single incident by ID */
    getById: (id: string) =>
        apiClient.get<IncidentDto>(`${BASE}/${id}`),

    /**
     * Create a new incident. Returns the IncidentCreateResult envelope:
     * outcome="Created" with the incident dto, or outcome="Suppressed"
     * (HTTP 202) when an active maintenance window absorbed the alert and no
     * row was written. Callers should branch on `outcome` to decide whether
     * to navigate to the new incident or surface a "suppressed by maintenance"
     * toast. Fix 02.G4 / 11.G12.
     */
    create: (data: CreateIncidentRequest) =>
        apiClient.post<IncidentCreateResult>(`${BASE}`, data),

    /** Update an existing incident */
    update: (id: string, data: UpdateIncidentRequest) =>
        apiClient.put<void>(`${BASE}/${id}`, data),

    /** Delete (soft) an incident */
    delete: (id: string) =>
        apiClient.delete<void>(`${BASE}/${id}`),

    /** Acknowledge an incident */
    acknowledge: (id: string) =>
        apiClient.post<{ message: string }>(`${BASE}/${id}/acknowledge`),

    /** Resolve an incident */
    resolve: (id: string) =>
        apiClient.post<{ message: string }>(`${BASE}/${id}/resolve`),

    /** Close a resolved incident (terminal — cannot be transitioned further). */
    close: (id: string) =>
        apiClient.post<{ message: string }>(`${BASE}/${id}/close`),

    /** Reopen a resolved or closed incident back to Open. */
    reopen: (id: string) =>
        apiClient.post<{ message: string }>(`${BASE}/${id}/reopen`),

    /** Manually escalate an incident */
    escalate: (id: string, reason?: string) =>
        apiClient.post<{ message: string }>(`${BASE}/${id}/escalate`, { reason }),

    /** Reassign an incident to a different user */
    reassign: (id: string, targetUserId: string) =>
        apiClient.put<{ message: string }>(`${BASE}/${id}/assign`, { targetUserId }),

    /** Bulk acknowledge multiple incidents */
    bulkAcknowledge: (incidentIds: string[]) =>
        apiClient.post<{ succeeded: number; failed: number; total: number }>(`${BASE}/bulk/acknowledge`, { incidentIds }),

    /** Bulk resolve multiple incidents */
    bulkResolve: (incidentIds: string[]) =>
        apiClient.post<{ succeeded: number; failed: number; total: number }>(`${BASE}/bulk/resolve`, { incidentIds }),

    /** Get all notes for an incident */
    getNotes: (incidentId: string) =>
        apiClient.get<IncidentNoteDto[]>(`${BASE}/${incidentId}/notes`),

    /** Add a note to an incident */
    addNote: (incidentId: string, data: CreateIncidentNoteRequest) =>
        apiClient.post<IncidentNoteDto>(`${BASE}/${incidentId}/notes`, data),

    /** Update a note */
    updateNote: (noteId: string, data: UpdateIncidentNoteRequest) =>
        apiClient.put<void>(`${BASE}/notes/${noteId}`, data),

    /** Delete a note */
    deleteNote: (noteId: string) =>
        apiClient.delete<void>(`${BASE}/notes/${noteId}`),

    /** Get incident timeline (incident + notes composite) */
    getTimeline: (id: string) =>
        apiClient.get<{ incident: IncidentDto; notes: IncidentNoteDto[]; events: IncidentTimelineEvent[] }>(`${BASE}/${id}/timeline`),

    /** Get active video conference link if any exists */
    getActiveConference: (id: string) =>
        apiClient.get<ActiveConferenceDto | null>(`${BASE}/${id}/conference`),

    /** Outbound webhook delivery history (ACK callbacks). Newest first. */
    getWebhookDeliveries: (id: string, limit = 20) =>
        apiClient.get<WebhookDeliveryDto[]>(`${BASE}/${id}/webhook-deliveries?limit=${limit}`),
};
