/**
 * User types mirroring BE Callu.Shared.Models.Auth.UserDto
 */

export interface UserDto {
    id: string;
    email: string;
    displayName?: string;
    firstName?: string;
    lastName?: string;
    phoneNumber?: string;
    timezone?: string;
    initials?: string;
    role: string;
    isActive: boolean;
    emailConfirmed: boolean;
    createdAt: string;
    lastLoginAt?: string;
}

/** Mirrors BE inline record InviteUserRequest(Email, Role) */
export interface InviteUserRequest {
    email: string;
    role: string;
}

/** Mirrors BE inline record ChangeRoleRequest(Role) */
export interface ChangeRoleRequest {
    role: string;
}

/** Mirrors BE AdminUpdateUserRequest — admin edit of a user's name + phone */
export interface AdminUpdateUserRequest {
    firstName?: string;
    lastName?: string;
    phoneNumber?: string;
}
