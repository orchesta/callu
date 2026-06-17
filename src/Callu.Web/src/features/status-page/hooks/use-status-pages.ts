/**
 * Status Pages React Query hooks.
 */

import { useQuery, useQueryClient } from '@tanstack/react-query';
import { apiQueryOptions, useApiMutation } from '@/shared/api';
import { statusPageApi } from '../api/status-page.api';
import type {
    CreateStatusPageRequest,
    UpdateStatusPageRequest,
    AddComponentRequest,
    UpdateComponentRequest,
    CreateStatusIncidentRequest,
    AddIncidentUpdateRequest,
} from '../types/status-page.types';

export const statusPageKeys = {
    all: ['status-pages'] as const,
    list: () => [...statusPageKeys.all, 'list'] as const,
    detail: (id: string) => [...statusPageKeys.all, 'detail', id] as const,
    slug: (slug: string) => [...statusPageKeys.all, 'slug', slug] as const,
};

export const statusPageQueries = {
    list: () => apiQueryOptions(statusPageKeys.list(), () => statusPageApi.getAll(), { staleTime: 2 * 60_000 }),
    detail: (id: string) =>
        apiQueryOptions(statusPageKeys.detail(id), () => statusPageApi.getById(id), { enabled: !!id }),
    bySlug: (slug: string) =>
        apiQueryOptions(statusPageKeys.slug(slug), () => statusPageApi.getBySlug(slug), { enabled: !!slug }),
    stats: (pageId: string | undefined) =>
        apiQueryOptions([...statusPageKeys.all, 'stats', pageId] as const, () => statusPageApi.getStats(pageId!), {
            enabled: !!pageId,
            staleTime: 60_000,
        }),
    uptime: (pageId: string | undefined, days = 30) =>
        apiQueryOptions([...statusPageKeys.all, 'uptime', pageId, days] as const, () => statusPageApi.getUptime(pageId!, days), {
            enabled: !!pageId,
            staleTime: 5 * 60_000,
        }),
    subscribers: (pageId: string | undefined) =>
        apiQueryOptions([...statusPageKeys.all, 'subscribers', pageId] as const, () => statusPageApi.getSubscribers(pageId!), {
            enabled: !!pageId,
            staleTime: 2 * 60_000,
        }),
};

export function useStatusPages() {
    return useQuery(statusPageQueries.list());
}

export function useStatusPage(id: string) {
    return useQuery(statusPageQueries.detail(id));
}

export function useStatusPageBySlug(slug: string) {
    return useQuery(statusPageQueries.bySlug(slug));
}

export function useCreateStatusPage() {
    const qc = useQueryClient();
    return useApiMutation(
        (data: CreateStatusPageRequest) => statusPageApi.create(data),
        { onSuccess: () => qc.invalidateQueries({ queryKey: statusPageKeys.list() }) },
    );
}

export function useUpdateStatusPage() {
    const qc = useQueryClient();
    return useApiMutation(
        ({ id, ...data }: { id: string } & UpdateStatusPageRequest) =>
            statusPageApi.update(id, data),
        {
            onSuccess: (_, { id }) => {
                qc.invalidateQueries({ queryKey: statusPageKeys.list() });
                qc.invalidateQueries({ queryKey: statusPageKeys.detail(id) });
            },
        },
    );
}

export function useDeleteStatusPage() {
    const qc = useQueryClient();
    return useApiMutation(
        (id: string) => statusPageApi.delete(id),
        { onSuccess: () => qc.invalidateQueries({ queryKey: statusPageKeys.list() }) },
    );
}

export function useAddComponent() {
    const qc = useQueryClient();
    return useApiMutation(
        ({ pageId, ...data }: { pageId: string } & AddComponentRequest) =>
            statusPageApi.addComponent(pageId, data),
        { onSuccess: () => qc.invalidateQueries({ queryKey: statusPageKeys.all }) },
    );
}

export function useUpdateComponent() {
    const qc = useQueryClient();
    return useApiMutation(
        ({ componentId, ...data }: { componentId: string } & UpdateComponentRequest) =>
            statusPageApi.updateComponent(componentId, data),
        { onSuccess: () => qc.invalidateQueries({ queryKey: statusPageKeys.all }) },
    );
}

export function useRemoveComponent() {
    const qc = useQueryClient();
    return useApiMutation(
        (componentId: string) => statusPageApi.removeComponent(componentId),
        { onSuccess: () => qc.invalidateQueries({ queryKey: statusPageKeys.all }) },
    );
}

export function useTestHealthCheck() {
    const qc = useQueryClient();
    return useApiMutation(
        (componentId: string) => statusPageApi.testHealthCheck(componentId),
        { onSuccess: () => qc.invalidateQueries({ queryKey: statusPageKeys.all }) },
    );
}

export function useSniffHealthCheck() {
    const qc = useQueryClient();
    return useApiMutation(
        (componentId: string) => statusPageApi.sniffHealthCheck(componentId),
        { onSuccess: () => qc.invalidateQueries({ queryKey: statusPageKeys.all }) },
    );
}

export function useCreateStatusIncident() {
    const qc = useQueryClient();
    return useApiMutation(
        ({ pageId, ...data }: { pageId: string } & CreateStatusIncidentRequest) =>
            statusPageApi.createIncident(pageId, data),
        { onSuccess: () => qc.invalidateQueries({ queryKey: statusPageKeys.all }) },
    );
}

export function useAddIncidentUpdate() {
    const qc = useQueryClient();
    return useApiMutation(
        ({ incidentId, ...data }: { incidentId: string } & AddIncidentUpdateRequest) =>
            statusPageApi.addUpdate(incidentId, data),
        { onSuccess: () => qc.invalidateQueries({ queryKey: statusPageKeys.all }) },
    );
}

export function useStatusPageStats(pageId: string | undefined) {
    return useQuery(statusPageQueries.stats(pageId));
}

export function useStatusPageUptime(pageId: string | undefined, days = 30) {
    return useQuery(statusPageQueries.uptime(pageId, days));
}

export function useSubscribeToStatusPage() {
    return useApiMutation(
        ({ pageId, email }: { pageId: string; email: string }) =>
            statusPageApi.subscribe(pageId, email),
        { successMessage: 'Subscribed successfully' },
    );
}

export function useUnsubscribeFromStatusPage() {
    return useApiMutation(
        ({ pageId, email }: { pageId: string; email: string }) =>
            statusPageApi.unsubscribe(pageId, email),
    );
}

export function useStatusPageSubscribers(pageId: string | undefined) {
    return useQuery(statusPageQueries.subscribers(pageId));
}

export function useRemoveSubscriber() {
    const qc = useQueryClient();
    return useApiMutation(
        ({ pageId, email }: { pageId: string; email: string }) =>
            statusPageApi.removeSubscriber(pageId, email),
        {
            onSuccess: () => qc.invalidateQueries({ queryKey: [...statusPageKeys.all, 'subscribers'] }),
            successMessage: 'Subscriber removed',
        },
    );
}
