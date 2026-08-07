import { useQuery } from '@tanstack/react-query';
import { apiQueryOptions } from '@/shared/api';
import { reportsApi } from '../api/reports.api';
import type {
    IncidentTrendPointDto,
    MttMetricPointDto,
    ServiceUptimeDto,
    TeamPerformanceDto,
    SeverityDistributionDto,
} from '../types/reports.types';

export const reportKeys = {
    all: ['reports'] as const,
    incidentTrends: (from: Date, to: Date, groupBy: string) =>
        [...reportKeys.all, 'incident-trends', from.toISOString(), to.toISOString(), groupBy] as const,
    mttMetrics: (from: Date, to: Date) =>
        [...reportKeys.all, 'mtt-metrics', from.toISOString(), to.toISOString()] as const,
    serviceUptime: (from: Date, to: Date) =>
        [...reportKeys.all, 'service-uptime', from.toISOString(), to.toISOString()] as const,
    teamPerformance: (from: Date, to: Date) =>
        [...reportKeys.all, 'team-performance', from.toISOString(), to.toISOString()] as const,
    severityDistribution: (from: Date, to: Date) =>
        [...reportKeys.all, 'severity-distribution', from.toISOString(), to.toISOString()] as const,
};

export const reportQueries = {
    incidentTrends: (from: Date, to: Date, groupBy = 'day') =>
        apiQueryOptions<IncidentTrendPointDto[]>(
            reportKeys.incidentTrends(from, to, groupBy),
            () => reportsApi.getIncidentTrends(from, to, groupBy),
        ),
    mttMetrics: (from: Date, to: Date) =>
        apiQueryOptions<MttMetricPointDto[]>(
            reportKeys.mttMetrics(from, to),
            () => reportsApi.getMttMetrics(from, to),
        ),
    serviceUptime: (from: Date, to: Date) =>
        apiQueryOptions<ServiceUptimeDto[]>(
            reportKeys.serviceUptime(from, to),
            () => reportsApi.getServiceUptime(from, to),
        ),
    teamPerformance: (from: Date, to: Date) =>
        apiQueryOptions<TeamPerformanceDto[]>(
            reportKeys.teamPerformance(from, to),
            () => reportsApi.getTeamPerformance(from, to),
        ),
    severityDistribution: (from: Date, to: Date) =>
        apiQueryOptions<SeverityDistributionDto[]>(
            reportKeys.severityDistribution(from, to),
            () => reportsApi.getSeverityDistribution(from, to),
        ),
};

export function useIncidentTrends(from: Date, to: Date, groupBy = 'day') {
    return useQuery(reportQueries.incidentTrends(from, to, groupBy));
}

export function useMttMetrics(from: Date, to: Date) {
    return useQuery(reportQueries.mttMetrics(from, to));
}

export function useServiceUptime(from: Date, to: Date) {
    return useQuery(reportQueries.serviceUptime(from, to));
}

export function useTeamPerformance(from: Date, to: Date) {
    return useQuery(reportQueries.teamPerformance(from, to));
}

export function useSeverityDistribution(from: Date, to: Date) {
    return useQuery(reportQueries.severityDistribution(from, to));
}
