/**
 * useWebRTC — React hook wrapping the WebRTC provider lifecycle.
 *
 * Auto-selects provider from JoinResultDto, manages connection state,
 * participants, and media streams. Cleans up on unmount.
 *
 * Remote streams are stored in a Record keyed by participant ID
 * for correct stream-to-participant mapping.
 */

import { useState, useRef, useCallback, useEffect } from 'react';
import type { JoinResultDto } from '../types/conference.types';
import {
    type IWebRTCProvider,
    type WebRTCParticipant,
    type WebRTCConnectionState,
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
    /** Join a conference using credentials from JoinResultDto */
    join: (joinResult: JoinResultDto, conferenceId: string, contextData?: Record<string, string>) => Promise<void>;
    /** Leave the current conference */
    leave: () => Promise<void>;
    /** Toggle microphone */
    toggleMic: (enabled: boolean) => void;
    /** Toggle camera */
    toggleCamera: (enabled: boolean) => void;
    /** Error message if connection failed */
    error: string | null;
}

export function useWebRTC(): UseWebRTCReturn {
    const [connectionState, setConnectionState] = useState<WebRTCConnectionState>('disconnected');
    const [participants, setParticipants] = useState<WebRTCParticipant[]>([]);
    const [localStream, setLocalStream] = useState<MediaStream | null>(null);
    const [remoteStreams, setRemoteStreams] = useState<Record<string, MediaStream>>({});
    const [error, setError] = useState<string | null>(null);

    const providerRef = useRef<IWebRTCProvider | null>(null);

    const join = useCallback(async (joinResult: JoinResultDto, conferenceId: string, contextData?: Record<string, string>) => {
        const credentials = extractCredentials(joinResult);
        if (!credentials) {
            const msg = 'No WebRTC provider credentials returned from server';
            setError(msg);
            setConnectionState('error');
            throw new Error(msg);
        }

        try {
            setError(null);
            setConnectionState('connecting');

            const provider = await createWebRTCProvider(credentials);
            providerRef.current = provider;

            provider.onConnectionStateChange((state) => setConnectionState(state));
            provider.onParticipantJoined((p) => setParticipants((prev) => [...prev, p]));
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

            await provider.connect(credentials);

            await provider.joinConference(conferenceId, contextData);
        } catch (err) {
            const message = err instanceof Error ? err.message : 'Failed to connect to conference';
            setError(message);
            setConnectionState('error');
            throw err instanceof Error ? err : new Error(message);
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
    }, []);

    const toggleMic = useCallback((enabled: boolean) => {
        providerRef.current?.toggleMic(enabled);
    }, []);

    const toggleCamera = useCallback((enabled: boolean) => {
        providerRef.current?.toggleCamera(enabled);
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
        join,
        leave,
        toggleMic,
        toggleCamera,
        error,
    };
}
