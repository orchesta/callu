/** Integration-scoped capture reads/purge, served from /api/v1/captures. */

import { apiClient } from '@/shared/api';
import type { WebhookCaptureDto } from '@/features/services/types/webhook-capture.types';
import { CAPTURE_PAGE_SIZE } from '@/features/services/api/captures.api';

const BASE = '/api/v1/captures';

export const integrationCapturesApi = {
    getByIntegration: (integrationId: string, page: number) =>
        apiClient.get<WebhookCaptureDto[]>(
            `${BASE}/integration/${integrationId}?page=${page}&pageSize=${CAPTURE_PAGE_SIZE}`,
        ),

    getCountByIntegration: (integrationId: string) =>
        apiClient.get<{ count: number }>(`${BASE}/integration/${integrationId}/count`),

    deleteAll: (integrationId: string) =>
        apiClient.delete<{ deletedCount: number }>(`${BASE}/integration/${integrationId}`),
};
