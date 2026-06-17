/**
 * Email Templates API — EmailTemplatesController (7 endpoints).
 *
 * Route: /api/v1/email-templates
 */

import { apiClient } from '@/shared/api';
import type {
    EmailTemplateDto,
    EmailTemplateDetailDto,
    CreateEmailTemplateRequest,
    UpdateEmailTemplateRequest,
} from '../types/email-template.types';

const BASE = '/api/v1/email-templates';

export const emailTemplateApi = {
    /** GET / — List all templates */
    getAll: () => apiClient.get<EmailTemplateDto[]>(BASE),

    /** GET /{id} — Get template with full body */
    getById: (id: string) => apiClient.get<EmailTemplateDetailDto>(`${BASE}/${id}`),

    /** POST / — Create new template */
    create: (data: CreateEmailTemplateRequest) =>
        apiClient.post<EmailTemplateDto>(BASE, data),

    /** PUT /{id} — Update template */
    update: (id: string, data: UpdateEmailTemplateRequest) =>
        apiClient.put<void>(`${BASE}/${id}`, data),

    /** DELETE /{id} — Delete template (not system templates) */
    delete: (id: string) => apiClient.delete<void>(`${BASE}/${id}`),

    /** POST /{id}/preview — Preview template with variables */
    preview: (id: string, variables: Record<string, string>) =>
        apiClient.post<{ html: string }>(`${BASE}/${id}/preview`, { variables }),

    /** POST /{id}/send-test — Send test email */
    sendTest: (id: string, email: string) =>
        apiClient.post<void>(`${BASE}/${id}/send-test`, { email }),
};
