/**
 * Status Page Types — mirrors BE DTOs.
 */

/** Mirrors BE StatusPageDto (list item) */
export interface StatusPageDto {
    id: string;
    name: string;
    slug: string;
    isPublic: boolean;
    overallStatus: string;
    createdAt: string;
}

/** Mirrors BE StatusPageDetailDto (includes components + incidents) */
export interface StatusPageDetailDto extends StatusPageDto {
    description?: string;
    logoUrl?: string;
    customDomain?: string;
    supportEmail?: string;
    allowSubscriptions: boolean;
    components: StatusPageComponentDto[];
    incidents: StatusPageIncidentDto[];
}

/** Mirrors BE StatusPageComponentDto */
export interface StatusPageComponentDto {
    id: string;
    name: string;
    description?: string;
    status: string;
    displayOrder: number;
    serviceId?: string;
    healthCheckEnabled: boolean;
    healthCheckUrl?: string;
    healthCheckHttpMethod?: string;
    healthCheckIntervalSeconds: number;
    healthCheckTimeoutSeconds: number;
    lastHealthCheckAt?: string;
    lastHealthCheckResult?: string;
    lastHealthCheckResponseMs?: number;
    healthCheckConsecutiveFailures: number;
    healthCheckSampleResponse?: string;
    healthCheckFieldMappings?: string;
    healthCheckStateMapping?: string;
    healthCheckListeningMode: boolean;
}

/** Mirrors BE StatusPageIncidentDto */
export interface StatusPageIncidentDto {
    id: string;
    title: string;
    status: string;
    impact: string;
    createdAt: string;
    updates: StatusPageIncidentUpdateDto[];
}

/** Mirrors BE StatusPageIncidentUpdateDto */
export interface StatusPageIncidentUpdateDto {
    id: string;
    message: string;
    status: string;
    createdAt: string;
}

/** Mirrors BE CreateStatusPageRequest */
export interface CreateStatusPageRequest {
    name: string;
    slug: string;
    description?: string;
}

/** Mirrors BE UpdateStatusPageRequest */
export interface UpdateStatusPageRequest {
    name?: string;
    description?: string;
    slug?: string;
    isPublic?: boolean;
    supportEmail?: string;
    allowSubscriptions?: boolean;
}

/** Mirrors BE AddComponentRequest */
export interface AddComponentRequest {
    name: string;
    description?: string;
    serviceId?: string;
    healthCheckEnabled?: boolean;
    healthCheckUrl?: string;
    healthCheckHttpMethod?: string;
    healthCheckIntervalSeconds?: number;
    healthCheckTimeoutSeconds?: number;
    healthCheckHeaders?: string;
    healthCheckBody?: string;
    healthCheckContentType?: string;
    healthCheckFieldMappings?: string;
    healthCheckStateMapping?: string;
}

/** Mirrors BE UpdateComponentRequest */
export interface UpdateComponentRequest {
    name?: string;
    status?: string;
    displayOrder?: number;
    healthCheckEnabled?: boolean;
    healthCheckUrl?: string;
    healthCheckHttpMethod?: string;
    healthCheckIntervalSeconds?: number;
    healthCheckTimeoutSeconds?: number;
    healthCheckHeaders?: string;
    healthCheckBody?: string;
    healthCheckContentType?: string;
    healthCheckFieldMappings?: string;
    healthCheckStateMapping?: string;
}

/** Mirrors BE CreateStatusIncidentRequest */
export interface CreateStatusIncidentRequest {
    title: string;
    status: string;
    impact?: string;
}

/** Mirrors BE AddIncidentUpdateRequest */
export interface AddIncidentUpdateRequest {
    message: string;
    status: string;
}

/** Mirrors BE StatusPageStatsDto */
export interface StatusPageStatsDto {
    componentCount: number;
    activeIncidentCount: number;
    pageViews: number;
    subscriberCount: number;
}

/** Mirrors BE StatusPageSubscriberDto (admin) */
export interface StatusPageSubscriberDto {
    id: string;
    email: string;
    isConfirmed: boolean;
    subscribedAt: string;
}

/** Mirrors BE HealthCheckResultDto */
export interface HealthCheckResultDto {
    componentId: string;
    status: string;
    responseMs?: number;
    message?: string;
    checkedAt: string;
}

/** Mirrors BE HealthCheckSnifferResultDto */
export interface HealthCheckSnifferResultDto {
    componentId: string;
    httpStatusCode: number;
    responseBody?: string;
    contentType?: string;
    responseMs: number;
    responseHeaders: Record<string, string>;
}

/** Mirrors BE UptimeDayDto */
export interface UptimeDayDto {
    date: string;
    status: 'operational' | 'degraded' | 'partial_outage' | 'major_outage' | 'maintenance' | 'no_data';
    uptimePercent: number | null;
}

/** Mirrors BE ComponentUptimeDto */
export interface ComponentUptimeDto {
    componentId: string;
    componentName: string;
    currentStatus: string;
    averageUptimePercent: number;
    days: UptimeDayDto[];
}
