import { useQuery, useQueryClient } from '@tanstack/react-query';
import { apiQueryOptions, useApiMutation } from '@/shared/api';
import { scheduleApi } from '../api/schedule.api';
import type {
    CreateScheduleRequest, UpdateScheduleRequest,
    CreateRotationRequest, UpdateRotationRequest,
    CreateOverrideRequest, UpdateOverrideRequest,
} from '../types/schedule.types';

export const scheduleKeys = {
    all: ['schedules'] as const,
    lists: () => [...scheduleKeys.all, 'list'] as const,
    details: () => [...scheduleKeys.all, 'detail'] as const,
    detail: (id: string) => [...scheduleKeys.details(), id] as const,
    onCall: (id: string) => [...scheduleKeys.all, 'on-call', id] as const,
    occurrences: (id: string, days: number) =>
        [...scheduleKeys.all, 'occurrences', id, days] as const,
};

export const scheduleQueries = {
    list: () => apiQueryOptions(scheduleKeys.lists(), () => scheduleApi.getAll(), { staleTime: 2 * 60_000 }),
    detail: (id: string) =>
        apiQueryOptions(scheduleKeys.detail(id), () => scheduleApi.getById(id), { enabled: !!id }),
    onCall: (id: string) =>
        apiQueryOptions(scheduleKeys.onCall(id), () => scheduleApi.getScheduleOnCall(id), {
            enabled: !!id,
            staleTime: 60_000,
            refetchInterval: 60_000,
        }),
    occurrences: (id: string, days: number) =>
        apiQueryOptions(
            scheduleKeys.occurrences(id, days),
            () => scheduleApi.getOccurrences(id, days),
            { enabled: !!id, staleTime: 60_000 },
        ),
};

export function useSchedules() {
    return useQuery(scheduleQueries.list());
}

export function useSchedule(id: string) {
    return useQuery(scheduleQueries.detail(id));
}

export function useScheduleOnCall(id: string) {
    return useQuery(scheduleQueries.onCall(id));
}

export function useScheduleOccurrences(id: string, days = 30) {
    return useQuery(scheduleQueries.occurrences(id, days));
}

export function useCreateSchedule() {
    const qc = useQueryClient();
    return useApiMutation(
        (data: CreateScheduleRequest) => scheduleApi.create(data),
        { successMessage: 'Schedule created', onSuccess: () => qc.invalidateQueries({ queryKey: scheduleKeys.lists() }) }
    );
}

export function useUpdateSchedule() {
    const qc = useQueryClient();
    return useApiMutation(
        ({ id, ...data }: { id: string } & UpdateScheduleRequest) => scheduleApi.update(id, data),
        { successMessage: 'Schedule updated', onSuccess: () => qc.invalidateQueries({ queryKey: scheduleKeys.all }) }
    );
}

export function useDeleteSchedule() {
    const qc = useQueryClient();
    return useApiMutation(
        (id: string) => scheduleApi.delete(id),
        { successMessage: 'Schedule deleted', onSuccess: () => qc.invalidateQueries({ queryKey: scheduleKeys.lists() }) }
    );
}

export function useAddRotation() {
    const qc = useQueryClient();
    return useApiMutation(
        ({ scheduleId, ...data }: { scheduleId: string } & CreateRotationRequest) =>
            scheduleApi.addRotation(scheduleId, data),
        { successMessage: 'Rotation added', onSuccess: () => qc.invalidateQueries({ queryKey: scheduleKeys.all }) }
    );
}

export function useUpdateRotation() {
    const qc = useQueryClient();
    return useApiMutation(
        ({ rotationId, ...data }: { rotationId: string } & UpdateRotationRequest) =>
            scheduleApi.updateRotation(rotationId, data),
        { successMessage: 'Rotation updated', onSuccess: () => qc.invalidateQueries({ queryKey: scheduleKeys.all }) }
    );
}

export function useDeleteRotation() {
    const qc = useQueryClient();
    return useApiMutation(
        (rotationId: string) => scheduleApi.deleteRotation(rotationId),
        { successMessage: 'Rotation deleted', onSuccess: () => qc.invalidateQueries({ queryKey: scheduleKeys.all }) }
    );
}

export function useCreateOverride() {
    const qc = useQueryClient();
    return useApiMutation(
        (data: CreateOverrideRequest) => scheduleApi.createOverride(data),
        { successMessage: 'Override created', onSuccess: () => qc.invalidateQueries({ queryKey: scheduleKeys.all }) }
    );
}

export function useUpdateOverride() {
    const qc = useQueryClient();
    return useApiMutation(
        ({ overrideId, ...data }: { overrideId: string } & UpdateOverrideRequest) =>
            scheduleApi.updateOverride(overrideId, data),
        { successMessage: 'Override updated', onSuccess: () => qc.invalidateQueries({ queryKey: scheduleKeys.all }) }
    );
}

export function useDeleteOverride() {
    const qc = useQueryClient();
    return useApiMutation(
        (overrideId: string) => scheduleApi.deleteOverride(overrideId),
        { successMessage: 'Override deleted', onSuccess: () => qc.invalidateQueries({ queryKey: scheduleKeys.all }) }
    );
}
