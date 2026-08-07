/**
 * Reports API module — connects to ReportsController
 */

import { apiClient } from '@/shared/api';
import type {
    IncidentTrendPointDto,
    MttMetricPointDto,
    ServiceUptimeDto,
    TeamPerformanceDto,
    SeverityDistributionDto,
} from '../types/reports.types';

const BASE = '/api/v1/reports';

function dateParam(d: Date) {
    return d.toISOString();
}

export const reportsApi = {
    /** GET /reports/incident-trends */
    getIncidentTrends: (from: Date, to: Date, groupBy = 'day') =>
        apiClient.get<IncidentTrendPointDto[]>(
            `${BASE}/incident-trends?from=${dateParam(from)}&to=${dateParam(to)}&groupBy=${groupBy}`,
        ),

    /** GET /reports/mtt-metrics */
    getMttMetrics: (from: Date, to: Date) =>
        apiClient.get<MttMetricPointDto[]>(
            `${BASE}/mtt-metrics?from=${dateParam(from)}&to=${dateParam(to)}`,
        ),

    /** GET /reports/service-uptime */
    getServiceUptime: (from: Date, to: Date) =>
        apiClient.get<ServiceUptimeDto[]>(
            `${BASE}/service-uptime?from=${dateParam(from)}&to=${dateParam(to)}`,
        ),

    /** GET /reports/team-performance */
    getTeamPerformance: (from: Date, to: Date) =>
        apiClient.get<TeamPerformanceDto[]>(
            `${BASE}/team-performance?from=${dateParam(from)}&to=${dateParam(to)}`,
        ),

    /** GET /reports/severity-distribution */
    getSeverityDistribution: (from: Date, to: Date) =>
        apiClient.get<SeverityDistributionDto[]>(
            `${BASE}/severity-distribution?from=${dateParam(from)}&to=${dateParam(to)}`,
        ),
};
