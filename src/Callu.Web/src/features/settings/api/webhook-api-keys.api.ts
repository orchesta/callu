/**
 * Workspace-wide overview of webhook API keys (one per service that has one).
 * Read-only — keys are created/rotated from the service detail page, not here.
 */

import { apiClient } from '@/shared/api';

export interface WebhookApiKeyOverview {
    serviceId: string;
    serviceName: string;
    hasApiKey: boolean;
    maskedApiKey?: string | null;
    hasSignatureSecret: boolean;
    updatedAt?: string | null;
    webhookEnabled: boolean;
}

const BASE = '/api/v1/webhook-api-keys';

export const webhookApiKeysApi = {
    list: () => apiClient.get<WebhookApiKeyOverview[]>(BASE),
};
