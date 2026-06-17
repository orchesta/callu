/**
 * Settings & API Keys React Query hooks.
 */

import { useQuery, useQueryClient } from '@tanstack/react-query';
import { apiQueryOptions, useApiMutation } from '@/shared/api';
import { settingsApi } from '../api/settings.api';
import { webhookApiKeysApi } from '../api/webhook-api-keys.api';
import type {
    UpdateOrganizationSettingsRequest,
    UpdateSmtpSettingsRequest,
} from '../types/settings.types';

export const settingsKeys = {
    all: ['settings'] as const,
    organization: () => [...settingsKeys.all, 'organization'] as const,
    smtp: () => [...settingsKeys.all, 'smtp'] as const,
    timezones: () => [...settingsKeys.all, 'timezones'] as const,
    webhookApiKeys: () => ['webhook-api-keys'] as const,
};

export const settingsQueries = {
    organization: () =>
        apiQueryOptions(settingsKeys.organization(), () => settingsApi.getOrganization(), { staleTime: 5 * 60_000 }),
    smtp: () => apiQueryOptions(settingsKeys.smtp(), () => settingsApi.getSmtp(), { staleTime: 5 * 60_000 }),
    timezones: () => apiQueryOptions(settingsKeys.timezones(), () => settingsApi.getTimezones(), { staleTime: Infinity }),
    webhookApiKeys: () =>
        apiQueryOptions(settingsKeys.webhookApiKeys(), () => webhookApiKeysApi.list(), { staleTime: 30_000 }),
};

export function useOrganizationSettings() {
    return useQuery(settingsQueries.organization());
}

export function useUpdateOrganization() {
    const qc = useQueryClient();
    return useApiMutation(
        (data: UpdateOrganizationSettingsRequest) => settingsApi.updateOrganization(data),
        {
            onSuccess: () =>
                qc.invalidateQueries({ queryKey: settingsKeys.organization() }),
        },
    );
}

export function useSmtpSettings() {
    return useQuery(settingsQueries.smtp());
}

export function useSaveSmtp() {
    const qc = useQueryClient();
    return useApiMutation(
        (data: UpdateSmtpSettingsRequest) => settingsApi.saveSmtp(data),
        {
            onSuccess: () =>
                qc.invalidateQueries({ queryKey: settingsKeys.smtp() }),
        },
    );
}

export function useTestSmtpConnection() {
    return useApiMutation(
        () => settingsApi.testSmtpConnection(),
    );
}

export function useSendTestEmail() {
    return useApiMutation(
        (recipientEmail: string) => settingsApi.sendTestEmail(recipientEmail),
    );
}

export function useTimezones() {
    return useQuery(settingsQueries.timezones());
}

export function useWebhookApiKeys() {
    return useQuery(settingsQueries.webhookApiKeys());
}
