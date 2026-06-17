/**
 * Video Conference React Query hooks.
 */

import { useQuery } from '@tanstack/react-query';
import { apiQueryOptions, useApiMutation } from '@/shared/api';
import { conferenceApi } from '../api/conference.api';

export const conferenceKeys = {
    all: ['conference'] as const,
    validate: (token: string) => [...conferenceKeys.all, 'validate', token] as const,
};

/** Create a conference room for a given incident */
export function useCreateConferenceRoom() {
    return useApiMutation(
        (incidentId: string) => conferenceApi.createRoom(incidentId),
    );
}

export const conferenceJoinQueries = {
    validateParticipant: (token: string) =>
        apiQueryOptions(conferenceKeys.validate(token), () => conferenceApi.validateParticipant(token), {
            enabled: !!token,
        }),
};

/** Validate a participant token (e.g. on the join page) */
export function useValidateParticipant(token: string) {
    return useQuery(conferenceJoinQueries.validateParticipant(token));
}

/** Join a conference with a participant token */
export function useJoinConference() {
    return useApiMutation(
        (participantToken: string) => conferenceApi.joinConference(participantToken),
    );
}

/** Leave a conference with a participant token */
export function useLeaveConference() {
    return useApiMutation(
        (participantToken: string) => conferenceApi.leaveConference(participantToken),
    );
}

/** End a conference room (moderator action) */
export function useEndConference() {
    return useApiMutation(
        (roomId: string) => conferenceApi.endConference(roomId),
    );
}
