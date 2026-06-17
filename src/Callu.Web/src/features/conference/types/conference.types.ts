/**
 * Video Conference Types — mirrors BE DTOs from Callu.Shared.Models.Conference
 *
 * Controller: VideoConferenceController at /api/v1/conferences
 * Endpoints are token-based: create room by incidentId, validate/join/leave by participantToken.
 */

/** Mirrors BE ConferenceRoomResult */
export interface ConferenceRoomResult {
    success: boolean;
    error?: string;
    roomId: string;
    roomToken: string;
    conferenceUrl: string;
    participantCount: number;
}

/** Mirrors BE ParticipantInfoDto */
export interface ParticipantInfoDto {
    participantToken: string;
    displayName: string;
    roomId: string;
    /** Voximplant dial string for callConference — must match cloud routing (e.g. callu-incident-… ) */
    voximplantConferenceId?: string;
    incidentId: string;
    incidentTitle: string;
    incidentSeverity: string;
    roomStatus: string;
    expiresAt: string;
    activeParticipants: number;
    isAlreadyActive: boolean;
}

/** Mirrors BE JoinResultDto */
export interface JoinResultDto {
    success: boolean;
    error?: string;

    voximplantLoginKey?: string;
    voximplantAppName?: string;
    voximplantAccountName?: string;
    voximplantUsername?: string;
    voximplantNode?: string;

    twilioAccessToken?: string;
    twilioRoomName?: string;

    displayName: string;
}
