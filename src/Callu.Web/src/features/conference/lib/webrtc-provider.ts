/**
 * WebRTC Provider Abstraction — Provider-agnostic interface for conference media.
 *
 * The backend decides which provider is active via JoinResultDto.
 * The frontend auto-selects the right adapter using createWebRTCProvider().
 */

import type { JoinResultDto } from '../types/conference.types';

/** Discriminated union — backend decides which provider via JoinResultDto fields */
export type WebRTCCredentials =
    | { provider: 'voximplant'; loginKey: string; appName: string; accountName: string; username: string }
    | { provider: 'twilio'; accessToken: string; roomName: string };

export interface WebRTCParticipant {
    id: string;
    displayName: string;
    stream?: MediaStream;
    isMuted: boolean;
    isCameraOff: boolean;
}

/** Callback signature for participant events — includes stream when available */
export type ParticipantCallback = (participant: WebRTCParticipant) => void;
/** Callback for stream events — maps stream to participant ID */
export type RemoteStreamCallback = (participantId: string, stream: MediaStream) => void;
export type StreamCallback = (stream: MediaStream) => void;

export type WebRTCConnectionState = 'disconnected' | 'connecting' | 'connected' | 'error';

export interface IWebRTCProvider {
    /** Connect to the provider cloud and authenticate */
    connect(credentials: WebRTCCredentials): Promise<void>;

    /** Join a conference room. contextData is an optional small map of IDs that the
     *  backend scenario correlates with a persisted room (e.g. incident_id). */
    joinConference(conferenceId: string, contextData?: Record<string, string>): Promise<void>;

    /** Leave and disconnect */
    leaveConference(): Promise<void>;

    /** Toggle microphone on/off */
    toggleMic(enabled: boolean): void;

    /** Toggle camera on/off */
    toggleCamera(enabled: boolean): void;

    onParticipantJoined(cb: ParticipantCallback): void;
    onParticipantLeft(cb: ParticipantCallback): void;
    onLocalStream(cb: StreamCallback): void;
    /** Remote stream with participant ID for correct mapping */
    onRemoteStream(cb: RemoteStreamCallback): void;
    onConnectionStateChange(cb: (state: WebRTCConnectionState) => void): void;

    /** Clean up resources */
    dispose(): void;
}

/** Extract credentials from JoinResultDto — auto-detect provider */
export function extractCredentials(joinResult: JoinResultDto): WebRTCCredentials | null {
    if (joinResult.voximplantLoginKey && joinResult.voximplantAppName && joinResult.voximplantAccountName && joinResult.voximplantUsername) {
        return {
            provider: 'voximplant',
            loginKey: joinResult.voximplantLoginKey,
            appName: joinResult.voximplantAppName,
            accountName: joinResult.voximplantAccountName,
            username: joinResult.voximplantUsername,
        };
    }

    if (joinResult.twilioAccessToken && joinResult.twilioRoomName) {
        return {
            provider: 'twilio',
            accessToken: joinResult.twilioAccessToken,
            roomName: joinResult.twilioRoomName,
        };
    }

    return null;
}

/** Create the appropriate WebRTC provider based on credentials */
export async function createWebRTCProvider(credentials: WebRTCCredentials): Promise<IWebRTCProvider> {
    switch (credentials.provider) {
        case 'voximplant': {
            const { VoximplantWebRTCProvider } = await import('./voximplant-provider');
            return new VoximplantWebRTCProvider();
        }
        case 'twilio':
            throw new Error('Twilio WebRTC provider not yet implemented');
        default:
            throw new Error(`Unknown WebRTC provider: ${(credentials as WebRTCCredentials).provider}`);
    }
}
