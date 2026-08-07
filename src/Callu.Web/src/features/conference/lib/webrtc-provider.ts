/** Provider-agnostic conference media interface; the adapter is chosen from JoinResultDto by createWebRTCProvider(). */

import type { JoinResultDto } from '../types/conference.types';

/** Discriminated union — backend decides which provider via JoinResultDto fields */
export type WebRTCCredentials =
    | { provider: 'voximplant'; loginKey: string; appName: string; accountName: string; username: string; node: string }
    | { provider: 'twilio'; accessToken: string; roomName: string };

export interface WebRTCParticipant {
    id: string;
    displayName: string;
    stream?: MediaStream;
    isMuted: boolean;
    isCameraOff: boolean;
}

/** A selectable input device (microphone or camera). */
export interface DeviceOption {
    deviceId: string;
    label: string;
}

export interface DeviceList {
    microphones: DeviceOption[];
    cameras: DeviceOption[];
}

/** Callback signature for participant events — includes stream when available */
export type ParticipantCallback = (participant: WebRTCParticipant) => void;
/** Callback for stream events — maps stream to participant ID */
export type RemoteStreamCallback = (participantId: string, stream: MediaStream) => void;
/** Local preview stream, or null when the camera is off. */
export type StreamCallback = (stream: MediaStream | null) => void;
/** The participant currently speaking (native voice-activity detection), or null. */
export type ActiveSpeakerCallback = (participantId: string | null) => void;

/** A non-fatal media/connection problem the UI can surface without leaving the call. */
export interface WebRTCError {
    kind: 'permission-denied' | 'no-device' | 'device-busy' | 'media-error' | 'connection';
    device?: 'microphone' | 'camera';
    message: string;
}
export type ErrorCallback = (error: WebRTCError) => void;

/** 'disconnected' is a clean end; 'dropped' means the session object is dead and recovery
 * needs a full fresh join with new credentials. */
export type WebRTCConnectionState = 'disconnected' | 'connecting' | 'connected' | 'reconnecting' | 'dropped' | 'error';

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

    /** Enumerate available microphones and cameras (labels require a prior permission grant). */
    getDevices(): Promise<DeviceList>;
    /** Switch the active microphone; the choice is reused for subsequent (re)publishes. */
    setAudioInput(deviceId: string): Promise<void>;
    /** Switch the active camera; the choice is reused for subsequent (re)publishes. */
    setVideoInput(deviceId: string): Promise<void>;

    onParticipantJoined(cb: ParticipantCallback): void;
    onParticipantLeft(cb: ParticipantCallback): void;
    /** A participant's mute/camera state changed. */
    onParticipantUpdated(cb: ParticipantCallback): void;
    onLocalStream(cb: StreamCallback): void;
    /** Remote stream with participant ID for correct mapping */
    onRemoteStream(cb: RemoteStreamCallback): void;
    /** The active speaker changed (or null when nobody is talking). */
    onActiveSpeaker(cb: ActiveSpeakerCallback): void;
    onConnectionStateChange(cb: (state: WebRTCConnectionState) => void): void;
    /** Non-fatal media/permission errors (e.g. mic/camera denied) that the UI should surface. */
    onError(cb: ErrorCallback): void;

    /** Clean up resources */
    dispose(): void;
}

/** Extract credentials from JoinResultDto — auto-detect provider */
export function extractCredentials(joinResult: JoinResultDto): WebRTCCredentials | null {
    if (joinResult.voximplantLoginKey && joinResult.voximplantAppName && joinResult.voximplantAccountName && joinResult.voximplantUsername && joinResult.voximplantNode) {
        return {
            provider: 'voximplant',
            loginKey: joinResult.voximplantLoginKey,
            appName: joinResult.voximplantAppName,
            accountName: joinResult.voximplantAccountName,
            username: joinResult.voximplantUsername,
            node: joinResult.voximplantNode,
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
