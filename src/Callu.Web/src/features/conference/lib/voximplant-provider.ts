/**
 * Voximplant Web SDK adapter (v5, modular API).
 *
 * Flow: Core.init → registerModules(stream, conference) → client.connect({ node })
 *   → client.login(user, password) → conferenceManager.createConference → addStream → join.
 *
 * The server rotates a short-lived password per join (Voximplant has no server-side
 * one-time-key API — that flow is client-only), passed here as `loginKey`. The node
 * (NODE_1…NODE_12) is chosen in the provider form and travels in the join result.
 */

import { Core, ConnectionNode } from '@voximplant/websdk';
import {
    StreamLoader,
    streamToken,
    VideoQuality,
    type StreamManager,
    type LocalStream,
} from '@voximplant/websdk/modules/stream';
import {
    ConferenceLoader,
    conferenceToken,
    ConferenceEvent,
    EndpointEvent,
    type Conference,
    type ConferenceManager,
    type Endpoint,
} from '@voximplant/websdk/modules/conference-manager';
import type {
    IWebRTCProvider,
    WebRTCCredentials,
    ParticipantCallback,
    RemoteStreamCallback,
    StreamCallback,
    WebRTCConnectionState,
} from './webrtc-provider';

export class VoximplantWebRTCProvider implements IWebRTCProvider {
    private core: Core | null = null;
    private streamManager: StreamManager | null = null;
    private conferenceManager: ConferenceManager | null = null;
    private conference: Conference | null = null;

    private localAudioStream: LocalStream | null = null;
    private localVideoStream: LocalStream | null = null;

    /** endpointId → display name, so the leave callback can name a participant by id alone */
    private endpointNames = new Map<string, string>();
    /** endpointId → aggregate MediaStream collecting that endpoint's audio + video tracks */
    private remoteMedia = new Map<string, MediaStream>();
    /** Our own Voximplant user name (local part), used to skip our own endpoint */
    private localUserName = '';

    private participantJoinedCb: ParticipantCallback | null = null;
    private participantLeftCb: ParticipantCallback | null = null;
    private localStreamCb: StreamCallback | null = null;
    private remoteStreamCb: RemoteStreamCallback | null = null;
    private connectionStateCb: ((state: WebRTCConnectionState) => void) | null = null;

    async connect(credentials: WebRTCCredentials): Promise<void> {
        if (credentials.provider !== 'voximplant') {
            throw new Error('VoximplantWebRTCProvider only accepts voximplant credentials');
        }

        this.connectionStateCb?.('connecting');

        const fullUsername = credentials.username;
        this.localUserName = (credentials.username.split('@')[0] ?? '').toLowerCase();
        const node = ConnectionNode[credentials.node as keyof typeof ConnectionNode];
        if (!node) {
            this.connectionStateCb?.('error');
            throw new Error(`Unknown Voximplant node "${credentials.node}"`);
        }

        try {
            const core = Core.init({});
            core.registerModules([StreamLoader(), ConferenceLoader()]);
            this.core = core;

            const streamModule = await core.getModuleAsync(streamToken);
            this.streamManager = streamModule.streamManager;
            this.conferenceManager = await core.getModuleAsync(conferenceToken);

            await core.client.connect({ node });
            await core.client.login({ username: fullUsername, password: credentials.loginKey });

            this.connectionStateCb?.('connected');
        } catch (error: unknown) {
            const e = error as { code?: unknown; name?: unknown; message?: unknown };
            console.error('[VoximplantProvider] connect/login FAILED — code:', e?.code,
                'name:', e?.name, 'message:', e?.message, '| raw:', error);
            this.connectionStateCb?.('error');
            throw error;
        }
    }

    async joinConference(conferenceId: string, contextData?: Record<string, string>): Promise<void> {
        if (!this.conferenceManager || !this.streamManager) throw new Error('Not connected');

        const customData = contextData ? JSON.stringify(contextData) : undefined;

        const conference = this.conferenceManager.createConference({
            conferenceName: conferenceId,
            ...(customData ? { customData } : {}),
        });
        this.conference = conference;

        conference.addEventListener(ConferenceEvent.Connected, () => {
            this.connectionStateCb?.('connected');
        });

        conference.addEventListener(ConferenceEvent.Disconnected, () => {
            this.connectionStateCb?.('disconnected');
        });

        conference.addEventListener(ConferenceEvent.Failed, (event) => {
            console.error('[VoximplantProvider] Conference failed:', event.payload);
            this.connectionStateCb?.('error');
        });

        conference.addEventListener(ConferenceEvent.EndpointAdded, (event) => {
            const endpoint = conference.endpoints.value.get(event.payload.newEndpointId);
            if (endpoint) this.handleEndpoint(endpoint);
        });

        conference.addEventListener(ConferenceEvent.EndpointRemoved, (event) => {
            const id = event.payload.removedEndpointId;
            const displayName = this.endpointNames.get(id);
            this.remoteMedia.delete(id);
            // Only announce a leave for endpoints we actually surfaced as participants.
            if (displayName === undefined) return;
            this.endpointNames.delete(id);
            this.participantLeftCb?.({ id, displayName, isMuted: false, isCameraOff: false });
        });

        // Publish local audio + video before joining so the first frame is sent on connect.
        const audioStream = await this.streamManager.createAudioStream({ audioProcessing: true });
        this.localAudioStream = audioStream;
        await conference.addStream(audioStream);

        const videoStream = await this.streamManager.createVideoStream(VideoQuality.HD);
        this.localVideoStream = videoStream;
        await conference.addStream(videoStream);
        this.localStreamCb?.(videoStream.sourceStream);

        await conference.join();
    }

