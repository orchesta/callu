import { useQuery, useQueryClient } from '@tanstack/react-query';
import { apiQueryOptions, useApiMutation } from '@/shared/api';
import { notificationChannelsApi } from '../api/notification-channels.api';
import type {
    NotificationChannelDto,
    CreateNotificationChannelRequest,
    UpdateNotificationChannelRequest,
    ChannelTypeDefinition,
} from '../types/notification-channels.types';

const NC_KEY = ['notification-channels'] as const;
const NC_TYPES_KEY = ['notification-channels', 'types'] as const;
const NC_SEVERITY_KEY = ['notification-channels', 'severity-options'] as const;

/** Shared query options for prefetch / invalidation */
export const notificationChannelQueries = {
    all: () => apiQueryOptions<NotificationChannelDto[]>(NC_KEY, () => notificationChannelsApi.getAll(), { staleTime: 5 * 60_000 }),
    types: () =>
        apiQueryOptions<ChannelTypeDefinition[]>(NC_TYPES_KEY, () => notificationChannelsApi.getChannelTypes(), {
            staleTime: Infinity,
        }),
    severityOptions: () =>
        apiQueryOptions<string[]>(NC_SEVERITY_KEY, () => notificationChannelsApi.getSeverityOptions(), {
            staleTime: Infinity,
        }),
};

export function useNotificationChannels() {
    return useQuery(notificationChannelQueries.all());
}

export function useCreateNotificationChannel() {
    const qc = useQueryClient();
    return useApiMutation<NotificationChannelDto, CreateNotificationChannelRequest>(
        (data) => notificationChannelsApi.create(data),
        {
            successMessage: 'Channel created',
            errorMessage: false,
            onSuccess: () => qc.invalidateQueries({ queryKey: NC_KEY }),
        },
    );
}

export function useUpdateNotificationChannel() {
    const qc = useQueryClient();
    return useApiMutation<void, { id: string; data: UpdateNotificationChannelRequest }>(
        ({ id, data }) => notificationChannelsApi.update(id, data),
        {
            successMessage: 'Channel saved',
            errorMessage: false,
            onSuccess: () => qc.invalidateQueries({ queryKey: NC_KEY }),
        },
    );
}

export function useToggleNotificationChannel() {
    const qc = useQueryClient();
    return useApiMutation<{ message: string }, string>(
        (id) => notificationChannelsApi.toggle(id),
        { successMessage: 'Channel toggled', onSuccess: () => qc.invalidateQueries({ queryKey: NC_KEY }) },
    );
}

export function useTestNotificationChannel() {
    return useApiMutation<{ message: string }, { id: string; message: string }>(
        ({ id, message }) => notificationChannelsApi.test(id, message),
        { successMessage: 'Test notification sent' },
    );
}

export function useDeleteNotificationChannel() {
    const qc = useQueryClient();
    return useApiMutation<void, string>(
        (id) => notificationChannelsApi.delete(id),
        { successMessage: 'Channel deleted', onSuccess: () => qc.invalidateQueries({ queryKey: NC_KEY }) },
    );
}

/** Fetch supported channel type definitions from backend */
export function useChannelTypes() {
    return useQuery(notificationChannelQueries.types());
}

/** Fetch severity filter options from backend */
export function useSeverityOptions() {
    return useQuery(notificationChannelQueries.severityOptions());
}
