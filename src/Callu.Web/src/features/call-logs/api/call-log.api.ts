/**
 * Call Log API module — connects to CallLogsController (3 endpoints).
 */

import { apiClient } from '@/shared/api';
import type { CallLogDto, CallLogPagedResponse, CallTimelineEventDto } from '../types/call-log.types';

const BASE = '/api/v1/call-logs';

export const callLogApi = {
    /** GET /call-logs?page=&pageSize= — paginated list */
    getAll: (page = 1, pageSize = 25) =>
        apiClient.get<CallLogPagedResponse>(BASE, {
            params: { page, pageSize },
        }),

    /** GET /call-logs/incident/{incidentId} — logs for a specific incident */
    getByIncident: (incidentId: string) =>
        apiClient.get<CallLogDto[]>(`${BASE}/incident/${incidentId}`),

    /** GET /call-logs/incident/{incidentId}/timeline — timeline events */
    getTimeline: (incidentId: string) =>
        apiClient.get<CallTimelineEventDto[]>(`${BASE}/incident/${incidentId}/timeline`),
};
