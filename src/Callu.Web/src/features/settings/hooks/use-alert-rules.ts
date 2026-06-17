import { useQuery, useQueryClient } from '@tanstack/react-query';
import { apiQueryOptions, useApiMutation } from '@/shared/api';
import { alertRulesApi } from '../api/alert-rules.api';
import type { CreateAlertRuleRequest, UpdateAlertRuleRequest, AlertRuleDto, AlertRuleMetadata } from '../types/alert-rules.types';

export const alertRuleKeys = {
    all: ['alert-rules'] as const,
    detail: (id: string) => [...alertRuleKeys.all, id] as const,
    metadata: () => [...alertRuleKeys.all, 'metadata'] as const,
};

export const alertRuleQueries = {
    list: () => apiQueryOptions<AlertRuleDto[]>(alertRuleKeys.all, () => alertRulesApi.getAll()),
    detail: (id: string) =>
        apiQueryOptions<AlertRuleDto>(alertRuleKeys.detail(id), () => alertRulesApi.getById(id), { enabled: !!id }),
    metadata: () =>
        apiQueryOptions<AlertRuleMetadata>(alertRuleKeys.metadata(), () => alertRulesApi.getMetadata(), {
            staleTime: 5 * 60_000,
        }),
};

export function useAlertRules() {
    return useQuery(alertRuleQueries.list());
}

export function useAlertRule(id: string) {
    return useQuery(alertRuleQueries.detail(id));
}

export function useCreateAlertRule() {
    const queryClient = useQueryClient();
    return useApiMutation<AlertRuleDto, CreateAlertRuleRequest>(
        (data) => alertRulesApi.create(data),
        {
            successMessage: 'Alert rule created',
            onSuccess: () => queryClient.invalidateQueries({ queryKey: alertRuleKeys.all }),
        },
    );
}

export function useUpdateAlertRule() {
    const queryClient = useQueryClient();
    return useApiMutation<void, { id: string; data: UpdateAlertRuleRequest }>(
        ({ id, data }) => alertRulesApi.update(id, data),
        {
            successMessage: 'Alert rule updated',
            onSuccess: () => queryClient.invalidateQueries({ queryKey: alertRuleKeys.all }),
        },
    );
}

export function useDeleteAlertRule() {
    const queryClient = useQueryClient();
    return useApiMutation<void, string>(
        (id) => alertRulesApi.delete(id),
        {
            successMessage: 'Alert rule deleted',
            onSuccess: () => queryClient.invalidateQueries({ queryKey: alertRuleKeys.all }),
        },
    );
}

export function useToggleAlertRule() {
    const queryClient = useQueryClient();
    return useApiMutation<{ message: string }, string>(
        (id) => alertRulesApi.toggle(id),
        {
            successMessage: 'Alert rule toggled',
            onSuccess: () => queryClient.invalidateQueries({ queryKey: alertRuleKeys.all }),
        },
    );
}

/** Fetch condition fields, operators, action types, severity values from backend */
export function useAlertRuleMetadata() {
    return useQuery(alertRuleQueries.metadata());
}
