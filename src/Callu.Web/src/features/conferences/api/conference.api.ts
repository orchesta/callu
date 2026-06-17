import { apiClient } from '@/shared/api/client';
import type { PagedResult } from '@/shared/types/common.types';
import type { ConferenceRoomDto, ConferenceRoomFilter } from '../types/conference.types';

const BASE = '/api/v1/conferences';

export const conferenceApi = {
    /** Get paginated video conferences with optional filters */
    getAll: (filter?: ConferenceRoomFilter) =>
        apiClient.get<PagedResult<ConferenceRoomDto>>(BASE, {
            params: filter as Record<string, string | number | boolean | undefined>,
        }),
};
