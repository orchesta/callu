export interface ConferenceRoomDto {
    id: string;
    incidentId: string;
    incidentTitle: string;
    roomToken: string;
    status: string;
    participantCount: number;
    createdAt: string;
    expiresAt: string;
    endedAt?: string;
    recordingEnabled: boolean;
    recordingUrl?: string;
    voximplantConferenceId?: string;
}

export interface ConferenceRoomFilter {
    page?: number;
    pageSize?: number;
    status?: string;
    incidentId?: string;
    hasRecording?: boolean;
}

/** Current user's join info for an incident's active conference room. */
export interface ActiveConferenceInfo {
    roomId: string;
    roomToken: string;
    status: string;
    participantCount: number;
    userParticipantToken: string;
    expiresAt: string;
}
