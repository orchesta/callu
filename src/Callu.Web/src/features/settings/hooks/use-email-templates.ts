/**
 * Email Templates React Query hooks.
 */

import { useQuery, useQueryClient } from '@tanstack/react-query';
import { apiQueryOptions, useApiMutation } from '@/shared/api';
import { emailTemplateApi } from '../api/email-templates.api';
import type {
    CreateEmailTemplateRequest,
    UpdateEmailTemplateRequest,
} from '../types/email-template.types';

export const emailTemplateKeys = {
    all: ['email-templates'] as const,
    list: () => [...emailTemplateKeys.all, 'list'] as const,
    detail: (id: string) => [...emailTemplateKeys.all, 'detail', id] as const,
};

export const emailTemplateQueries = {
    list: () => apiQueryOptions(emailTemplateKeys.list(), () => emailTemplateApi.getAll(), { staleTime: 5 * 60_000 }),
    detail: (id: string) =>
        apiQueryOptions(emailTemplateKeys.detail(id), () => emailTemplateApi.getById(id), { enabled: !!id }),
};

export function useEmailTemplates() {
    return useQuery(emailTemplateQueries.list());
}

export function useEmailTemplate(id: string) {
    return useQuery(emailTemplateQueries.detail(id));
}

export function useCreateEmailTemplate() {
    const qc = useQueryClient();
    return useApiMutation(
        (data: CreateEmailTemplateRequest) => emailTemplateApi.create(data),
        {
            onSuccess: () => qc.invalidateQueries({ queryKey: emailTemplateKeys.list() }),
        },
    );
}

export function useUpdateEmailTemplate() {
    const qc = useQueryClient();
    return useApiMutation(
        ({ id, ...data }: { id: string } & UpdateEmailTemplateRequest) =>
            emailTemplateApi.update(id, data),
        {
            onSuccess: (_, { id }) => {
                qc.invalidateQueries({ queryKey: emailTemplateKeys.list() });
                qc.invalidateQueries({ queryKey: emailTemplateKeys.detail(id) });
            },
        },
    );
}

export function useDeleteEmailTemplate() {
    const qc = useQueryClient();
    return useApiMutation(
        (id: string) => emailTemplateApi.delete(id),
        {
            onSuccess: () => qc.invalidateQueries({ queryKey: emailTemplateKeys.list() }),
        },
    );
}

export function usePreviewEmailTemplate() {
    return useApiMutation(
        ({ id, variables }: { id: string; variables: Record<string, string> }) =>
            emailTemplateApi.preview(id, variables),
    );
}

export function useSendTestEmail() {
    return useApiMutation(
        ({ id, email }: { id: string; email: string }) =>
            emailTemplateApi.sendTest(id, email),
    );
}
