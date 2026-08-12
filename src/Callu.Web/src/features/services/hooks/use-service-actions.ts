import { useQuery, useQueryClient } from '@tanstack/react-query';
import { apiQueryOptions, useApiMutation } from '@/shared/api';
import { serviceActionsApi } from '../api/service-actions.api';
import type {
    CreateServiceActionRequest,
    UpdateServiceActionRequest,
} from '../types/service.types';

export const serviceActionKeys = {
    all: ['service-actions'] as const,
    list: (serviceId: string) => [...serviceActionKeys.all, serviceId] as const,
};

export function useServiceActions(serviceId: string) {
    return useQuery(
        apiQueryOptions(
            serviceActionKeys.list(serviceId),
            () => serviceActionsApi.list(serviceId),
            { enabled: !!serviceId },
        ),
    );
}

export function useCreateServiceAction(serviceId: string) {
    const qc = useQueryClient();
    return useApiMutation(
        (data: CreateServiceActionRequest) => serviceActionsApi.create(serviceId, data),
        {
            onSuccess: () =>
                qc.invalidateQueries({ queryKey: serviceActionKeys.list(serviceId) }),
        },
    );
}

export function useUpdateServiceAction(serviceId: string) {
    const qc = useQueryClient();
    return useApiMutation(
        ({ actionId, ...data }: { actionId: string } & UpdateServiceActionRequest) =>
            serviceActionsApi.update(serviceId, actionId, data),
        {
            onSuccess: () =>
                qc.invalidateQueries({ queryKey: serviceActionKeys.list(serviceId) }),
        },
    );
}

export function useDeleteServiceAction(serviceId: string) {
    const qc = useQueryClient();
    return useApiMutation(
        (actionId: string) => serviceActionsApi.remove(serviceId, actionId),
        {
            onSuccess: () =>
                qc.invalidateQueries({ queryKey: serviceActionKeys.list(serviceId) }),
        },
    );
}
