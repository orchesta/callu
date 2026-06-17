/**
 * Call Log Types — mirrors BE DTOs from Callu.Shared.Models.Communication
 */

/** Mirrors BE CallLogDto */
export interface CallLogDto {
    id: string;
    incidentId: string;
    incidentTitle: string;
    phoneNumber: string;
    calledPersonName?: string;
    status: string;
    durationSeconds: number;
    attemptNumber: number;
    failureReason?: string;
    initiatedAt: string;
    completedAt?: string;
    formattedDuration: string;
}

/** Query params for the paginated getAll endpoint */
export interface CallLogFilters {
    page?: number;
    pageSize?: number;
}

/** Paginated response shape from the BE */
export interface CallLogPagedResponse {
    items: CallLogDto[];
    total: number;
    page: number;
    pageSize: number;
}

/**
 * Timeline event DTO — BE hasn't defined a shared DTO for this,
 * but the endpoint exists. Using placeholder shape until confirmed.
 */
export interface CallTimelineEventDto {
    id: string;
    eventType: string;
    description: string;
    timestamp: string;
}
