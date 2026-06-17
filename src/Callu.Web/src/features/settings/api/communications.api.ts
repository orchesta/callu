/**
 * Communications API — connects to ProvidersController.
 * All communication provider, SIP trunk, and TTS template endpoints
 * are served from /api/v1/providers.
 */

import { apiClient } from '@/shared/api';
import type {
    CommunicationProviderDto,
    CreateProviderRequest,
    UpdateProviderRequest,
    CapabilityDto,
    SipTrunkDto,
    CreateSipTrunkRequest,
    UpdateSipTrunkRequest,
    TtsTemplateDto,
    TtsTemplateSaveRequest,
    TtsKeyDescriptor,
} from '../types/communications.types';

const BASE = '/api/v1/providers';

export const communicationsApi = {
    getProviders: () =>
        apiClient.get<CommunicationProviderDto[]>(BASE),
    getProvider: (id: string) =>
        apiClient.get<CommunicationProviderDto>(`${BASE}/${id}`),
    createProvider: (data: CreateProviderRequest) =>
        apiClient.post<CommunicationProviderDto>(BASE, data),
    updateProvider: (id: string, data: UpdateProviderRequest) =>
        apiClient.put<void>(`${BASE}/${id}`, data),
    deleteProvider: (id: string) =>
        apiClient.delete<void>(`${BASE}/${id}`),
    /** Send a one-off test SMS through the provider (verifies config end-to-end). */
    testSms: (id: string, data: { to: string; message?: string }) =>
        apiClient.post<{ success: boolean; messageId?: string; errorMessage?: string }>(`${BASE}/${id}/test-sms`, data),
    getCapabilities: () =>
        apiClient.get<CapabilityDto[]>(`${BASE}/capabilities`),

    getSipTrunks: () =>
        apiClient.get<SipTrunkDto[]>(`${BASE}/sip-trunks`),
    getSipTrunk: (id: string) =>
        apiClient.get<SipTrunkDto>(`${BASE}/sip-trunks/${id}`),
    createSipTrunk: (data: CreateSipTrunkRequest) =>
        apiClient.post<SipTrunkDto>(`${BASE}/sip-trunks`, data),
    updateSipTrunk: (id: string, data: UpdateSipTrunkRequest) =>
        apiClient.put<void>(`${BASE}/sip-trunks/${id}`, data),
    deleteSipTrunk: (id: string) =>
        apiClient.delete<void>(`${BASE}/sip-trunks/${id}`),

    getTtsTemplates: () =>
        apiClient.get<TtsTemplateDto[]>(`${BASE}/tts-templates`),
    getTtsTemplate: (langCode: string) =>
        apiClient.get<TtsTemplateDto>(`${BASE}/tts-templates/${langCode}`),
    saveTtsTemplate: (data: TtsTemplateSaveRequest) =>
        apiClient.post<void>(`${BASE}/tts-templates`, data),
    deleteTtsTemplate: (langCode: string) =>
        apiClient.delete<void>(`${BASE}/tts-templates/${langCode}`),
    getTtsDefaults: (langCode: string) =>
        apiClient.get<Record<string, string>>(`${BASE}/tts-templates/defaults/${langCode}`),
    getTtsKeys: () =>
        apiClient.get<TtsKeyDescriptor[]>(`${BASE}/tts-templates/keys`),
};
