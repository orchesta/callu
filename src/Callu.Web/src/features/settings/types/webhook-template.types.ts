/**
 * Webhook Template Types — mirrors BE DTOs from Callu.Shared.Models.Webhooks
 */

/** Mirrors BE WebhookTemplateDto */
export interface WebhookTemplateDto {
    id: string;
    name: string;
    description?: string;
    fieldMappings: string;
    stateMapping?: string;
    samplePayload?: string;
    dataLanguage: string;
    isBuiltIn: boolean;
    isActive: boolean;
    usageCount: number;
}

/** Mirrors BE CreateWebhookTemplateRequest */
export interface CreateWebhookTemplateRequest {
    name: string;
    description?: string;
    fieldMappings: string;
    stateMapping?: string;
    samplePayload?: string;
    dataLanguage?: string;
}

/** Mirrors BE UpdateWebhookTemplateRequest */
export interface UpdateWebhookTemplateRequest {
    name?: string;
    description?: string;
    fieldMappings?: string;
    stateMapping?: string;
    samplePayload?: string;
    dataLanguage?: string;
    isActive?: boolean;
}

/** Mirrors BE WebhookTemplateTestResult */
export interface WebhookTemplateTestResult {
    success: boolean;
    errorMessage?: string;
    mappedFields: Record<string, string | null>;
}
