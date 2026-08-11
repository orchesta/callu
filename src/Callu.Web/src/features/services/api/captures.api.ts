/** Webhook capture reads, review and purge, served from /api/v1/captures. */

import { apiClient } from '@/shared/api';
import type { WebhookCaptureDto } from '../types/webhook-capture.types';

const BASE = '/api/v1/captures';

export const CAPTURE_PAGE_SIZE = 50;

export const capturesApi = {
    getByService: (serviceId: string, page: number) =>
        apiClient.get<WebhookCaptureDto[]>(
            `${BASE}/service/${serviceId}?page=${page}&pageSize=${CAPTURE_PAGE_SIZE}`,
        ),

    getCountByService: (serviceId: string) =>
        apiClient.get<{ count: number }>(`${BASE}/service/${serviceId}/count`),

    getById: (id: string) =>
        apiClient.get<WebhookCaptureDto>(`${BASE}/${id}`),

    markAsReviewed: (id: string) =>
        apiClient.post<{ message: string }>(`${BASE}/${id}/review`),

    delete: (id: string) =>
        apiClient.delete<void>(`${BASE}/${id}`),

    deleteAll: (serviceId: string) =>
        apiClient.delete<{ deletedCount: number }>(`${BASE}/service/${serviceId}`),
};
