/** Incident CRUD, lifecycle actions, notes and timeline endpoints under /api/v1/incidents. */

import { apiClient } from '@/shared/api/client';
import type { ApiResponse, PagedResult } from '@/shared/types/common.types';
import type {
    IncidentDto,
    IncidentListItem,
    IncidentNoteDto,
    IncidentTimelineEvent,
    ActiveConferenceDto,
    IncidentFilter,
    IncidentCreateResult,
    WebhookDeliveryDto,
    IncidentEscalation,
    CreateIncidentRequest,
    UpdateIncidentRequest,
    CreateIncidentNoteRequest,
    UpdateIncidentNoteRequest,
} from '../types/incident.types';

const BASE = '/api/v1/incidents';

/** Reads one create response, in either the current envelope or the older bare-incident 201. */
// The API now returns the envelope for both outcomes. This stays because the SPA and the API are
// separate containers that restart independently, so a new SPA can briefly talk to an older API.
export function normalizeCreateResult(body: IncidentCreateResult | IncidentDto): IncidentCreateResult {
    return 'outcome' in body ? body : { outcome: 'Created', incident: body };
}

export const incidentApi = {
    /** Get paginated incidents with optional filters */
    getAll: (filter?: IncidentFilter) =>
        apiClient.get<PagedResult<IncidentListItem>>(BASE, {
            params: filter as Record<string, string | number | boolean | undefined>,
        }),

    /** Get single incident by ID */
    getById: (id: string) =>
        apiClient.get<IncidentDto>(`${BASE}/${id}`),

    /** Creates an incident, returning an envelope callers must branch on: "Suppressed" means a
     * maintenance window absorbed the alert and no row was written. */
    create: async (data: CreateIncidentRequest): Promise<ApiResponse<IncidentCreateResult>> => {
        const res = await apiClient.post<IncidentCreateResult | IncidentDto>(`${BASE}`, data);
        if (!res.success || !res.data) return res as ApiResponse<IncidentCreateResult>;
        return { ...res, data: normalizeCreateResult(res.data) };
    },

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

    /** The escalation run: every step, which one it is on, and when the next is due. */
    getEscalation: (id: string) =>
        apiClient.get<IncidentEscalation>(`${BASE}/${id}/escalation`),
};
