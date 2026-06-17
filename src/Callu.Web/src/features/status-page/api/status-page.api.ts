/**
 * Status Pages API — StatusPagesController.
 *
 * Route: /api/v1/status-pages
 */

import { apiClient } from '@/shared/api';
import type {
    StatusPageDto,
    StatusPageDetailDto,
    StatusPageIncidentDto,
    StatusPageStatsDto,
    StatusPageSubscriberDto,
    HealthCheckResultDto,
    HealthCheckSnifferResultDto,
    ComponentUptimeDto,
    CreateStatusPageRequest,
    UpdateStatusPageRequest,
    AddComponentRequest,
    UpdateComponentRequest,
    CreateStatusIncidentRequest,
    AddIncidentUpdateRequest,
} from '../types/status-page.types';

const BASE = '/api/v1/status-pages';

export const statusPageApi = {
    /** GET / */
    getAll: () => apiClient.get<StatusPageDto[]>(BASE),

    /** GET /{id} */
    getById: (id: string) => apiClient.get<StatusPageDetailDto>(`${BASE}/${id}`),

    /** GET /slug/{slug} (anonymous) */
    getBySlug: (slug: string) => apiClient.get<StatusPageDetailDto>(`${BASE}/slug/${slug}`),

    /** POST / */
    create: (data: CreateStatusPageRequest) => apiClient.post<StatusPageDto>(BASE, data),

    /** PUT /{id} */
    update: (id: string, data: UpdateStatusPageRequest) =>
        apiClient.put<void>(`${BASE}/${id}`, data),

    /** DELETE /{id} */
    delete: (id: string) => apiClient.delete<void>(`${BASE}/${id}`),

    /** POST /{pageId}/components */
    addComponent: (pageId: string, data: AddComponentRequest) =>
        apiClient.post<void>(`${BASE}/${pageId}/components`, data),

    /** PUT /components/{componentId} */
    updateComponent: (componentId: string, data: UpdateComponentRequest) =>
        apiClient.put<void>(`${BASE}/components/${componentId}`, data),

    /** DELETE /components/{componentId} */
    removeComponent: (componentId: string) =>
        apiClient.delete<void>(`${BASE}/components/${componentId}`),

    /** POST /components/{componentId}/health-check/test */
    testHealthCheck: (componentId: string) =>
        apiClient.post<HealthCheckResultDto>(`${BASE}/components/${componentId}/health-check/test`),

    /** POST /components/{componentId}/health-check/sniff */
    sniffHealthCheck: (componentId: string) =>
        apiClient.post<HealthCheckSnifferResultDto>(`${BASE}/components/${componentId}/health-check/sniff`),

    /** POST /{pageId}/incidents */
    createIncident: (pageId: string, data: CreateStatusIncidentRequest) =>
        apiClient.post<StatusPageIncidentDto>(`${BASE}/${pageId}/incidents`, data),

    /** POST /incidents/{incidentId}/updates */
    addUpdate: (incidentId: string, data: AddIncidentUpdateRequest) =>
        apiClient.post<void>(`${BASE}/incidents/${incidentId}/updates`, data),

    /** POST /incidents/{incidentId}/notify-subscribers — manual, explicit subscriber email */
    notifySubscribers: (incidentId: string) =>
        apiClient.post<{ message: string }>(`${BASE}/incidents/${incidentId}/notify-subscribers`),

    /** GET /{pageId}/stats */
    getStats: (pageId: string) =>
        apiClient.get<StatusPageStatsDto>(`${BASE}/${pageId}/stats`),

    /** GET /{pageId}/uptime?days=30 */
    getUptime: (pageId: string, days = 30) =>
        apiClient.get<ComponentUptimeDto[]>(`${BASE}/${pageId}/uptime?days=${days}`),

    /** POST /{pageId}/view */
    recordView: (pageId: string) =>
        apiClient.post<void>(`${BASE}/${pageId}/view`),

    /** POST /{pageId}/subscribe */
    subscribe: (pageId: string, email: string) =>
        apiClient.post<{ message: string }>(`${BASE}/${pageId}/subscribe`, { email }),

    /** DELETE /{pageId}/subscribe */
    unsubscribe: (pageId: string, email: string) =>
        apiClient.delete<{ message: string }>(`${BASE}/${pageId}/subscribe?email=${encodeURIComponent(email)}`),

    /** GET /subscriptions/confirm?token=... — landing page handler for the double opt-in email link. */
    confirmSubscription: (token: string) =>
        apiClient.get<{ message: string }>(`${BASE}/subscriptions/confirm?token=${encodeURIComponent(token)}`),

    /** GET /subscriptions/unsubscribe?token=... — one-click unsubscribe target. */
    unsubscribeByToken: (token: string) =>
        apiClient.get<{ message: string }>(`${BASE}/subscriptions/unsubscribe?token=${encodeURIComponent(token)}`),

    /** GET /{pageId}/subscribers */
    getSubscribers: (pageId: string) =>
        apiClient.get<StatusPageSubscriberDto[]>(`${BASE}/${pageId}/subscribers`),

    /** DELETE /{pageId}/subscribers/{email} */
    removeSubscriber: (pageId: string, email: string) =>
        apiClient.delete<void>(`${BASE}/${pageId}/subscribers/${encodeURIComponent(email)}`),
};
