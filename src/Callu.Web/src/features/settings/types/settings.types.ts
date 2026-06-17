/**
 * Settings Types — mirrors BE DTOs from Callu.Shared.Models.Settings
 */

/** Mirrors BE OrganizationSettingsDto */
export interface OrganizationSettingsDto {
    id: string;
    organizationName: string;
    defaultTimezone: string;
    defaultCulture: string;
    emailNotificationsEnabled: boolean;
    baseUrl: string;
    createdAt: string;
    updatedAt?: string;
}

/** Mirrors BE UpdateOrganizationSettingsRequest */
export interface UpdateOrganizationSettingsRequest {
    organizationName: string;
    defaultTimezone: string;
    defaultCulture: string;
    emailNotificationsEnabled: boolean;
    baseUrl: string;
}

/** Mirrors BE SmtpSettingsDto (password is never exposed, HasPassword flag instead) */
export interface SmtpSettingsDto {
    id: string;
    host: string;
    port: number;
    enableSsl: boolean;
    username?: string;
    hasPassword: boolean;
    fromAddress: string;
    fromName: string;
    isConfigured: boolean;
    lastTestedAt?: string;
    lastTestResult?: string;
}

/** Mirrors BE UpdateSmtpSettingsRequest */
export interface UpdateSmtpSettingsRequest {
    host: string;
    port: number;
    enableSsl: boolean;
    username?: string;
    password?: string;
    fromAddress: string;
    fromName: string;
}

/** Mirrors BE TimezoneDto */
export interface TimezoneDto {
    id: string;
    displayName: string;
    standardName: string;
    baseUtcOffset: string;
    offsetString: string;
    supportsDaylightSaving: boolean;
}

/** Inline request in controller */
export interface TestEmailRequest {
    recipientEmail: string;
}

/** Generic test result (used by SMTP test + test email) */
export interface SmtpTestResult {
    success: boolean;
    message: string;
}
