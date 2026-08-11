import { apiClient } from '@/shared/api';
import type {
    IntegrationDto,
    IntegrationSecretsDto,
    CreateIntegrationRequest,
    UpdateIntegrationRequest,
    BindIntegrationServiceRequest,
} from '../types/integrations.types';

const BASE = '/api/v1/integrations';

export const integrationsApi = {
    getAll: (serviceId?: string) =>
        apiClient.get<IntegrationDto[]>(BASE, {
            params: serviceId ? { serviceId } : undefined,
        }),

    getById: (id: string) =>
        apiClient.get<IntegrationDto>(`${BASE}/${id}`),

    create: (data: CreateIntegrationRequest) =>
        apiClient.post<IntegrationSecretsDto>(BASE, data),

    update: (id: string, data: UpdateIntegrationRequest) =>
        apiClient.put<IntegrationDto>(`${BASE}/${id}`, data),

    delete: (id: string) =>
        apiClient.delete<void>(`${BASE}/${id}`),

    rotateCredentials: (id: string) =>
        apiClient.post<IntegrationSecretsDto>(`${BASE}/${id}/rotate-credentials`),

    bindService: (id: string, serviceId: string | null) =>
        apiClient.put<IntegrationDto>(
            `${BASE}/${id}/service`,
            { serviceId } satisfies BindIntegrationServiceRequest,
        ),
};
