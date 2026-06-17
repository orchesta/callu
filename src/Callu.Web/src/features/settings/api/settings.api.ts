/**
 * Settings API module — connects to SettingsController (7 endpoints).
 */

import { apiClient } from '@/shared/api';
import type {
    OrganizationSettingsDto,
    UpdateOrganizationSettingsRequest,
    SmtpSettingsDto,
    UpdateSmtpSettingsRequest,
    SmtpTestResult,
    TimezoneDto,
} from '../types/settings.types';

const BASE = '/api/v1/settings';

export const settingsApi = {
    /** GET /settings/organization */
    getOrganization: () =>
        apiClient.get<OrganizationSettingsDto>(`${BASE}/organization`),

    /** PUT /settings/organization */
    updateOrganization: (data: UpdateOrganizationSettingsRequest) =>
        apiClient.put<OrganizationSettingsDto>(`${BASE}/organization`, data),

    /** GET /settings/smtp */
    getSmtp: () =>
        apiClient.get<SmtpSettingsDto>(`${BASE}/smtp`),

    /** PUT /settings/smtp */
    saveSmtp: (data: UpdateSmtpSettingsRequest) =>
        apiClient.put<{ message: string }>(`${BASE}/smtp`, data),

    /** POST /settings/smtp/test-connection */
    testSmtpConnection: () =>
        apiClient.post<SmtpTestResult>(`${BASE}/smtp/test-connection`),

    /** POST /settings/smtp/test-email */
    sendTestEmail: (recipientEmail: string) =>
        apiClient.post<SmtpTestResult>(`${BASE}/smtp/test-email`, { recipientEmail }),

    /** GET /settings/localization/timezones */
    getTimezones: () =>
        apiClient.get<TimezoneDto[]>(`${BASE}/localization/timezones`),
};
