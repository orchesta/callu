import { apiClient } from '@/shared/api';
import type { RunbookDto, CreateRunbookRequest, UpdateRunbookRequest } from '../types/runbooks.types';

const BASE = '/api/v1/runbooks';

export const runbooksApi = {
    getAll: () => apiClient.get<RunbookDto[]>(BASE),
    getById: (id: string) => apiClient.get<RunbookDto>(`${BASE}/${id}`),
    getByService: (serviceId: string) => apiClient.get<RunbookDto[]>(`${BASE}/by-service/${serviceId}`),
    create: (data: CreateRunbookRequest) => apiClient.post<RunbookDto>(BASE, data),
    update: (id: string, data: UpdateRunbookRequest) => apiClient.put<void>(`${BASE}/${id}`, data),
    markUsed: (id: string) => apiClient.post<{ message: string }>(`${BASE}/${id}/mark-used`),
    delete: (id: string) => apiClient.delete<void>(`${BASE}/${id}`),
};
