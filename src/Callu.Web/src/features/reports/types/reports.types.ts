export interface IncidentTrendPointDto {
    date: string;
    count: number;
    critical: number;
    high: number;
    medium: number;
    low: number;
}

export interface MttMetricPointDto {
    date: string;
    mttaMinutes: number;
    mttrMinutes: number;
    incidentCount: number;
}

export interface ServiceUptimeDto {
    serviceId: string;
    serviceName: string;
    uptimePercent: number;
    incidentCount: number;
    totalDowntimeMinutes: number;
}

export interface TeamPerformanceDto {
    teamId: string;
    teamName: string;
    totalIncidents: number;
    avgAcknowledgeMinutes: number;
    avgResolveMinutes: number;
    resolvedCount: number;
}

export interface SeverityDistributionDto {
    severity: string;
    count: number;
    percentage: number;
}
