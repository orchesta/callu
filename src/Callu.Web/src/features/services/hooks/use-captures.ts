/**
 * Webhook Captures hooks — React Query wrappers for captures.api.ts
 */

import { useQuery, useQueryClient } from '@tanstack/react-query';
import { apiQueryOptions, useApiMutation } from '@/shared/api';
import { capturesApi } from '../api/captures.api';
import { integrationCapturesApi } from '@/features/applications/api/captures.api';

export const captureKeys = {
    all: ['captures'] as const,
    service: (serviceId: string) => [...captureKeys.all, 'service', serviceId] as const,
    byService: (serviceId: string, page: number) =>
        [...captureKeys.service(serviceId), { page }] as const,
    serviceCount: (serviceId: string) => [...captureKeys.service(serviceId), 'count'] as const,
    integration: (integrationId: string) =>
        [...captureKeys.all, 'integration', integrationId] as const,
    byIntegration: (integrationId: string, page: number) =>
        [...captureKeys.integration(integrationId), { page }] as const,
    integrationCount: (integrationId: string) =>
        [...captureKeys.integration(integrationId), 'count'] as const,
    detail: (id: string) => [...captureKeys.all, 'detail', id] as const,
};

export const captureQueries = {
    byService: (serviceId: string, page: number) =>
        apiQueryOptions(captureKeys.byService(serviceId, page), () => capturesApi.getByService(serviceId, page), {
            enabled: !!serviceId,
        }),
    detail: (id: string) =>
        apiQueryOptions(captureKeys.detail(id), () => capturesApi.getById(id), { enabled: !!id }),
};

export function useCapturesByService(serviceId: string, page: number = 1) {
    return useQuery(captureQueries.byService(serviceId, page));
}

export function useServiceCaptureCount(serviceId: string) {
    return useQuery(
        apiQueryOptions(captureKeys.serviceCount(serviceId), () => capturesApi.getCountByService(serviceId), {
            enabled: !!serviceId,
        }),
    );
}

export function useCapturesByIntegration(integrationId: string, page: number = 1) {
    return useQuery(
        apiQueryOptions(
            captureKeys.byIntegration(integrationId, page),
            () => integrationCapturesApi.getByIntegration(integrationId, page),
            { enabled: !!integrationId },
        ),
    );
}

export function useIntegrationCaptureCount(integrationId: string) {
    return useQuery(
        apiQueryOptions(
            captureKeys.integrationCount(integrationId),
            () => integrationCapturesApi.getCountByIntegration(integrationId),
            { enabled: !!integrationId },
        ),
    );
}

export function useCapture(id: string) {
    return useQuery(captureQueries.detail(id));
}

export function useMarkCaptureReviewed() {
    const qc = useQueryClient();
    return useApiMutation(
        (id: string) => capturesApi.markAsReviewed(id),
        {
            successMessage: 'Marked as reviewed',
            onSuccess: () => qc.invalidateQueries({ queryKey: captureKeys.all }),
        },
    );
}

/** Deletion is permanent; the row is gone, not hidden. */
export function useDeleteCapture() {
    const qc = useQueryClient();
    return useApiMutation(
        (id: string) => capturesApi.delete(id),
        {
            successMessage: 'Capture deleted',
            onSuccess: (_, id) => {
                qc.removeQueries({ queryKey: captureKeys.detail(id) });
                qc.invalidateQueries({ queryKey: captureKeys.all });
            },
        },
    );
}

export function useDeleteAllCaptures() {
    const qc = useQueryClient();
    return useApiMutation(
        (serviceId: string) => capturesApi.deleteAll(serviceId),
        {
            successMessage: 'All captures cleared',
            onSuccess: (_, serviceId) =>
                qc.invalidateQueries({ queryKey: captureKeys.service(serviceId) }),
        },
    );
}

export function useDeleteAllIntegrationCaptures() {
    const qc = useQueryClient();
    return useApiMutation(
        (integrationId: string) => integrationCapturesApi.deleteAll(integrationId),
        {
            successMessage: 'All captures cleared',
            onSuccess: (_, integrationId) =>
                qc.invalidateQueries({ queryKey: captureKeys.integration(integrationId) }),
        },
    );
}
