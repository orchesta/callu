/**
 * Voximplant Management React Query hooks.
 * 
 * Consolidated from features/settings/hooks/use-voximplant.ts
 */

import { useQuery, useQueryClient } from '@tanstack/react-query';
import { apiQueryOptions, useApiMutation } from '@/shared/api';
import { voximplantApi } from '../api/voximplant.api';
import type {
    CreateVoxApplicationRequest,
    CreateVoxScenarioRequest,
    CreateVoxRuleRequest,
    CreateVoxUserRequest,
} from '../types/voximplant.types';

export const voximplantKeys = {
    all: ['voximplant'] as const,
    account: (providerId: string) => [...voximplantKeys.all, 'account', providerId] as const,
    status: (providerId: string) => [...voximplantKeys.all, 'status', providerId] as const,
    applications: (providerId: string) => [...voximplantKeys.all, 'applications', providerId] as const,
    scenarios: (providerId: string, appId?: number) =>
        [...voximplantKeys.all, 'scenarios', providerId, appId] as const,
    rules: (providerId: string, appId: number) =>
        [...voximplantKeys.all, 'rules', providerId, appId] as const,
    users: (providerId: string, appId: number) =>
        [...voximplantKeys.all, 'users', providerId, appId] as const,

};

export const voximplantQueries = {
    account: (providerId: string) =>
        apiQueryOptions(voximplantKeys.account(providerId), () => voximplantApi.getAccountInfo(providerId), {
            enabled: !!providerId,
            staleTime: 5 * 60_000,
        }),
    status: (providerId: string) =>
        apiQueryOptions(voximplantKeys.status(providerId), () => voximplantApi.getStatus(providerId), {
            enabled: !!providerId,
            staleTime: 30_000,
            refetchInterval: 30_000,
        }),
    applications: (providerId: string) =>
        apiQueryOptions(voximplantKeys.applications(providerId), () => voximplantApi.getApplications(providerId), {
            enabled: !!providerId,
            staleTime: 5 * 60_000,
        }),
    scenarios: (providerId: string, applicationId?: number) =>
        apiQueryOptions(
            voximplantKeys.scenarios(providerId, applicationId),
            () => voximplantApi.getScenarios(providerId, applicationId),
            { enabled: !!providerId, staleTime: 5 * 60_000 },
        ),
    rules: (providerId: string, applicationId: number) =>
        apiQueryOptions(voximplantKeys.rules(providerId, applicationId), () => voximplantApi.getRules(providerId, applicationId), {
            enabled: !!providerId && !!applicationId,
            staleTime: 5 * 60_000,
        }),
    users: (providerId: string, applicationId: number) =>
        apiQueryOptions(voximplantKeys.users(providerId, applicationId), () => voximplantApi.getUsers(providerId, applicationId), {
            enabled: !!providerId && !!applicationId,
            staleTime: 2 * 60_000,
        }),
};

export function useVoxAccountInfo(providerId: string) {
    return useQuery(voximplantQueries.account(providerId));
}

export function useVoxStatus(providerId: string) {
    return useQuery(voximplantQueries.status(providerId));
}

export function useVoxApplications(providerId: string) {
    return useQuery(voximplantQueries.applications(providerId));
}

export function useVoxScenarios(providerId: string, applicationId?: number) {
    return useQuery(voximplantQueries.scenarios(providerId, applicationId));
}

export function useVoxRules(providerId: string, applicationId: number) {
    return useQuery(voximplantQueries.rules(providerId, applicationId));
}

export function useVoxUsers(providerId: string, applicationId: number) {
    return useQuery(voximplantQueries.users(providerId, applicationId));
}

export function useCreateVoxApplication() {
    const qc = useQueryClient();
    return useApiMutation(
        ({ providerId, ...data }: { providerId: string } & CreateVoxApplicationRequest) =>
            voximplantApi.createApplication(providerId, data),
        {
            onSuccess: (_, { providerId }) =>
                qc.invalidateQueries({ queryKey: voximplantKeys.applications(providerId) }),
        },
    );
}

export function useDeleteVoxApplication() {
    const qc = useQueryClient();
    return useApiMutation(
        ({ providerId, applicationId }: { providerId: string; applicationId: number }) =>
            voximplantApi.deleteApplication(providerId, applicationId),
        {
            onSuccess: (_, { providerId }) =>
                qc.invalidateQueries({ queryKey: voximplantKeys.applications(providerId) }),
        },
    );
}

export function useCreateVoxScenario() {
    const qc = useQueryClient();
    return useApiMutation(
        ({ providerId, ...data }: { providerId: string } & CreateVoxScenarioRequest) =>
            voximplantApi.createScenario(providerId, data),
        {
            onSuccess: (_, { providerId }) =>
                qc.invalidateQueries({ queryKey: [...voximplantKeys.all, 'scenarios', providerId] }),
        },
    );
}

export function useCreateVoxRule() {
    const qc = useQueryClient();
    return useApiMutation(
        ({ providerId, ...data }: { providerId: string } & CreateVoxRuleRequest) =>
            voximplantApi.createRule(providerId, data),
        {
            onSuccess: (_, { providerId }) =>
                qc.invalidateQueries({ queryKey: [...voximplantKeys.all, 'rules', providerId] }),
        },
    );
}

export function useCreateVoxUser() {
    const qc = useQueryClient();
    return useApiMutation(
        ({ providerId, ...data }: { providerId: string } & CreateVoxUserRequest) =>
            voximplantApi.createUser(providerId, data),
        {
            onSuccess: (_, { providerId }) =>
                qc.invalidateQueries({ queryKey: [...voximplantKeys.all, 'users', providerId] }),
        },
    );
}

export function useVoxProvision() {
    const qc = useQueryClient();
    return useApiMutation(
        (providerId: string) => voximplantApi.provision(providerId),
        {
            successMessage: 'Provisioning completed successfully',
            onSuccess: () =>
                qc.invalidateQueries({ queryKey: voximplantKeys.all }),
        },
    );
}

export function useVoxSyncUsers() {
    const qc = useQueryClient();
    return useApiMutation(
        ({ providerId }: { providerId: string }) =>
            voximplantApi.syncUsers(providerId),
        {
            successMessage: 'User synchronization completed',
            onSuccess: (_, { providerId }) => {
                qc.invalidateQueries({ queryKey: [...voximplantKeys.all, 'users', providerId] });
                qc.invalidateQueries({ queryKey: voximplantKeys.status(providerId) });
            },
        },
    );
}

