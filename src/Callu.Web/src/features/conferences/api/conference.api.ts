import { apiClient } from '@/shared/api/client';
import type { PagedResult } from '@/shared/types/common.types';
import type { ActiveConferenceInfo, ConferenceRoomDto, ConferenceRoomFilter } from '../types/conference.types';

const BASE = '/api/v1/conferences';

export const conferenceApi = {
    /** Get paginated video conferences with optional filters */
    getAll: (filter?: ConferenceRoomFilter) =>
        apiClient.get<PagedResult<ConferenceRoomDto>>(BASE, {
            params: filter as Record<string, string | number | boolean | undefined>,
        }),

    /** Resolves — creating if needed — the caller's participant token for an incident's active
     * conference room, or null when the server reports there is no active room. */
    getJoinInfo: (incidentId: string) =>
        apiClient.get<ActiveConferenceInfo | null>(`/api/v1/incidents/${incidentId}/conference`),
};
