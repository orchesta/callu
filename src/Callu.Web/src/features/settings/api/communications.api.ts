/** Communication provider, SIP trunk and TTS template endpoints, all served from /api/v1/providers. */

import { apiClient } from '@/shared/api';
import { API_URL } from '@/shared/config';
import { authService } from '@/shared/auth/auth.service';
import type {
    CommunicationProviderDto,
    CreateProviderRequest,
    UpdateProviderRequest,
    CapabilityDto,
    CapabilityRouteDto,
    SipTrunkDto,
    CreateSipTrunkRequest,
    UpdateSipTrunkRequest,
    TtsTemplateDto,
    TtsTemplateSaveRequest,
    TtsKeyDescriptor,
    TtsPreviewRequest,
    TtsPreviewSegment,
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

    getCapabilityRoutes: () =>
        apiClient.get<CapabilityRouteDto[]>(`${BASE}/capability-routes`),
    /** A null providerId clears the pin and lets the ordinary provider order apply again. */
    setCapabilityRoute: (capability: string, providerId: string | null) =>
        apiClient.put<void>(`${BASE}/capability-routes/${capability}`, { providerId }),

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

    previewTts: (data: TtsPreviewRequest) =>
        apiClient.post<TtsPreviewSegment[]>(`${BASE}/tts/preview`, data),
    /** What a real call would say for this language — rendered through the path a call takes. */
    previewTtsTemplate: (languageCode: string) =>
        apiClient.post<TtsPreviewSegment[]>(
            `${BASE}/tts/preview/template/${encodeURIComponent(languageCode)}`, {}),
    /**
     * The rendered audio, as an object URL the caller must revoke. Fetched by hand rather than through
     * the typed client: it is a WAV stream, not an ApiResponse envelope, and an <audio src> cannot
     * carry the bearer token this endpoint requires.
     */
    ttsPreviewAudio: async (key: string): Promise<string> => {
        const url = `${API_URL}${BASE}/tts/preview/audio/${encodeURIComponent(key)}`;

        let response = await fetch(url, { headers: authHeader() });

        // The token is short-lived and a segment is fetched whenever a player mounts, so renew once
        // rather than showing "no audio" for a render that is sitting right there.
        if (response.status === 401 && (await authService.refreshAccessToken())) {
            response = await fetch(url, { headers: authHeader() });
        }

        if (!response.ok) throw new Error(`preview audio returned ${response.status}`);
        return URL.createObjectURL(await response.blob());
    },
};

function authHeader(): HeadersInit {
    const token = authService.getAccessToken();
    return token ? { Authorization: `Bearer ${token}` } : {};
}
