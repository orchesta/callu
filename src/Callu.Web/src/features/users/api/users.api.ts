/**
 * Users API module — connects to UsersController.
 */

import { apiClient } from '@/shared/api';
import type {
    UserDto,
    InviteUserRequest,
    ChangeRoleRequest,
    AdminUpdateUserRequest,
    NotificationPreferencesDto,
} from '../types/user.types';

const BASE = '/api/v1/users';

export const usersApi = {
    /** Get all users */
    getAll: () =>
        apiClient.get<UserDto[]>(BASE),

    /** Get user by ID */
    getById: (id: string) =>
        apiClient.get<UserDto>(`${BASE}/${id}`),

    /** Invite a new user (sends email invitation) */
    invite: (data: InviteUserRequest) =>
        apiClient.post<{ message: string }>(`${BASE}/invite`, data),

    /** Admin: update a user's name + phone */
    updateUser: (id: string, data: AdminUpdateUserRequest) =>
        apiClient.put<{ message: string }>(`${BASE}/${id}`, data),

    /** Change a user's role */
    changeRole: (id: string, data: ChangeRoleRequest) =>
        apiClient.put<{ message: string }>(`${BASE}/${id}/role`, data),

    /** Remove a user */
    remove: (id: string) =>
        apiClient.delete<void>(`${BASE}/${id}`),

    /** Resend invitation email */
    resendInvitation: (id: string) =>
        apiClient.post<{ message: string }>(`${BASE}/${id}/resend-invitation`),

    /** Admin: get a user's notification preferences */
    getNotificationPreferences: (id: string) =>
        apiClient.get<NotificationPreferencesDto>(`${BASE}/${id}/notification-preferences`),

    /** Admin: update a user's notification preferences */
    updateNotificationPreferences: (id: string, data: NotificationPreferencesDto) =>
        apiClient.put<{ message: string }>(`${BASE}/${id}/notification-preferences`, data),
};
