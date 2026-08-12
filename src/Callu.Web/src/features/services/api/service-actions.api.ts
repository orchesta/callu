import { apiClient } from '@/shared/api';
import type {
    ServiceActionDto,
    CreateServiceActionRequest,
    UpdateServiceActionRequest,
} from '../types/service.types';

const BASE = '/api/v1/services';

export const serviceActionsApi = {
    list: (serviceId: string) =>
        apiClient.get<ServiceActionDto[]>(`${BASE}/${serviceId}/actions`),
    create: (serviceId: string, data: CreateServiceActionRequest) =>
        apiClient.post<ServiceActionDto>(`${BASE}/${serviceId}/actions`, data),
    update: (serviceId: string, actionId: string, data: UpdateServiceActionRequest) =>
        apiClient.put<ServiceActionDto>(`${BASE}/${serviceId}/actions/${actionId}`, data),
    remove: (serviceId: string, actionId: string) =>
        apiClient.delete<void>(`${BASE}/${serviceId}/actions/${actionId}`),
};
