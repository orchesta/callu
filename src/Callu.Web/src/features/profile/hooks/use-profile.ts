/**
 * Profile React Query hooks.
 */

import { useQuery, useQueryClient } from '@tanstack/react-query';
import { apiQueryOptions, useApiMutation } from '@/shared/api';
import { profileApi } from '../api/profile.api';
import type { UpdateProfileRequest, ChangePasswordRequest, NotificationPreferencesDto } from '../types/profile.types';
import { useEffect } from 'react';
import { getLocale, hasDeviceLocaleChoice, localeForCulture, setLocale } from '@/shared/locales/i18n';

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
/** Adopts the account's language on a device that has not chosen one, so a person who set Turkish
 *  elsewhere is not met in English — and is not overruled if they picked a language here. */
export function useAdoptAccountLanguage(culture: string | null | undefined) {
    useEffect(() => {
        if (!culture || hasDeviceLocaleChoice()) return;

        const locale = localeForCulture(culture);
        if (locale && locale !== getLocale()) void setLocale(locale);
    }, [culture]);
}

/** Records the language choice without a toast — the screen switching language is the feedback. */
export function useUpdateLanguage() {
    const qc = useQueryClient();
    return useApiMutation(
        (culture: string) => profileApi.update({ culture }),
        {
            successMessage: false,
            onSuccess: () => qc.invalidateQueries({ queryKey: profileKeys.all }),
        },
    );
}

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
