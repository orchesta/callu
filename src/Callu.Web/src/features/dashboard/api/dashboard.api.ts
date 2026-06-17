/**
 * Dashboard API module — connects to DashboardController endpoints.
 *
 * Endpoints:
 *   GET /api/v1/dashboard/summary         → DashboardSummary
 *   GET /api/v1/dashboard/incident-counts  → Record<string, number>
 *   GET /api/v1/dashboard/system-health    → ServiceHealthItem[]
 */

import { apiClient } from '@/shared/api';
import type {
    DashboardSummary,
    IncidentCountByStatus,
    ServiceHealthItem,
} from '../types/dashboard.types';

const BASE = '/api/v1/dashboard';

export const dashboardApi = {
    /** Full dashboard summary (counts, MTTA/MTTR, recent incidents) */
    getSummary: (recentCount?: number, timeRangeDays?: number) =>
        apiClient.get<DashboardSummary>(`${BASE}/summary`, {
            params: { 
                ...(recentCount ? { recentCount } : {}),
                ...(timeRangeDays ? { timeRangeDays } : {}),
            },
        }),

    /** Incident counts grouped by status */
    getIncidentCounts: () =>
        apiClient.get<IncidentCountByStatus>(`${BASE}/incident-counts`),

    /** Service catalog for system health overview */
    getSystemHealth: () =>
        apiClient.get<ServiceHealthItem[]>(`${BASE}/system-health`),
};
