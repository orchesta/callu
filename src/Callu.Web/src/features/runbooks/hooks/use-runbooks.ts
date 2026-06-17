import { useQuery, useQueryClient } from '@tanstack/react-query';
import { apiQueryOptions, useApiMutation } from '@/shared/api';
import { runbooksApi } from '../api/runbooks.api';
import type { RunbookDto, CreateRunbookRequest, UpdateRunbookRequest } from '../types/runbooks.types';

export const runbookKeys = {
    all: ['runbooks'] as const,
    detail: (id: string) => [...runbookKeys.all, id] as const,
};

export const runbookQueries = {
    list: () => apiQueryOptions<RunbookDto[]>(runbookKeys.all, () => runbooksApi.getAll()),
    detail: (id: string) =>
        apiQueryOptions<RunbookDto>(runbookKeys.detail(id), () => runbooksApi.getById(id), { enabled: !!id }),
};

export function useRunbooks() {
    return useQuery(runbookQueries.list());
}

export function useRunbook(id: string) {
    return useQuery(runbookQueries.detail(id));
}

export function useCreateRunbook() {
    const qc = useQueryClient();
    return useApiMutation<RunbookDto, CreateRunbookRequest>(
        (data) => runbooksApi.create(data),
        { successMessage: 'Runbook created', onSuccess: () => qc.invalidateQueries({ queryKey: runbookKeys.all }) },
    );
}

export function useUpdateRunbook() {
    const qc = useQueryClient();
    return useApiMutation<void, { id: string; data: UpdateRunbookRequest }>(
        ({ id, data }) => runbooksApi.update(id, data),
        { successMessage: 'Runbook saved', onSuccess: () => qc.invalidateQueries({ queryKey: runbookKeys.all }) },
    );
}

export function useMarkRunbookUsed() {
    const qc = useQueryClient();
    return useApiMutation<{ message: string }, string>(
        (id) => runbooksApi.markUsed(id),
        { successMessage: 'Usage recorded', onSuccess: () => qc.invalidateQueries({ queryKey: runbookKeys.all }) },
    );
}

export function useDeleteRunbook() {
    const qc = useQueryClient();
    return useApiMutation<void, string>(
        (id) => runbooksApi.delete(id),
        { successMessage: 'Runbook deleted', onSuccess: () => qc.invalidateQueries({ queryKey: runbookKeys.all }) },
    );
}
