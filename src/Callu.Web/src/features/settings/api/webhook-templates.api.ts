/**
 * Webhook Templates API — connects to WebhookTemplatesController (7 endpoints).
 */

import { apiClient } from '@/shared/api';
import type {
    WebhookTemplateDto,
    CreateWebhookTemplateRequest,
    UpdateWebhookTemplateRequest,
    WebhookTemplateTestResult,
} from '../types/webhook-template.types';

const BASE = '/api/v1/webhook-templates';

export const webhookTemplateApi = {
    /** GET /webhook-templates */
    getAll: () =>
        apiClient.get<WebhookTemplateDto[]>(BASE),

    /** GET /webhook-templates/{id} */
    getById: (id: string) =>
        apiClient.get<WebhookTemplateDto>(`${BASE}/${id}`),

    /** POST /webhook-templates */
    create: (data: CreateWebhookTemplateRequest) =>
        apiClient.post<WebhookTemplateDto>(BASE, data),

    /** PUT /webhook-templates/{id} */
    update: (id: string, data: UpdateWebhookTemplateRequest) =>
        apiClient.put<void>(`${BASE}/${id}`, data),

    /** DELETE /webhook-templates/{id} */
    delete: (id: string) =>
        apiClient.delete<void>(`${BASE}/${id}`),

    /** POST /webhook-templates/from-capture/{captureId} */
    createFromCapture: (captureId: string, data: CreateWebhookTemplateRequest) =>
        apiClient.post<WebhookTemplateDto>(`${BASE}/from-capture/${captureId}`, data),

    /** POST /webhook-templates/{id}/test */
    test: (id: string, samplePayload: string) =>
        apiClient.post<WebhookTemplateTestResult>(`${BASE}/${id}/test`, { samplePayload }),
};
