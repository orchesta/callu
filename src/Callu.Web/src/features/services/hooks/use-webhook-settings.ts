import { useQuery, useQueryClient } from '@tanstack/react-query';
import { apiQueryOptions, useApiMutation } from '@/shared/api';
import { webhookSettingsApi } from '../api/webhook-settings.api';
import type {
    ServiceWebhookSettingsDto,
    SetSignatureRequest,
    SetSignatureResponse,
} from '../types/webhook-settings.types';

export const webhookSettingsKeys = {
    all: ['webhook-settings'] as const,
    detail: (serviceId: string) => [...webhookSettingsKeys.all, serviceId] as const,
};

export const webhookSettingsQueries = {
    detail: (serviceId: string, enablePolling = false) =>
        apiQueryOptions<ServiceWebhookSettingsDto>(
            webhookSettingsKeys.detail(serviceId),
            () => webhookSettingsApi.getSettings(serviceId),
            {
                enabled: !!serviceId,
                staleTime: enablePolling ? 0 : 15_000,
                refetchInterval: enablePolling ? 3_000 : false,
            },
        ),
};

export function useWebhookSettings(serviceId: string, enablePolling = false) {
    return useQuery(webhookSettingsQueries.detail(serviceId, enablePolling));
}

/** Clears the service's webhook: the URL stops resolving and listening mode goes off with it. */
export function useDisableWebhook() {
    const qc = useQueryClient();
    return useApiMutation(
        (serviceId: string) => webhookSettingsApi.disableWebhook(serviceId),
        {
            onSuccess: (_, serviceId) => {
                qc.invalidateQueries({ queryKey: webhookSettingsKeys.detail(serviceId) });
                qc.invalidateQueries({ queryKey: ['services'] });
            },
        },
    );
}

/** Points the service at a saved template, or back at the default parsing when given null. */
export function useSetWebhookTemplate() {
    const qc = useQueryClient();
    return useApiMutation(
        ({ serviceId, templateId }: { serviceId: string; templateId: string | null }) =>
            webhookSettingsApi.setTemplate(serviceId, templateId),
        {
            onSuccess: (_, { serviceId }) => {
                qc.invalidateQueries({ queryKey: webhookSettingsKeys.detail(serviceId) });
                qc.invalidateQueries({ queryKey: ['webhook-templates'] });
            },
        },
    );
}

export function useRegenerateToken() {
    const queryClient = useQueryClient();
    return useApiMutation<{ token: string }, string>(
        (serviceId: string) => webhookSettingsApi.regenerateToken(serviceId),
        {
            successMessage: 'Token regenerated',
            onSuccess: () => {
                queryClient.invalidateQueries({ queryKey: webhookSettingsKeys.all });
            },
        },
    );
}

export function useRegenerateApiKey() {
    const queryClient = useQueryClient();
    return useApiMutation<{ apiKey: string }, string>(
        (serviceId: string) => webhookSettingsApi.regenerateApiKey(serviceId),
        {
            successMessage: 'API key regenerated',
            onSuccess: () => {
                queryClient.invalidateQueries({ queryKey: webhookSettingsKeys.all });
            },
        },
    );
}

export function useToggleListeningMode() {
    const queryClient = useQueryClient();
    return useApiMutation<{ listeningMode: boolean }, { serviceId: string; enabled: boolean }>(
        ({ serviceId, enabled }) => webhookSettingsApi.toggleListeningMode(serviceId, enabled),
        {
            onSuccess: (_, { serviceId }) => {
                queryClient.invalidateQueries({ queryKey: webhookSettingsKeys.detail(serviceId) });
            },
        },
    );
}

export function useSetSignature() {
    const queryClient = useQueryClient();
    return useApiMutation<SetSignatureResponse, { serviceId: string; body: SetSignatureRequest }>(
        ({ serviceId, body }) => webhookSettingsApi.setSignature(serviceId, body),
        {
            successMessage: 'Signature secret set — copy it now, it will not be shown again',
            onSuccess: (_, { serviceId }) => {
                queryClient.invalidateQueries({ queryKey: webhookSettingsKeys.detail(serviceId) });
            },
        },
    );
}

export function useClearSignature() {
    const queryClient = useQueryClient();
    return useApiMutation<void, string>(
        (serviceId: string) => webhookSettingsApi.clearSignature(serviceId),
        {
            successMessage: 'Signature secret cleared',
            onSuccess: (_, serviceId) => {
                queryClient.invalidateQueries({ queryKey: webhookSettingsKeys.detail(serviceId) });
            },
        },
    );
}
