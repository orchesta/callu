/** Mirrors the conference DTOs in Callu.Shared.Models.Conference. */

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
