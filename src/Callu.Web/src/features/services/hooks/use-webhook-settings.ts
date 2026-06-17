import { useQuery, useQueryClient } from '@tanstack/react-query';
import { apiQueryOptions, useApiMutation } from '@/shared/api';
import { webhookSettingsApi } from '../api/webhook-settings.api';
import type {
    ServiceWebhookSettingsDto,
    SetSignatureRequest,
    SetSignatureResponse,
} from '../types/webhook-settings.types';
import { serviceKeys } from './use-services';

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

export function useSetProvider() {
    const queryClient = useQueryClient();
    return useApiMutation<ServiceWebhookSettingsDto, { serviceId: string; providerId: string }>(
        ({ serviceId, providerId }) => webhookSettingsApi.setProvider(serviceId, providerId),
        {
            successMessage: 'Provider set',
            onSuccess: (_, { serviceId }) => {
                queryClient.invalidateQueries({ queryKey: webhookSettingsKeys.detail(serviceId) });
                queryClient.invalidateQueries({ queryKey: serviceKeys.detail(serviceId) });
            },
        },
    );
}

export function useDisableWebhook() {
    const queryClient = useQueryClient();
    return useApiMutation<void, string>(
        (serviceId: string) => webhookSettingsApi.disableWebhook(serviceId),
        {
            successMessage: 'Webhook disabled',
            onSuccess: () => {
                queryClient.invalidateQueries({ queryKey: webhookSettingsKeys.all });
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
