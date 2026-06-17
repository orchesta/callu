import { useQuery } from '@tanstack/react-query';
import { apiQueryOptions } from '@/shared/api';
import { conferenceApi } from '../api/conference.api';
import type { ConferenceRoomFilter } from '../types/conference.types';

export const conferenceKeys = {
  all: ['conferences'] as const,
  lists: () => [...conferenceKeys.all, 'list'] as const,
  list: (filters?: ConferenceRoomFilter) => [...conferenceKeys.lists(), filters] as const,
};

export const conferenceRoomQueries = {
  list: (filters?: ConferenceRoomFilter) =>
    apiQueryOptions(conferenceKeys.list(filters), () => conferenceApi.getAll(filters), { staleTime: 30_000 }),
};

export function useConferences(filters?: ConferenceRoomFilter) {
  return useQuery(conferenceRoomQueries.list(filters));
}
