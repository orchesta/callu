/**
 * Webhook Captures API — connects to CapturesController (5 endpoints)
 *
 * Base: /api/v1/captures
 *
 * Endpoints:
 *   GET    /service/{serviceId}         → WebhookCaptureDto[]
 *   GET    /{id}                        → WebhookCaptureDto
 *   POST   /{id}/review                 → { message }
 *   DELETE /{id}                        → 204
 *   DELETE /service/{serviceId}          → { deletedCount }
 */

import { apiClient } from '@/shared/api';
import type { WebhookCaptureDto } from '../types/webhook-capture.types';

const BASE = '/api/v1/captures';

export const capturesApi = {
    getByService: (serviceId: string) =>
        apiClient.get<WebhookCaptureDto[]>(`${BASE}/service/${serviceId}`),

    getById: (id: string) =>
        apiClient.get<WebhookCaptureDto>(`${BASE}/${id}`),

    markAsReviewed: (id: string) =>
        apiClient.post<{ message: string }>(`${BASE}/${id}/review`),

    delete: (id: string) =>
        apiClient.delete<void>(`${BASE}/${id}`),

    deleteAll: (serviceId: string) =>
        apiClient.delete<{ deletedCount: number }>(`${BASE}/service/${serviceId}`),
};
