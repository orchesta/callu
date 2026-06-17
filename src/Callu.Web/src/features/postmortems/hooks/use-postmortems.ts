import { useQuery, useQueryClient } from '@tanstack/react-query';
import { apiQueryOptions, useApiMutation } from '@/shared/api';
import { postmortemsApi } from '../api/postmortems.api';
import type { PostmortemDto, CreatePostmortemRequest, UpdatePostmortemRequest } from '../types/postmortems.types';

export const postmortemKeys = {
    all: ['postmortems'] as const,
    detail: (id: string) => [...postmortemKeys.all, id] as const,
};

export const postmortemQueries = {
    list: () => apiQueryOptions<PostmortemDto[]>(postmortemKeys.all, () => postmortemsApi.getAll()),
    detail: (id: string) =>
        apiQueryOptions<PostmortemDto>(postmortemKeys.detail(id), () => postmortemsApi.getById(id), { enabled: !!id }),
};

export function usePostmortems() {
    return useQuery(postmortemQueries.list());
}

export function usePostmortem(id: string) {
    return useQuery(postmortemQueries.detail(id));
}

export function useCreatePostmortem() {
    const qc = useQueryClient();
    return useApiMutation<PostmortemDto, CreatePostmortemRequest>(
        (data) => postmortemsApi.create(data),
        { successMessage: 'Postmortem created', onSuccess: () => qc.invalidateQueries({ queryKey: postmortemKeys.all }) },
    );
}

export function useUpdatePostmortem() {
    const qc = useQueryClient();
    return useApiMutation<void, { id: string; data: UpdatePostmortemRequest }>(
        ({ id, data }) => postmortemsApi.update(id, data),
        { successMessage: 'Postmortem saved', onSuccess: () => qc.invalidateQueries({ queryKey: postmortemKeys.all }) },
    );
}

export function useSubmitPostmortem() {
    const qc = useQueryClient();
    return useApiMutation<{ message: string }, string>(
        (id) => postmortemsApi.submit(id),
        { successMessage: 'Postmortem submitted for review', onSuccess: () => qc.invalidateQueries({ queryKey: postmortemKeys.all }) },
    );
}

export function useRejectPostmortem() {
    const qc = useQueryClient();
    return useApiMutation<{ message: string }, string>(
        (id) => postmortemsApi.reject(id),
        { successMessage: 'Postmortem returned to draft', onSuccess: () => qc.invalidateQueries({ queryKey: postmortemKeys.all }) },
    );
}

export function usePublishPostmortem() {
    const qc = useQueryClient();
    return useApiMutation<{ message: string }, string>(
        (id) => postmortemsApi.publish(id),
        { successMessage: 'Postmortem published', onSuccess: () => qc.invalidateQueries({ queryKey: postmortemKeys.all }) },
    );
}

export function useLockPostmortem() {
    const qc = useQueryClient();
    return useApiMutation<{ message: string }, string>(
        (id) => postmortemsApi.lock(id),
        { successMessage: 'Postmortem locked', onSuccess: () => qc.invalidateQueries({ queryKey: postmortemKeys.all }) },
    );
}

export function useDeletePostmortem() {
    const qc = useQueryClient();
    return useApiMutation<void, string>(
        (id) => postmortemsApi.delete(id),
        { successMessage: 'Postmortem deleted', onSuccess: () => qc.invalidateQueries({ queryKey: postmortemKeys.all }) },
    );
}
