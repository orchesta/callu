import { apiClient } from '@/shared/api';
import type { PostmortemDto, CreatePostmortemRequest, UpdatePostmortemRequest } from '../types/postmortems.types';

const BASE = '/api/v1/postmortems';

export const postmortemsApi = {
    getAll: () => apiClient.get<PostmortemDto[]>(BASE),
    getById: (id: string) => apiClient.get<PostmortemDto>(`${BASE}/${id}`),
    getByIncident: (incidentId: string) => apiClient.get<PostmortemDto[]>(`${BASE}/by-incident/${incidentId}`),
    create: (data: CreatePostmortemRequest) => apiClient.post<PostmortemDto>(BASE, data),
    update: (id: string, data: UpdatePostmortemRequest) => apiClient.put<void>(`${BASE}/${id}`, data),
    submit: (id: string) => apiClient.post<{ message: string }>(`${BASE}/${id}/submit`),
    reject: (id: string) => apiClient.post<{ message: string }>(`${BASE}/${id}/reject`),
    publish: (id: string) => apiClient.post<{ message: string }>(`${BASE}/${id}/publish`),
    lock: (id: string) => apiClient.post<{ message: string }>(`${BASE}/${id}/lock`),
    delete: (id: string) => apiClient.delete<void>(`${BASE}/${id}`),
};
