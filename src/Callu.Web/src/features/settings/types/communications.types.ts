/**
 * Communications Types — mirrors BE DTOs from Callu.Shared.Models.Communication
 */

/** BE CommunicationCapability flags enum */
export enum CommunicationCapability {
    None = 0,
    VoiceCalls = 1 << 0,
    Sms = 1 << 1,
    WhatsApp = 1 << 2,
    VideoConference = 1 << 3,
    TTS = 1 << 4,
    ASR = 1 << 5,
    Recording = 1 << 6,
    VoicemailDetection = 1 << 9,
}

/** Mirrors BE CommunicationProviderDto */
export interface CommunicationProviderDto {
    id: string;
    name: string;
    providerType: string;
    capabilities: number;
    sipTrunkId?: string;
    sipTrunkName?: string;
    isEnabled: boolean;
    priority: number;
    lastTestedAt?: string;
    lastTestResult?: string;

    voximplantAccountId?: string;
    voximplantApiKey?: string;
    voximplantNode?: string;

    verimorUsername?: string;
    verimorPassword?: string;
    verimorSenderId?: string;

    httpSms?: HttpSmsConfigDto;
}

/** Non-secret view of a generic HTTP SMS provider's config — mirrors BE HttpSmsConfigDto. */
export interface HttpSmsConfigDto {
    url: string;
    method: string;
    contentType: string;
    senderId?: string;
    bodyTemplate?: string;
    headers?: Record<string, string>;
    successMode?: string;
    successField?: string;
    successValue?: string;
    messageIdPath?: string;
    hasApiKey: boolean;
    hasUsername: boolean;
    hasPassword: boolean;
}

/** Create provider request — matches BE CreateProviderRequest */
export interface CreateProviderRequest {
    name: string;
    providerType: string;
    config: Record<string, unknown>;
    sipTrunkId?: string;
    priority?: number;
}

/** Update provider request — matches BE UpdateProviderRequest */
export interface UpdateProviderRequest {
    name?: string;
    config?: Record<string, unknown>;
    sipTrunkId?: string;
    isEnabled?: boolean;
    priority?: number;
}


/** Capability description */
export interface CapabilityDto {
    id: string;
    name: string;
    description: string;
}

export interface SipTrunkDto {
    id: string;
    name: string;
    server: string;
    port: number;
    username: string;
    authUser?: string;
    callerId?: string;
    displayName?: string;
    useTls: boolean;
    useTcp: boolean;
    isEnabled: boolean;
}

export interface CreateSipTrunkRequest {
    name: string;
    server: string;
    port: number;
    username: string;
    password: string;
    authUser?: string;
    callerId?: string;
    displayName?: string;
    useTls: boolean;
    useTcp: boolean;
}

export interface UpdateSipTrunkRequest {
    name?: string;
    server?: string;
    port?: number;
    username?: string;
    password?: string;
    authUser?: string;
    callerId?: string;
    displayName?: string;
    useTls?: boolean;
    useTcp?: boolean;
    isEnabled?: boolean;
}

export interface TtsTemplateDto {
    id: string;
    languageCode: string;
    displayName: string;
    isDefault: boolean;
    messages: Record<string, string>;
}

export interface TtsTemplateSaveRequest {
    languageCode: string;
    displayName: string;
    isDefault: boolean;
    messages: Record<string, string>;
}

/** Describes a TTS message key — mirrors BE TtsKeyDescriptor */
export interface TtsKeyDescriptor {
    key: string;
    label: string;
    group: string;
    description: string;
}
