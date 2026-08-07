/**
 * Webhook Settings Types — aligned with BE ServiceWebhookSettingsDto
 */

export interface ServiceWebhookSettingsDto {
    serviceId: string;
    providerId?: string;
    providerName?: string;
    webhookEnabled: boolean;
    webhookUrl?: string;
    webhookToken?: string;
    /** Masked: "****abcd" on GET; full value is only returned once at regenerate. */
    apiKey?: string;
    hasApiKey: boolean;
    listeningMode: boolean;
    /** True when an HMAC signing secret is configured. The plaintext is never returned by GET. */
    hasSignatureSecret?: boolean;
    /** Configured signature header name (default X-Callu-Signature). */
    signatureHeaderName?: string;
    templateId?: string;
    templateName?: string;
    lastWebhookReceivedAt?: string;
    webhooksReceivedCount: number;
    capturedCount: number;
}

export interface SetProviderRequest {
    providerId: string;
}

export interface ToggleListeningModeRequest {
    enabled: boolean;
}

export interface SetTemplateRequest {
    templateId: string | null;
}

export interface SetSignatureRequest {
    /** ≥ 32 chars required by the backend validator. */
    secret: string;
    /** Defaults to "X-Callu-Signature" if omitted. */
    headerName?: string;
}

/** Response shape of POST /webhook-settings/signature — full secret returned exactly once. */
export interface SetSignatureResponse {
    secret: string;
    headerName: string;
}
