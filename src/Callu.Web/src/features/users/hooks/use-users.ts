/**
 * Users React Query hooks.
 */

import { useQuery, useQueryClient } from '@tanstack/react-query';
import { apiQueryOptions, useApiMutation } from '@/shared/api';
import { usersApi } from '../api/users.api';
import type { InviteUserRequest, AdminUpdateUserRequest, NotificationPreferencesDto } from '../types/user.types';

export const userKeys = {
    all: ['users'] as const,
    lists: () => [...userKeys.all, 'list'] as const,
    detail: (id: string) => [...userKeys.all, 'detail', id] as const,
    notificationPreferences: (id: string) => [...userKeys.all, 'notification-preferences', id] as const,
};

export const userQueries = {
    list: () => apiQueryOptions(userKeys.lists(), () => usersApi.getAll(), { staleTime: 2 * 60_000 }),
    detail: (id: string) => apiQueryOptions(userKeys.detail(id), () => usersApi.getById(id), { enabled: !!id }),
    notificationPreferences: (id: string, enabled = true) =>
        apiQueryOptions(
            userKeys.notificationPreferences(id),
            () => usersApi.getNotificationPreferences(id),
            { enabled: enabled && !!id },
        ),
};

/** All users list */
export function useUsers() {
    return useQuery(userQueries.list());
}

/** Single user by ID */
export function useUser(id: string) {
    return useQuery(userQueries.detail(id));
}

/** Invite a new user */
export function useInviteUser() {
    const qc = useQueryClient();
    return useApiMutation(
        (data: InviteUserRequest) => usersApi.invite(data),
        {
            successMessage: 'Invitation sent successfully',
            onSuccess: () => qc.invalidateQueries({ queryKey: userKeys.lists() }),
        },
    );
}

/** Admin: update a user's name + phone */
export function useUpdateUser() {
    const qc = useQueryClient();
    return useApiMutation(
        ({ id, data }: { id: string; data: AdminUpdateUserRequest }) => usersApi.updateUser(id, data),
        {
            successMessage: 'User updated',
            onSuccess: () => qc.invalidateQueries({ queryKey: userKeys.all }),
        },
    );
}

/** Change a user's role */
export function useChangeRole() {
    const qc = useQueryClient();
    return useApiMutation(
        ({ id, role }: { id: string; role: string }) => usersApi.changeRole(id, { role }),
        {
            successMessage: 'Role updated',
            onSuccess: () => qc.invalidateQueries({ queryKey: userKeys.all }),
        },
    );
}

/** Remove a user */
export function useRemoveUser() {
    const qc = useQueryClient();
    return useApiMutation(
        (id: string) => usersApi.remove(id),
        {
            successMessage: 'User removed',
            onSuccess: () => qc.invalidateQueries({ queryKey: userKeys.lists() }),
        },
    );
}

/** Admin: a user's notification preferences (fetched lazily, e.g. when an edit modal opens) */
export function useUserNotificationPreferences(id: string, enabled = true) {
    return useQuery(userQueries.notificationPreferences(id, enabled));
}

/** Admin: update a user's notification preferences */
export function useUpdateUserNotificationPreferences() {
    const qc = useQueryClient();
    return useApiMutation(
        ({ id, data }: { id: string; data: NotificationPreferencesDto }) =>
            usersApi.updateNotificationPreferences(id, data),
        {
            successMessage: 'Notification preferences updated',
            onSuccess: (_result, variables) =>
                qc.invalidateQueries({ queryKey: userKeys.notificationPreferences(variables.id) }),
        },
    );
}

/** Resend invitation email */
export function useResendInvitation() {
    const qc = useQueryClient();
    return useApiMutation(
        (id: string) => usersApi.resendInvitation(id),
        {
            successMessage: 'Invitation resent',
            onSuccess: () => qc.invalidateQueries({ queryKey: userKeys.lists() }),
        },
    );
}
