/**
 * Email Template Types — mirrors BE DTOs from EmailTemplatesController.
 *
 * Backend endpoints: CRUD + preview + send-test.
 * See: Callu.Api/Controllers/EmailTemplatesController.cs
 */

/** Mirrors planned BE EmailTemplateDto (list item) */
export interface EmailTemplateDto {
    id: string;
    name: string;
    key: string;
    subject: string;
    isSystem: boolean;
    isActive: boolean;
    createdAt: string;
}

/** Mirrors planned BE EmailTemplateDetailDto (single item with body) */
export interface EmailTemplateDetailDto extends EmailTemplateDto {
    htmlBody: string;
    plainTextBody?: string;
    description?: string;
}

/** Mirrors planned BE CreateEmailTemplateRequest */
export interface CreateEmailTemplateRequest {
    name: string;
    key: string;
    subject: string;
    htmlBody: string;
    plainTextBody?: string;
    description?: string;
}

/** Mirrors planned BE UpdateEmailTemplateRequest */
export interface UpdateEmailTemplateRequest {
    name?: string;
    subject?: string;
    htmlBody?: string;
    plainTextBody?: string;
    description?: string;
    isActive?: boolean;
}

/** Preview email request */
export interface PreviewEmailRequest {
    variables: Record<string, string>;
}

/** Send test email request */
export interface SendTestEmailRequest {
    email: string;
}
