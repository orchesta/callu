import { apiClient } from '@/shared/api';
import type {
    ScheduleDto, ScheduleDetailDto, OnCallStatusDto,
    CreateScheduleRequest, UpdateScheduleRequest,
    ScheduleRotationDto, CreateRotationRequest, UpdateRotationRequest,
    OnCallOverrideDto, CreateOverrideRequest, UpdateOverrideRequest,
} from '../types/schedule.types';

const BASE = '/api/v1/schedules';

export const scheduleApi = {
    getAll: () => apiClient.get<ScheduleDto[]>(BASE),
    getById: (id: string) => apiClient.get<ScheduleDetailDto>(`${BASE}/${id}`),
    create: (data: CreateScheduleRequest) => apiClient.post<ScheduleDto>(BASE, data),
    update: (id: string, data: UpdateScheduleRequest) => apiClient.put<void>(`${BASE}/${id}`, data),
    delete: (id: string) => apiClient.delete<void>(`${BASE}/${id}`),

    getRotations: (scheduleId: string) =>
        apiClient.get<ScheduleRotationDto[]>(`${BASE}/${scheduleId}/rotations`),
    addRotation: (scheduleId: string, data: CreateRotationRequest) =>
        apiClient.post<ScheduleRotationDto>(`${BASE}/${scheduleId}/rotations`, data),
    updateRotation: (rotationId: string, data: UpdateRotationRequest) =>
        apiClient.put<void>(`${BASE}/rotations/${rotationId}`, data),
    deleteRotation: (rotationId: string) =>
        apiClient.delete<void>(`${BASE}/rotations/${rotationId}`),

    getOverrides: (scheduleId: string) =>
        apiClient.get<OnCallOverrideDto[]>(`${BASE}/${scheduleId}/overrides`),
    createOverride: (data: CreateOverrideRequest) =>
        apiClient.post<OnCallOverrideDto>(`${BASE}/overrides`, data),
    updateOverride: (overrideId: string, data: UpdateOverrideRequest) =>
        apiClient.put<void>(`${BASE}/overrides/${overrideId}`, data),
    deleteOverride: (overrideId: string) =>
        apiClient.delete<void>(`${BASE}/overrides/${overrideId}`),

    getScheduleOnCall: (id: string) =>
        apiClient.get<OnCallStatusDto>(`${BASE}/${id}/on-call`),

    getOccurrences: (scheduleId: string, days = 30) =>
        apiClient.get<ScheduleRotationDto[]>(`${BASE}/${scheduleId}/occurrences?days=${days}`),
};
