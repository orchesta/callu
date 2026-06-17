import { useQuery, useQueryClient } from '@tanstack/react-query';
import { apiQueryOptions, useApiMutation } from '@/shared/api';
import { escalationApi } from '../api/escalation.api';
import type {
    CreateEscalationRequest,
    UpdateEscalationRequest,
    AddStepRequest,
    UpdateStepRequest,
} from '../types/escalation.types';

export const escalationKeys = {
    all: ['escalations'] as const,
    lists: () => [...escalationKeys.all, 'list'] as const,
    details: () => [...escalationKeys.all, 'detail'] as const,
    detail: (id: string) => [...escalationKeys.details(), id] as const,
};

export const escalationQueries = {
    list: () => apiQueryOptions(escalationKeys.lists(), () => escalationApi.getAll(), { staleTime: 2 * 60_000 }),
    detail: (id: string) =>
        apiQueryOptions(escalationKeys.detail(id), () => escalationApi.getById(id), { enabled: !!id }),
};

export function useEscalationPolicies() {
    return useQuery(escalationQueries.list());
}

export function useEscalationPolicy(id: string) {
    return useQuery(escalationQueries.detail(id));
}

export function useCreatePolicy() {
    const qc = useQueryClient();
    return useApiMutation(
        (data: CreateEscalationRequest) => escalationApi.create(data),
        {
            successMessage: 'Escalation policy created',
            onSuccess: () => qc.invalidateQueries({ queryKey: escalationKeys.lists() }),
        }
    );
}

export function useUpdatePolicy() {
    const qc = useQueryClient();
    return useApiMutation(
        ({ id, ...data }: { id: string } & UpdateEscalationRequest) => escalationApi.update(id, data),
        {
            successMessage: 'Policy updated',
            onSuccess: () => qc.invalidateQueries({ queryKey: escalationKeys.all }),
        }
    );
}

export function useDeletePolicy() {
    const qc = useQueryClient();
    return useApiMutation(
        (id: string) => escalationApi.delete(id),
        {
            successMessage: 'Policy deleted',
            onSuccess: () => qc.invalidateQueries({ queryKey: escalationKeys.lists() }),
        }
    );
}

export function useAddStep() {
    const qc = useQueryClient();
    return useApiMutation(
        ({ policyId, ...data }: { policyId: string } & AddStepRequest) =>
            escalationApi.addStep(policyId, data),
        {
            successMessage: 'Step added',
            onSuccess: (_, { policyId }) =>
                qc.invalidateQueries({ queryKey: escalationKeys.detail(policyId) }),
        }
    );
}

export function useUpdateStep() {
    const qc = useQueryClient();
    return useApiMutation(
        ({ policyId, stepId, ...data }: { policyId: string; stepId: string } & UpdateStepRequest) =>
            escalationApi.updateStep(policyId, stepId, data),
        {
            successMessage: 'Step updated',
            onSuccess: (_, { policyId }) =>
                qc.invalidateQueries({ queryKey: escalationKeys.detail(policyId) }),
        }
    );
}

export function useRemoveStep() {
    const qc = useQueryClient();
    return useApiMutation(
        ({ policyId, stepId }: { policyId: string; stepId: string }) =>
            escalationApi.removeStep(policyId, stepId),
        {
            successMessage: 'Step removed',
            onSuccess: (_, { policyId }) =>
                qc.invalidateQueries({ queryKey: escalationKeys.detail(policyId) }),
        }
    );
}

export function useReorderSteps() {
    const qc = useQueryClient();
    return useApiMutation(
        ({ policyId, stepIds }: { policyId: string; stepIds: string[] }) =>
            escalationApi.reorderSteps(policyId, stepIds),
        {
            successMessage: 'Steps reordered',
            onSuccess: (_, { policyId }) =>
                qc.invalidateQueries({ queryKey: escalationKeys.detail(policyId) }),
        }
    );
}
