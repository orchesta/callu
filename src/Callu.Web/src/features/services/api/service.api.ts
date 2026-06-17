import { apiClient } from '@/shared/api';
import type { PagedResult } from '@/shared/types/common.types';
import type {
    ServiceDto,
    ServiceListDto,
    ServiceDependencyDto,
    CreateServiceRequest,
    UpdateServiceRequest,
    CreateServiceDependencyRequest,
} from '../types/service.types';

const BASE = '/api/v1/services';

export const serviceApi = {
    getAll: (page = 1, pageSize = 20) =>
        apiClient.get<PagedResult<ServiceListDto>>(BASE, { params: { page, pageSize } }),
    getById: (id: string) =>
        apiClient.get<ServiceDto>(`${BASE}/${id}`),
    create: (data: CreateServiceRequest) =>
        apiClient.post<ServiceDto>(BASE, data),
    update: (id: string, data: UpdateServiceRequest) =>
        apiClient.put<ServiceDto>(`${BASE}/${id}`, data),
    delete: (id: string) =>
        apiClient.delete<void>(`${BASE}/${id}`),

    getDependencies: (serviceId: string) =>
        apiClient.get<ServiceDependencyDto[]>(`${BASE}/${serviceId}/dependencies`),
    addDependency: (serviceId: string, data: CreateServiceDependencyRequest) =>
        apiClient.post<ServiceDependencyDto>(`${BASE}/${serviceId}/dependencies`, data),
    removeDependency: (dependencyId: string) =>
        apiClient.delete<void>(`${BASE}/dependencies/${dependencyId}`),

    getHealth: (id: string) =>
        apiClient.get<{ healthScore: number; lastChecked: string }>(`${BASE}/${id}/health`),
};
