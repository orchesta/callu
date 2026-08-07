/** Wraps the WebRTC provider lifecycle: picks the adapter from JoinResultDto, tracks state/participants/streams, tears down on unmount. */

import { useState, useRef, useCallback, useEffect } from 'react';
import type { JoinResultDto } from '../types/conference.types';
import {
    type IWebRTCProvider,
    type WebRTCParticipant,
    type WebRTCConnectionState,
    type DeviceList,
    extractCredentials,
    createWebRTCProvider,
} from '../lib/webrtc-provider';

interface UseWebRTCReturn {
    /** Current connection state */
    connectionState: WebRTCConnectionState;
    /** List of remote participants */
    participants: WebRTCParticipant[];
    /** Local media stream (camera + mic) */
    localStream: MediaStream | null;
    /** Remote media streams keyed by participant ID */
    remoteStreams: Record<string, MediaStream>;
    /** The participant currently speaking (native VAD), or null. */
    activeSpeakerId: string | null;
    /** Join a conference using credentials from JoinResultDto */
    join: (joinResult: JoinResultDto, conferenceId: string, contextData?: Record<string, string>) => Promise<void>;
    /** Leave the current conference */
    leave: () => Promise<void>;
    /** Toggle microphone */
    toggleMic: (enabled: boolean) => void;
    /** Toggle camera */
    toggleCamera: (enabled: boolean) => void;
    /** Available input devices (mics + cameras). */
    devices: DeviceList;
    /** Switch the active microphone / camera. */
    setAudioInput: (deviceId: string) => void;
    setVideoInput: (deviceId: string) => void;
    /** Error message if connection failed */
    error: string | null;
}

export function useWebRTC(): UseWebRTCReturn {
    const [connectionState, setConnectionState] = useState<WebRTCConnectionState>('disconnected');
    const [participants, setParticipants] = useState<WebRTCParticipant[]>([]);
    const [localStream, setLocalStream] = useState<MediaStream | null>(null);
    const [remoteStreams, setRemoteStreams] = useState<Record<string, MediaStream>>({});
    const [activeSpeakerId, setActiveSpeakerId] = useState<string | null>(null);
    const [devices, setDevices] = useState<DeviceList>({ microphones: [], cameras: [] });
    const [error, setError] = useState<string | null>(null);

    const providerRef = useRef<IWebRTCProvider | null>(null);
    const joiningRef = useRef(false);

    const join = useCallback(async (joinResult: JoinResultDto, conferenceId: string, contextData?: Record<string, string>) => {
        const credentials = extractCredentials(joinResult);
        if (!credentials) {
            const msg = 'No WebRTC provider credentials returned from server';
            setError(msg);
            setConnectionState('error');
            throw new Error(msg);
        }

        if (joiningRef.current) return;
        joiningRef.current = true;

        try {
            setError(null);
            setConnectionState('connecting');

            if (providerRef.current) {
                // Rejoin path (e.g. after a dropped session): the old provider's endpoints are
                // dead and its listeners are gone, so clear the derived state — stale tiles
                // would otherwise linger until page unload.
                providerRef.current.dispose();
                providerRef.current = null;
                setParticipants([]);
                setRemoteStreams({});
                setLocalStream(null);
                setActiveSpeakerId(null);
            }

            const provider = await createWebRTCProvider(credentials);
            providerRef.current = provider;

            provider.onConnectionStateChange((state) => setConnectionState(state));
            provider.onParticipantJoined((p) =>
                setParticipants((prev) => (prev.some((x) => x.id === p.id) ? prev : [...prev, p])),
            );
            provider.onParticipantLeft((p) => {
                setParticipants((prev) => prev.filter((x) => x.id !== p.id));
                setRemoteStreams((prev) => {
                    const next = { ...prev };
                    delete next[p.id];
                    return next;
                });
            });
            provider.onLocalStream((stream) => setLocalStream(stream));
            provider.onRemoteStream((participantId, stream) => {
                setRemoteStreams((prev) => ({ ...prev, [participantId]: stream }));
            });
            provider.onParticipantUpdated((p) =>
                setParticipants((prev) => prev.map((x) => (x.id === p.id ? p : x))),
            );
            provider.onActiveSpeaker((id) => setActiveSpeakerId(id));
            provider.onError((e) => setError(e.message));

            await provider.connect(credentials);

            await provider.joinConference(conferenceId, contextData);

            try {
                setDevices(await provider.getDevices());
            } catch { /* device list is best-effort */ }
        } catch (err) {
            const message = err instanceof Error ? err.message : 'Failed to connect to conference';
            setError(message);
            setConnectionState('error');
            throw err instanceof Error ? err : new Error(message);
        } finally {
            joiningRef.current = false;
        }
    }, []);

    const leave = useCallback(async () => {
        if (providerRef.current) {
            await providerRef.current.leaveConference();
            providerRef.current.dispose();
            providerRef.current = null;
        }

        setConnectionState('disconnected');
        setParticipants([]);
        setLocalStream(null);
        setRemoteStreams({});
        setActiveSpeakerId(null);
    }, []);

    const toggleMic = useCallback((enabled: boolean) => {
        providerRef.current?.toggleMic(enabled);
    }, []);

    const toggleCamera = useCallback((enabled: boolean) => {
        providerRef.current?.toggleCamera(enabled);
    }, []);

    const setAudioInput = useCallback((deviceId: string) => {
        void providerRef.current?.setAudioInput(deviceId);
    }, []);

    const setVideoInput = useCallback((deviceId: string) => {
        void providerRef.current?.setVideoInput(deviceId);
    }, []);

    useEffect(() => {
        return () => {
            if (providerRef.current) {
                providerRef.current.dispose();
                providerRef.current = null;
            }
        };
    }, []);

    return {
        connectionState,
        participants,
        localStream,
        remoteStreams,
        activeSpeakerId,
        devices,
        join,
        leave,
        toggleMic,
        toggleCamera,
        setAudioInput,
        setVideoInput,
        error,
    };
}
