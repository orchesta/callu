/** Integration-scoped capture reads/purge, plus the template dry-run used by parse preview. */

import { apiClient } from '@/shared/api';
import type { WebhookCaptureDto } from '@/features/services/types/webhook-capture.types';
import type { WebhookTemplateTestResult } from '@/features/settings/types/webhook-template.types';

const BASE = '/api/v1/captures';

export const integrationCapturesApi = {
    getByIntegration: (integrationId: string) =>
        apiClient.get<WebhookCaptureDto[]>(`${BASE}/integration/${integrationId}`),

    deleteAll: (integrationId: string) =>
        apiClient.delete<{ deletedCount: number }>(`${BASE}/integration/${integrationId}`),

    testTemplate: (templateId: string, samplePayload: string) =>
        apiClient.post<WebhookTemplateTestResult>(
            `/api/v1/webhook-templates/${templateId}/test`,
            { samplePayload },
        ),
};
