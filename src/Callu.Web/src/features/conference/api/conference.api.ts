/**
 * Video Conference API — connects to VideoConferenceController (5 endpoints).
 *
 * Route: /api/v1/conferences
 * Endpoints use incidentId for room creation and participantToken for join/leave.
 */

import { apiClient } from '@/shared/api';
import type {
    ConferenceRoomResult,
    ParticipantInfoDto,
    JoinResultDto,
} from '../types/conference.types';

const BASE = '/api/v1/conferences';

export const conferenceApi = {
    /** POST /rooms/{incidentId} — Create conference room for incident */
    createRoom: (incidentId: string) =>
        apiClient.post<ConferenceRoomResult>(`${BASE}/rooms/${incidentId}`),

    /** GET /validate/{participantToken} — Validate participant (anonymous) */
    validateParticipant: (participantToken: string) =>
        apiClient.get<ParticipantInfoDto>(`${BASE}/validate/${participantToken}`),

    /** POST /join/{participantToken} — Join conference (anonymous) */
    joinConference: (participantToken: string) =>
        apiClient.post<JoinResultDto>(`${BASE}/join/${participantToken}`),

    /** POST /leave/{participantToken} — Leave conference (anonymous) */
    leaveConference: (participantToken: string) =>
        apiClient.post<void>(`${BASE}/leave/${participantToken}`),

    /** POST /rooms/{roomId}/end — End conference */
    endConference: (roomId: string) =>
        apiClient.post<void>(`${BASE}/rooms/${roomId}/end`),
};
