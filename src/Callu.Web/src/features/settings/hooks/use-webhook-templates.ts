/**
 * Webhook Templates React Query hooks.
 */

import { useQuery, useQueryClient } from '@tanstack/react-query';
import { apiQueryOptions, useApiMutation } from '@/shared/api';
import { webhookTemplateApi } from '../api/webhook-templates.api';
import type { CreateWebhookTemplateRequest, UpdateWebhookTemplateRequest } from '../types/webhook-template.types';

export const webhookTemplateKeys = {
    all: ['webhook-templates'] as const,
    lists: () => [...webhookTemplateKeys.all, 'list'] as const,
    detail: (id: string) => [...webhookTemplateKeys.all, 'detail', id] as const,
};

export const webhookTemplateQueries = {
    list: () =>
        apiQueryOptions(webhookTemplateKeys.lists(), () => webhookTemplateApi.getAll(), { staleTime: 5 * 60_000 }),
    detail: (id: string) =>
        apiQueryOptions(webhookTemplateKeys.detail(id), () => webhookTemplateApi.getById(id), { enabled: !!id }),
};

export function useWebhookTemplates() {
    return useQuery(webhookTemplateQueries.list());
}

export function useWebhookTemplate(id: string) {
    return useQuery(webhookTemplateQueries.detail(id));
}

export function useCreateWebhookTemplate() {
    const qc = useQueryClient();
    return useApiMutation(
        (data: CreateWebhookTemplateRequest) => webhookTemplateApi.create(data),
        {
            onSuccess: () =>
                qc.invalidateQueries({ queryKey: webhookTemplateKeys.lists() }),
        },
    );
}

export function useUpdateWebhookTemplate() {
    const qc = useQueryClient();
    return useApiMutation(
        ({ id, ...data }: { id: string } & UpdateWebhookTemplateRequest) =>
            webhookTemplateApi.update(id, data),
        {
            onSuccess: () =>
                qc.invalidateQueries({ queryKey: webhookTemplateKeys.all }),
        },
    );
}

export function useDeleteWebhookTemplate() {
    const qc = useQueryClient();
    return useApiMutation(
        (id: string) => webhookTemplateApi.delete(id),
        {
            onSuccess: () =>
                qc.invalidateQueries({ queryKey: webhookTemplateKeys.lists() }),
        },
    );
}

export function useCreateWebhookTemplateFromCapture() {
    const qc = useQueryClient();
    return useApiMutation(
        ({ captureId, ...data }: { captureId: string } & CreateWebhookTemplateRequest) =>
            webhookTemplateApi.createFromCapture(captureId, data),
        {
            onSuccess: () =>
                qc.invalidateQueries({ queryKey: webhookTemplateKeys.lists() }),
        },
    );
}

export function useTestWebhookTemplate() {
    return useApiMutation(
        ({ id, samplePayload }: { id: string; samplePayload: string }) =>
            webhookTemplateApi.test(id, samplePayload),
    );
}
