/** Mirrors BE IntegrationDto — UI copy says "application". */
export interface IntegrationDto {
    id: string;
    name: string;
    type: string;
    description?: string;
    serviceId?: string;
    serviceName?: string;
    teamId?: string;
    teamName?: string;
    webhookTemplateId?: string;
    webhookTemplateName?: string;
    isActive: boolean;
    webhookEnabled: boolean;
    listeningMode: boolean;
    capturedCount: number;
    hasToken: boolean;
    /** Path only; prefix with window.location.origin in the UI. */
    webhookUrl?: string;
    hasApiKey: boolean;
    maskedApiKey?: string;
    hasSignatureSecret: boolean;
    signatureHeaderName?: string;
    lastWebhookReceivedAt?: string;
    webhooksReceivedCount: number;
    createdAt: string;
    updatedAt?: string;
}

/** Shown once at create / rotate — never returned again. */
export interface IntegrationSecretsDto {
    id: string;
    webhookUrl: string;
    webhookToken: string;
    apiKey: string;
}

export interface CreateIntegrationRequest {
    name: string;
    type?: string;
    description?: string;
    /** Omitted = the endpoint starts unbound and captures everything it receives. */
    serviceId?: string;
    teamId?: string;
    webhookTemplateId?: string;
    listeningMode?: boolean;
    webhookSecret?: string;
    webhookSignatureHeader?: string;
}

export interface UpdateIntegrationRequest {
    name: string;
    description?: string;
    teamId?: string;
    webhookTemplateId?: string;
    isActive: boolean;
    webhookEnabled: boolean;
    /** null/omit = leave untouched */
    listeningMode?: boolean;
    /** null/omit = leave; empty string = clear */
    webhookSecret?: string;
    webhookSignatureHeader?: string;
}

export interface BindIntegrationServiceRequest {
    /** null unbinds the endpoint back to capture-only. */
    serviceId: string | null;
}
