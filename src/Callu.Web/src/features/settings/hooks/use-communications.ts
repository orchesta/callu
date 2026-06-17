/**
 * Communications React Query hooks.
 */

import { useQuery, useQueryClient } from '@tanstack/react-query';
import { apiQueryOptions, useApiMutation } from '@/shared/api';
import { communicationsApi } from '../api/communications.api';
import type {
    CreateProviderRequest,
    UpdateProviderRequest,
    CreateSipTrunkRequest,
    UpdateSipTrunkRequest,
    TtsTemplateSaveRequest,
} from '../types/communications.types';

export const communicationKeys = {
    all: ['communications'] as const,
    providers: () => [...communicationKeys.all, 'providers'] as const,
    provider: (id: string) => [...communicationKeys.all, 'provider', id] as const,
    capabilities: () => [...communicationKeys.all, 'capabilities'] as const,
    sipTrunks: () => [...communicationKeys.all, 'sip-trunks'] as const,
    sipTrunk: (id: string) => [...communicationKeys.all, 'sip-trunk', id] as const,
    ttsTemplates: () => [...communicationKeys.all, 'tts-templates'] as const,
    ttsTemplate: (langCode: string) => [...communicationKeys.all, 'tts-template', langCode] as const,
};

export const communicationQueries = {
    providers: () =>
        apiQueryOptions(communicationKeys.providers(), () => communicationsApi.getProviders(), { staleTime: 5 * 60_000 }),
    provider: (id: string) =>
        apiQueryOptions(communicationKeys.provider(id), () => communicationsApi.getProvider(id), { enabled: !!id }),
    capabilities: () =>
        apiQueryOptions(communicationKeys.capabilities(), () => communicationsApi.getCapabilities(), { staleTime: Infinity }),
    sipTrunks: () =>
        apiQueryOptions(communicationKeys.sipTrunks(), () => communicationsApi.getSipTrunks(), { staleTime: 5 * 60_000 }),
    sipTrunk: (id: string) =>
        apiQueryOptions(communicationKeys.sipTrunk(id), () => communicationsApi.getSipTrunk(id), { enabled: !!id }),
    ttsTemplates: () =>
        apiQueryOptions(communicationKeys.ttsTemplates(), () => communicationsApi.getTtsTemplates(), { staleTime: 5 * 60_000 }),
    ttsTemplate: (langCode: string) =>
        apiQueryOptions(communicationKeys.ttsTemplate(langCode), () => communicationsApi.getTtsTemplate(langCode), {
            enabled: !!langCode,
        }),
};

export function useCommunicationProviders() {
    return useQuery(communicationQueries.providers());
}

export function useCommunicationProvider(id: string) {
    return useQuery(communicationQueries.provider(id));
}

export function useCommunicationCapabilities() {
    return useQuery(communicationQueries.capabilities());
}

export function useCreateProvider() {
    const qc = useQueryClient();
    return useApiMutation(
        (data: CreateProviderRequest) => communicationsApi.createProvider(data),
        {
            onSuccess: () =>
                qc.invalidateQueries({ queryKey: communicationKeys.providers() }),
        },
    );
}

export function useUpdateProvider() {
    const qc = useQueryClient();
    return useApiMutation(
        ({ id, ...data }: { id: string } & UpdateProviderRequest) =>
            communicationsApi.updateProvider(id, data),
        {
            onSuccess: () =>
                qc.invalidateQueries({ queryKey: communicationKeys.all }),
        },
    );
}

export function useDeleteProvider() {
    const qc = useQueryClient();
    return useApiMutation(
        (id: string) => communicationsApi.deleteProvider(id),
        {
            onSuccess: () =>
                qc.invalidateQueries({ queryKey: communicationKeys.providers() }),
        },
    );
}

export function useSipTrunks() {
    return useQuery(communicationQueries.sipTrunks());
}

export function useSipTrunk(id: string) {
    return useQuery(communicationQueries.sipTrunk(id));
}

export function useCreateSipTrunk() {
    const qc = useQueryClient();
    return useApiMutation(
        (data: CreateSipTrunkRequest) => communicationsApi.createSipTrunk(data),
        {
            onSuccess: () =>
                qc.invalidateQueries({ queryKey: communicationKeys.sipTrunks() }),
        },
    );
}

export function useUpdateSipTrunk() {
    const qc = useQueryClient();
    return useApiMutation(
        ({ id, ...data }: { id: string } & UpdateSipTrunkRequest) =>
            communicationsApi.updateSipTrunk(id, data),
        {
            onSuccess: () =>
                qc.invalidateQueries({ queryKey: communicationKeys.sipTrunks() }),
        },
    );
}

export function useDeleteSipTrunk() {
    const qc = useQueryClient();
    return useApiMutation(
        (id: string) => communicationsApi.deleteSipTrunk(id),
        {
            onSuccess: () =>
                qc.invalidateQueries({ queryKey: communicationKeys.sipTrunks() }),
        },
    );
}

export function useTtsTemplates() {
    return useQuery(communicationQueries.ttsTemplates());
}

export function useTtsTemplate(langCode: string) {
    return useQuery(communicationQueries.ttsTemplate(langCode));
}

export function useSaveTtsTemplate() {
    const qc = useQueryClient();
    return useApiMutation(
        (data: TtsTemplateSaveRequest) => communicationsApi.saveTtsTemplate(data),
        {
            onSuccess: () =>
                qc.invalidateQueries({ queryKey: communicationKeys.ttsTemplates() }),
        },
    );
}

export function useDeleteTtsTemplate() {
    const qc = useQueryClient();
    return useApiMutation(
        (langCode: string) => communicationsApi.deleteTtsTemplate(langCode),
        {
            onSuccess: () =>
                qc.invalidateQueries({ queryKey: communicationKeys.ttsTemplates() }),
        },
    );
}

