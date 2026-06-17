/**
 * Profile API module — connects to ProfileController.
 */

import { apiClient } from '@/shared/api';
import type { UserProfileDto, UpdateProfileRequest, ChangePasswordRequest, NotificationPreferencesDto } from '../types/profile.types';

const BASE = '/api/v1/profile';

export const profileApi = {
    /** Get current user profile */
    get: () =>
        apiClient.get<UserProfileDto>(BASE),

    /** Update profile (firstName, lastName, phoneNumber, timezone) */
    update: (data: UpdateProfileRequest) =>
        apiClient.put<{ message: string }>(BASE, data),

    /** Change password */
    changePassword: (data: ChangePasswordRequest) =>
        apiClient.post<{ message: string }>(`${BASE}/change-password`, data),

    /** Get notification preferences */
    getNotificationPreferences: () =>
        apiClient.get<NotificationPreferencesDto>(`${BASE}/notification-preferences`),

    /** Update notification preferences */
    updateNotificationPreferences: (data: NotificationPreferencesDto) =>
        apiClient.put<{ message: string }>(`${BASE}/notification-preferences`, data),
};
