/**
 * Dashboard types — mirrors BE DTOs from Callu.Shared.Models.Dashboard
 */

/** Lightweight incident item for dashboard lists (BE: IncidentListItemDto) */
export interface IncidentListItem {
    id: string;
    title: string;
    severity: string;
    status: string;
    startedAt: string;
    acknowledgedAt?: string;
    resolvedAt?: string;
    serviceName?: string;
    teamName?: string;
    acknowledgedBy?: string;
    resolvedBy?: string;
}

/** Service summary for system health (BE: ServiceDto) */
export interface ServiceHealthItem {
    id: string;
    name: string;
    description?: string;
    type: string;
    status: string;
    environment?: string;
    uptime: number;
    color?: string;
    icon?: string;
    isPublic: boolean;
    displayOrder: number;
    teamId?: string;
    teamName?: string;
    incidentCount: number;
    webhookEnabled?: boolean;
    createdAt: string;
}

export interface DashboardSummary {
    /** Incident counts by status */
    triggeredCount: number;
    acknowledgedCount: number;
    resolvedCount: number;
    totalIncidents: number;
    criticalCount: number;
    resolvedRate: number;

    /** Performance metrics (formatted strings from BE, e.g. "12m") */
    mtta: string;
    mttr: string;

    /** Severity breakdown: { "Critical": 5, "High": 3, ... } */
    severityCounts: Record<string, number>;

    /** Recent incidents (already limited by BE) */
    recentIncidents: IncidentListItem[];

    /** Service catalog */
    totalServices: number;
    services: ServiceHealthItem[];
}

/** Status-based incident counts, keyed by IncidentStatus string */
export type IncidentCountByStatus = Record<string, number>;
