/**
 * Profile Types — mirrors BE DTOs from Callu.Shared.Models.Auth
 */

/** Mirrors BE UserProfileDto */
export interface UserProfileDto {
    userId: string;
    firstName: string;
    lastName: string;
    email: string;
    phoneNumber?: string;
    timezone?: string;
    createdAt: string;
}

/** Mirrors BE UpdateProfileRequest */
export interface UpdateProfileRequest {
    firstName?: string;
    lastName?: string;
    phoneNumber?: string;
    timezone?: string;
}

/** Mirrors BE ChangePasswordRequest (inline record in ProfileController) */
export interface ChangePasswordRequest {
    currentPassword: string;
    newPassword: string;
}

/** Mirrors BE NotificationPreferencesDto */
export interface NotificationPreferencesDto {
    emailEnabled: boolean;
    smsEnabled: boolean;
    voiceEnabled: boolean;
    pushEnabled: boolean;
    quietHoursStart?: string | null;
    quietHoursEnd?: string | null;
    timezone?: string | null;
}
