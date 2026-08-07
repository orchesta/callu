/**
 * Alert Rules API module — connects to AlertRulesController
 */

import { apiClient } from '@/shared/api';
import type { AlertRuleDto, CreateAlertRuleRequest, UpdateAlertRuleRequest, AlertRuleMetadata } from '../types/alert-rules.types';

const BASE = '/api/v1/alert-rules';

export const alertRulesApi = {
    /** GET /alert-rules — all rules ordered by priority */
    getAll: () =>
        apiClient.get<AlertRuleDto[]>(BASE),

    /** GET /alert-rules/{id} — single rule */
    getById: (id: string) =>
        apiClient.get<AlertRuleDto>(`${BASE}/${id}`),

    /** POST /alert-rules — create new rule */
    create: (data: CreateAlertRuleRequest) =>
        apiClient.post<AlertRuleDto>(BASE, data),

    /** PUT /alert-rules/{id} — update rule */
    update: (id: string, data: UpdateAlertRuleRequest) =>
        apiClient.put<void>(`${BASE}/${id}`, data),

    /** DELETE /alert-rules/{id} — soft-delete rule */
    delete: (id: string) =>
        apiClient.delete<void>(`${BASE}/${id}`),

    /** POST /alert-rules/{id}/toggle — toggle enabled state */
    toggle: (id: string) =>
        apiClient.post<{ message: string }>(`${BASE}/${id}/toggle`),

    /** GET /alert-rules/metadata — condition fields, operators, action types, severity values */
    getMetadata: () =>
        apiClient.get<AlertRuleMetadata>(`${BASE}/metadata`),
};
