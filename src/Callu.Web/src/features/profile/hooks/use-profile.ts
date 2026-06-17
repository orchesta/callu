/**
 * Profile React Query hooks.
 */

import { useQuery, useQueryClient } from '@tanstack/react-query';
import { apiQueryOptions, useApiMutation } from '@/shared/api';
import { profileApi } from '../api/profile.api';
import type { UpdateProfileRequest, ChangePasswordRequest, NotificationPreferencesDto } from '../types/profile.types';

export const profileKeys = {
    all: ['profile'] as const,
    notifPrefs: () => [...profileKeys.all, 'notification-preferences'] as const,
};

export const profileQueries = {
    me: () => apiQueryOptions(profileKeys.all, () => profileApi.get()),
    notificationPreferences: () =>
        apiQueryOptions(profileKeys.notifPrefs(), () => profileApi.getNotificationPreferences()),
};

/** Get current user profile */
export function useProfile() {
    return useQuery(profileQueries.me());
}

/** Update profile */
export function useUpdateProfile() {
    const qc = useQueryClient();
    return useApiMutation(
        (data: UpdateProfileRequest) => profileApi.update(data),
        {
            successMessage: 'Profile updated',
            onSuccess: () => qc.invalidateQueries({ queryKey: profileKeys.all }),
        },
    );
}

/** Change password */
export function useChangePassword() {
    return useApiMutation(
        (data: ChangePasswordRequest) => profileApi.changePassword(data),
        { successMessage: 'Password changed successfully' },
    );
}

/** Get notification preferences */
export function useNotificationPreferences() {
    return useQuery(profileQueries.notificationPreferences());
}

/** Update notification preferences */
export function useUpdateNotificationPreferences() {
    const qc = useQueryClient();
    return useApiMutation(
        (data: NotificationPreferencesDto) => profileApi.updateNotificationPreferences(data),
        {
            successMessage: 'Notification preferences saved',
            onSuccess: () => qc.invalidateQueries({ queryKey: profileKeys.notifPrefs() }),
        },
    );
}
