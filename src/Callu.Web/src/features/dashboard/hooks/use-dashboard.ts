/**
 * Dashboard React Query hooks.
 *
 * Uses apiQueryOptions + useQuery (ApiResponse unwrap).
 * Components get clean typed data directly.
 */

import { useQuery } from '@tanstack/react-query';
import { apiQueryOptions } from '@/shared/api';
import { dashboardApi } from '../api/dashboard.api';

export const dashboardKeys = {
    all: ['dashboard'] as const,
    summary: (recentCount?: number, timeRangeDays?: number) =>
        [...dashboardKeys.all, 'summary', recentCount, timeRangeDays] as const,
    incidentCounts: () =>
        [...dashboardKeys.all, 'incident-counts'] as const,
    systemHealth: () =>
        [...dashboardKeys.all, 'system-health'] as const,
};

export const dashboardQueries = {
    summary: (recentCount?: number, timeRangeDays?: number) =>
        apiQueryOptions(dashboardKeys.summary(recentCount, timeRangeDays), () => dashboardApi.getSummary(recentCount, timeRangeDays), {
            staleTime: 60_000,
            refetchInterval: 60_000,
        }),
    incidentCounts: () =>
        apiQueryOptions(dashboardKeys.incidentCounts(), () => dashboardApi.getIncidentCounts(), { staleTime: 60_000 }),
    systemHealth: () =>
        apiQueryOptions(dashboardKeys.systemHealth(), () => dashboardApi.getSystemHealth(), {
            staleTime: 30_000,
            refetchInterval: 30_000,
        }),
};

/** Full dashboard summary — auto-refreshes every 60s */
export function useDashboardSummary(recentCount?: number, timeRangeDays?: number) {
    return useQuery(dashboardQueries.summary(recentCount, timeRangeDays));
}

/** Incident counts by status */
export function useIncidentCounts() {
    return useQuery(dashboardQueries.incidentCounts());
}

/** System health — all services with uptime info, refreshes every 30s */
export function useSystemHealth() {
    return useQuery(dashboardQueries.systemHealth());
}
