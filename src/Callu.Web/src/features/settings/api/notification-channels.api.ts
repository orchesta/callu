import { apiClient } from '@/shared/api';
import type {
    NotificationChannelDto,
    CreateNotificationChannelRequest,
    UpdateNotificationChannelRequest,
    ChannelTypeDefinition,
} from '../types/notification-channels.types';

const BASE = '/api/v1/notification-channels';

export const notificationChannelsApi = {
    getAll: () => apiClient.get<NotificationChannelDto[]>(BASE),
    getById: (id: string) => apiClient.get<NotificationChannelDto>(`${BASE}/${id}`),
    create: (data: CreateNotificationChannelRequest) => apiClient.post<NotificationChannelDto>(BASE, data),
    update: (id: string, data: UpdateNotificationChannelRequest) => apiClient.put<void>(`${BASE}/${id}`, data),
    toggle: (id: string) => apiClient.post<{ message: string }>(`${BASE}/${id}/toggle`),
    test: (id: string, message: string) => apiClient.post<{ message: string }>(`${BASE}/${id}/test`, { message }),
    delete: (id: string) => apiClient.delete<void>(`${BASE}/${id}`),
    /** GET /notification-channels/types — supported channel type definitions */
    getChannelTypes: () => apiClient.get<ChannelTypeDefinition[]>(`${BASE}/types`),
    /** GET /notification-channels/severity-options */
    getSeverityOptions: () => apiClient.get<string[]>(`${BASE}/severity-options`),
};
