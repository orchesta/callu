/**
 * Notifications React Query hooks.
 */

import { useQuery, useQueryClient } from '@tanstack/react-query';
import { apiQueryOptions, useApiMutation } from '@/shared/api';
import { notificationApi } from '../api/notification.api';

export const notificationKeys = {
    all: ['notifications'] as const,
    recent: () => [...notificationKeys.all, 'recent'] as const,
    unreadCount: () => [...notificationKeys.all, 'unread-count'] as const,
};

export const notificationQueries = {
    recent: (count = 20) =>
        apiQueryOptions(notificationKeys.recent(), () => notificationApi.getRecent(count), { staleTime: 30_000 }),
    unreadCount: () =>
        apiQueryOptions(notificationKeys.unreadCount(), () => notificationApi.getUnreadCount(), { staleTime: 30_000 }),
};

/** Recent notifications with auto-refresh */
export function useRecentNotifications(count = 20) {
    return useQuery(notificationQueries.recent(count));
}

/** Unread notification count with frequent auto-refresh */
export function useUnreadCount() {
    return useQuery(notificationQueries.unreadCount());
}

/** Mark a single notification as read */
export function useMarkAsRead() {
    const qc = useQueryClient();
    return useApiMutation(
        (id: string) => notificationApi.markAsRead(id),
        {
            successMessage: false,
            onSuccess: () => {
                qc.invalidateQueries({ queryKey: notificationKeys.recent() });
                qc.invalidateQueries({ queryKey: notificationKeys.unreadCount() });
            },
        },
    );
}

/** Mark all notifications as read */
export function useMarkAllAsRead() {
    const qc = useQueryClient();
    return useApiMutation(
        () => notificationApi.markAllAsRead(),
        {
            successMessage: 'All notifications marked as read',
            onSuccess: () => qc.invalidateQueries({ queryKey: notificationKeys.all }),
        },
    );
}
