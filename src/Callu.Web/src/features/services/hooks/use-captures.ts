/**
 * Webhook Captures hooks — React Query wrappers for captures.api.ts
 */

import { useQuery, useQueryClient } from '@tanstack/react-query';
import { apiQueryOptions, useApiMutation } from '@/shared/api';
import { capturesApi } from '../api/captures.api';

export const captureKeys = {
    all: ['captures'] as const,
    byService: (serviceId: string) => [...captureKeys.all, 'service', serviceId] as const,
    detail: (id: string) => [...captureKeys.all, 'detail', id] as const,
};

export const captureQueries = {
    byService: (serviceId: string) =>
        apiQueryOptions(captureKeys.byService(serviceId), () => capturesApi.getByService(serviceId), {
            enabled: !!serviceId,
        }),
    detail: (id: string) =>
        apiQueryOptions(captureKeys.detail(id), () => capturesApi.getById(id), { enabled: !!id }),
};

export function useCapturesByService(serviceId: string) {
    return useQuery(captureQueries.byService(serviceId));
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

export function useDeleteCapture() {
    return useApiMutation(
        (id: string) => capturesApi.delete(id),
        { successMessage: 'Capture deleted' },
    );
}

export function useDeleteAllCaptures() {
    return useApiMutation(
        (serviceId: string) => capturesApi.deleteAll(serviceId),
        { successMessage: 'All captures cleared' },
    );
}
