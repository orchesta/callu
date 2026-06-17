/**
 * Public Auth API methods.
 *
 * These endpoints do NOT require a JWT token — they are used
 * on pages where the user is not yet authenticated.
 *
 * Uses apiClient which handles base URL, Content-Type, and error unwrapping.
 */

import { apiClient } from '@/shared/api';

const BASE = '/api/v1/auth';

export const authApi = {
    /**
     * Request a password-reset email.
     * Backend always returns 200 to prevent email enumeration.
     */
    forgotPassword: (email: string) =>
        apiClient.post<void>(`${BASE}/forgot-password`, { email }),

    /**
     * Reset password using token received via email.
     */
    resetPassword: (email: string, token: string, newPassword: string) =>
        apiClient.post<void>(`${BASE}/reset-password`, { email, token, newPassword }),

    /**
     * Accept invitation and set initial password.
     */
    acceptInvitation: (email: string, token: string, newPassword: string) =>
        apiClient.post<void>(`${BASE}/accept-invitation`, { email, token, newPassword }),
};
