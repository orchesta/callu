import { useQuery, useQueryClient } from '@tanstack/react-query';
import { apiQueryOptions, useApiMutation, ApiErrorCategory, getErrorMessage } from '@/shared/api';
import { toast } from '@/shared/utils/toast';
import { scheduleApi } from '../api/schedule.api';
import type {
    CreateScheduleRequest,
    CreateOverrideRequest,
    SaveSchedulePlanRequest,
} from '../types/schedule.types';

export const scheduleKeys = {
    all: ['schedules'] as const,
    lists: () => [...scheduleKeys.all, 'list'] as const,
    details: () => [...scheduleKeys.all, 'detail'] as const,
    detail: (id: string) => [...scheduleKeys.details(), id] as const,
    onCall: (id: string) => [...scheduleKeys.all, 'on-call', id] as const,
    occurrences: (id: string, days: number) =>
        [...scheduleKeys.all, 'occurrences', id, days] as const,
    coverage: (id: string, days: number) =>
        [...scheduleKeys.all, 'coverage', id, days] as const,
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
    coverage: (id: string, days: number) =>
        apiQueryOptions(
            scheduleKeys.coverage(id, days),
            () => scheduleApi.getCoverage(id, days),
            { enabled: !!id, staleTime: 60_000 },
        ),
};

export function useSchedules() {
    return useQuery(scheduleQueries.list());
}

export function useSchedule(id: string) {
    return useQuery(scheduleQueries.detail(id));
}

export function useScheduleOccurrences(id: string, days = 30) {
    return useQuery(scheduleQueries.occurrences(id, days));
}

export function useScheduleCoverage(id: string, days = 30) {
    return useQuery(scheduleQueries.coverage(id, days));
}

export function useCreateSchedule() {
    const qc = useQueryClient();
    return useApiMutation(
        (data: CreateScheduleRequest) => scheduleApi.create(data),
        { successMessage: 'Schedule created', onSuccess: () => qc.invalidateQueries({ queryKey: scheduleKeys.lists() }) }
    );
}

export function useDeleteSchedule() {
    const qc = useQueryClient();
    return useApiMutation(
        (id: string) => scheduleApi.delete(id),
        { successMessage: 'Schedule deleted', onSuccess: () => qc.invalidateQueries({ queryKey: scheduleKeys.lists() }) }
    );
}

/** Error categories where the plan may have committed even though the call failed, so the operator is
 * warned rather than told the save was rejected. Everything else is a considered server "no". */
const PLAN_MAY_HAVE_LANDED: ReadonlySet<ApiErrorCategory> = new Set([
    ApiErrorCategory.Server,
    ApiErrorCategory.Network,
    ApiErrorCategory.Timeout,
]);

export function useSaveSchedulePlan() {
    const qc = useQueryClient();
    return useApiMutation(
        ({ id, ...plan }: { id: string } & SaveSchedulePlanRequest) => scheduleApi.savePlan(id, plan),
        {
            successMessage: 'Schedule updated',
            errorMessage: false,
            onError: (error) => {
                if (!PLAN_MAY_HAVE_LANDED.has(error.category)) {
                    toast.error('Schedule not saved', getErrorMessage(error));
                    return;
                }

                toast.warning(
                    'Schedule save could not be confirmed',
                    'The plan may have been saved, but the on-call calendar could not be rebuilt — until it is, '
                    + 'the old rotation decides who gets paged. Callu retries this automatically; reload and check '
                    + 'who is on call before relying on the new plan.',
                );
            },
            onSettled: () => qc.invalidateQueries({ queryKey: scheduleKeys.all }),
        },
    );
}

export function useCreateOverride() {
    const qc = useQueryClient();
    return useApiMutation(
        (data: CreateOverrideRequest) => scheduleApi.createOverride(data),
        { successMessage: 'Override created', onSuccess: () => qc.invalidateQueries({ queryKey: scheduleKeys.all }) }
    );
}

export function useDeleteOverride() {
    const qc = useQueryClient();
    return useApiMutation(
        (overrideId: string) => scheduleApi.deleteOverride(overrideId),
        { successMessage: 'Override deleted', onSuccess: () => qc.invalidateQueries({ queryKey: scheduleKeys.all }) }
    );
}
