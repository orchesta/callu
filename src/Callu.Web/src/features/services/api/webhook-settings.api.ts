import { apiClient } from '@/shared/api';
import type {
    ServiceWebhookSettingsDto,
    SetSignatureRequest,
    SetSignatureResponse,
} from '../types/webhook-settings.types';

const BASE = '/api/v1/services';

export const webhookSettingsApi = {
    getSettings: (serviceId: string) =>
        apiClient.get<ServiceWebhookSettingsDto>(`${BASE}/${serviceId}/webhook-settings`),

    setProvider: (serviceId: string, providerId: string) =>
        apiClient.post<ServiceWebhookSettingsDto>(`${BASE}/${serviceId}/webhook-settings/provider`, { providerId }),

    disableWebhook: (serviceId: string) =>
        apiClient.delete<void>(`${BASE}/${serviceId}/webhook-settings/provider`),

    regenerateToken: (serviceId: string) =>
        apiClient.post<{ token: string }>(`${BASE}/${serviceId}/webhook-settings/regenerate-token`),

    regenerateApiKey: (serviceId: string) =>
        apiClient.post<{ apiKey: string }>(`${BASE}/${serviceId}/webhook-settings/regenerate-api-key`),

    toggleListeningMode: (serviceId: string, enabled: boolean) =>
        apiClient.post<{ listeningMode: boolean }>(`${BASE}/${serviceId}/webhook-settings/listening-mode`, { enabled }),

    setTemplate: (serviceId: string, templateId: string | null) =>
        apiClient.put<{ templateId: string | null }>(`${BASE}/${serviceId}/webhook-settings/template`, { templateId }),

    /** Set HMAC signature secret + header. Returns the plaintext exactly once. */
    setSignature: (serviceId: string, body: SetSignatureRequest) =>
        apiClient.post<SetSignatureResponse>(`${BASE}/${serviceId}/webhook-settings/signature`, body),

    /** Clear the HMAC signature configuration. */
    clearSignature: (serviceId: string) =>
        apiClient.delete<void>(`${BASE}/${serviceId}/webhook-settings/signature`),
};
