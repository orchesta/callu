/**
 * Call Log React Query hooks.
 */

import { useQuery } from '@tanstack/react-query';
import { apiQueryOptions } from '@/shared/api';
import { callLogApi } from '../api/call-log.api';

export const callLogKeys = {
    all: ['call-logs'] as const,
    lists: () => [...callLogKeys.all, 'list'] as const,
    list: (page: number, pageSize: number) => [...callLogKeys.lists(), page, pageSize] as const,
    byIncident: (incidentId: string) => [...callLogKeys.all, 'incident', incidentId] as const,
    timeline: (incidentId: string) => [...callLogKeys.all, 'timeline', incidentId] as const,
};

export const callLogQueries = {
    list: (page: number, pageSize: number) =>
        apiQueryOptions(callLogKeys.list(page, pageSize), () => callLogApi.getAll(page, pageSize), { staleTime: 30_000 }),
    byIncident: (incidentId: string) =>
        apiQueryOptions(callLogKeys.byIncident(incidentId), () => callLogApi.getByIncident(incidentId), {
            enabled: !!incidentId,
        }),
    timeline: (incidentId: string) =>
        apiQueryOptions(callLogKeys.timeline(incidentId), () => callLogApi.getTimeline(incidentId), {
            enabled: !!incidentId,
        }),
};

/** Paginated call logs */
export function useCallLogs(page = 1, pageSize = 25) {
    return useQuery(callLogQueries.list(page, pageSize));
}

/** Call logs by incident */
export function useCallLogsByIncident(incidentId: string) {
    return useQuery(callLogQueries.byIncident(incidentId));
}

/** Timeline events for incident */
export function useCallTimeline(incidentId: string) {
    return useQuery(callLogQueries.timeline(incidentId));
}
