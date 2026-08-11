import { useQuery, useQueryClient } from '@tanstack/react-query';
import { apiQueryOptions, useApiMutation } from '@/shared/api';
import { integrationsApi } from '../api/integrations.api';
import type {
    CreateIntegrationRequest,
    UpdateIntegrationRequest,
} from '../types/integrations.types';

export const integrationKeys = {
    all: ['integrations'] as const,
    lists: () => [...integrationKeys.all, 'list'] as const,
    list: (serviceId?: string) => [...integrationKeys.lists(), { serviceId }] as const,
    detail: (id: string) => [...integrationKeys.all, 'detail', id] as const,
};

export function useIntegrations(serviceId?: string) {
    return useQuery(
        apiQueryOptions(
            integrationKeys.list(serviceId),
            () => integrationsApi.getAll(serviceId),
            { staleTime: 30_000 },
        ),
    );
}

export function useIntegration(id: string | undefined) {
    return useQuery(
        apiQueryOptions(
            integrationKeys.detail(id ?? ''),
            () => integrationsApi.getById(id!),
            { enabled: !!id },
        ),
    );
}

export function useCreateIntegration() {
    const qc = useQueryClient();
    return useApiMutation(
        (data: CreateIntegrationRequest) => integrationsApi.create(data),
        {
            onSuccess: () =>
                qc.invalidateQueries({ queryKey: integrationKeys.lists() }),
        },
    );
}

export function useUpdateIntegration() {
    const qc = useQueryClient();
    return useApiMutation(
        ({ id, ...data }: { id: string } & UpdateIntegrationRequest) =>
            integrationsApi.update(id, data),
        {
            onSuccess: () =>
                qc.invalidateQueries({ queryKey: integrationKeys.all }),
        },
    );
}

export function useDeleteIntegration() {
    const qc = useQueryClient();
    return useApiMutation(
        (id: string) => integrationsApi.delete(id),
        {
            onSuccess: () =>
                qc.invalidateQueries({ queryKey: integrationKeys.lists() }),
        },
    );
}

export function useRotateIntegrationCredentials() {
    const qc = useQueryClient();
    return useApiMutation(
        (id: string) => integrationsApi.rotateCredentials(id),
        {
            onSuccess: () =>
                qc.invalidateQueries({ queryKey: integrationKeys.all }),
        },
    );
}

export function useBindIntegrationService() {
    const qc = useQueryClient();
    return useApiMutation(
        ({ id, serviceId }: { id: string; serviceId: string | null }) =>
            integrationsApi.bindService(id, serviceId),
        {
            onSuccess: () =>
                qc.invalidateQueries({ queryKey: integrationKeys.all }),
        },
    );
}