    private handleEndpoint(endpoint: Endpoint): void {
        // Skip our own endpoint (the conference reports it too) and any service
        // endpoint without a real user (e.g. a TTS announcement player) — only real
        // remote participants get a tile.
        const userName = (endpoint.userName || '').toLowerCase();
        const isSelf = endpoint.id === this.conference?.endpointId.value || userName === this.localUserName;
        if (isSelf || !userName) return;

        // Dedup: an endpoint may be reported more than once.
        if (this.endpointNames.has(endpoint.id)) return;

        const displayName = endpoint.displayName || endpoint.userName || 'Unknown';
        this.endpointNames.set(endpoint.id, displayName);

        this.participantJoinedCb?.({
            id: endpoint.id,
            displayName,
            isMuted: endpoint.isMicrophoneMuted.value,
            isCameraOff: false,
        });

        // Streams already attached when the endpoint appears.
        for (const stream of endpoint.streams.values()) {
            this.attachRemoteTrack(endpoint.id, stream.sourceStream);
        }

        endpoint.addEventListener(EndpointEvent.RemoteMediaAdded, (event) => {
            this.attachRemoteTrack(endpoint.id, event.payload.stream.sourceStream);
        });
    }

    /**
     * A Voximplant endpoint exposes audio and video as separate streams; collect their
     * tracks into one MediaStream per endpoint so the tile renders video and plays audio
     * together (emitting each stream separately would overwrite the previous one).
     */
    private attachRemoteTrack(endpointId: string, source: MediaStream): void {
        let aggregate = this.remoteMedia.get(endpointId);
        if (!aggregate) {
            aggregate = new MediaStream();
            this.remoteMedia.set(endpointId, aggregate);
        }
        for (const track of source.getTracks()) {
            if (!aggregate.getTracks().some((t) => t.id === track.id)) {
                aggregate.addTrack(track);
            }
        }
        this.remoteStreamCb?.(endpointId, aggregate);
    }

    async leaveConference(): Promise<void> {
        if (this.conference) {
            try { this.conference.hangup(); } catch { /* empty */ }
            this.conference = null;
        }
        this.closeLocalStreams();
        this.endpointNames.clear();
        this.remoteMedia.clear();
    }

    toggleMic(enabled: boolean): void {
        if (!this.conference) return;
        if (enabled) this.conference.unmuteMicrophone();
        else this.conference.muteMicrophone();
    }

    toggleCamera(enabled: boolean): void {
        void this.setCamera(enabled);
    }

    private async setCamera(enabled: boolean): Promise<void> {
        if (!this.conference || !this.streamManager) return;

        if (enabled) {
            if (this.localVideoStream) return;
            const videoStream = await this.streamManager.createVideoStream(VideoQuality.HD);
            this.localVideoStream = videoStream;
            await this.conference.addStream(videoStream);
            this.localStreamCb?.(videoStream.sourceStream);
        } else {
            const videoStream = this.localVideoStream;
            if (!videoStream) return;
            this.localVideoStream = null;
            try { await this.conference.removeStream(videoStream); } catch { /* empty */ }
            videoStream.close();
        }
    }

    private closeLocalStreams(): void {
        for (const stream of [this.localAudioStream, this.localVideoStream]) {
            if (stream) {
                try { stream.close(); } catch { /* empty */ }
            }
        }
        this.localAudioStream = null;
        this.localVideoStream = null;
    }

    onParticipantJoined(cb: ParticipantCallback): void {
        this.participantJoinedCb = cb;
    }

    onParticipantLeft(cb: ParticipantCallback): void {
        this.participantLeftCb = cb;
    }

    onLocalStream(cb: StreamCallback): void {
        this.localStreamCb = cb;
    }

    onRemoteStream(cb: RemoteStreamCallback): void {
        this.remoteStreamCb = cb;
    }

    onConnectionStateChange(cb: (state: WebRTCConnectionState) => void): void {
        this.connectionStateCb = cb;
    }

    dispose(): void {
        if (this.conference) {
            try { this.conference.hangup(); } catch { /* empty */ }
            this.conference = null;
        }

        this.closeLocalStreams();
        this.endpointNames.clear();
        this.remoteMedia.clear();

        if (this.core) {
            try { void this.core.client.disconnect(); } catch { /* empty */ }
            this.core = null;
        }
        this.streamManager = null;
        this.conferenceManager = null;

        this.participantJoinedCb = null;
        this.participantLeftCb = null;
        this.localStreamCb = null;
        this.remoteStreamCb = null;
        this.connectionStateCb = null;
    }
}
