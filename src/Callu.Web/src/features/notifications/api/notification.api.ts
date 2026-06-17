/**
 * Notifications API module — connects to NotificationsController.
 */

import { apiClient } from '@/shared/api';
import type { NotificationItemDto } from '../types/notification.types';

const BASE = '/api/v1/notifications';

export const notificationApi = {
    /** Get recent notifications (default 20) */
    getRecent: (count = 20) =>
        apiClient.get<NotificationItemDto[]>(BASE, { params: { count } }),

    /** Get unread notification count */
    getUnreadCount: () =>
        apiClient.get<{ count: number }>(`${BASE}/unread-count`),

    /** Mark a single notification as read */
    markAsRead: (id: string) =>
        apiClient.post<{ message: string }>(`${BASE}/${id}/read`),

    /** Mark all notifications as read */
    markAllAsRead: () =>
        apiClient.post<{ message: string }>(`${BASE}/read-all`),
};
