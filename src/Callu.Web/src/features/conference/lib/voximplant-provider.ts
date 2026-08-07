/** Voximplant Web SDK adapter. The connection node and the short-lived per-join login password
 * both arrive in the join result; the SDK has no server-side one-time-key flow. */

import { Core, ConnectionNode, ConnectionTimeoutError, ConnectionNetworkError, ClientState } from '@voximplant/websdk';
import {
    StreamLoader,
    streamToken,
    VideoQuality,
    StreamEvent,
    type StreamManager,
    type LocalStream,
} from '@voximplant/websdk/modules/stream';
import {
    ConferenceLoader,
    conferenceToken,
    ConferenceEvent,
    ConferenceState,
    ConferenceDisconnectReason,
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
    ErrorCallback,
    ActiveSpeakerCallback,
    DeviceList,
    DeviceOption,
} from './webrtc-provider';

export class VoximplantWebRTCProvider implements IWebRTCProvider {
    private core: Core | null = null;
    private streamManager: StreamManager | null = null;
    private conferenceManager: ConferenceManager | null = null;
    private conference: Conference | null = null;

    private localAudioStream: LocalStream | null = null;
    private localVideoStream: LocalStream | null = null;

    /** Operator-selected input devices; reused for every (re)publish. */
    private selectedMicId: string | undefined;
    private selectedCamId: string | undefined;

    /** endpointId → display name, so the leave callback can name a participant by id alone */
    private endpointNames = new Map<string, string>();
    /** endpointId → aggregate MediaStream collecting that endpoint's audio + video tracks */
    private remoteMedia = new Map<string, MediaStream>();
    /** Our own Voximplant user name (local part), used to skip our own endpoint */
    private localUserName = '';

    private participantJoinedCb: ParticipantCallback | null = null;
    private participantLeftCb: ParticipantCallback | null = null;
    private participantUpdatedCb: ParticipantCallback | null = null;
    private localStreamCb: StreamCallback | null = null;
    private remoteStreamCb: RemoteStreamCallback | null = null;
    private activeSpeakerCb: ActiveSpeakerCallback | null = null;
    /** Endpoints currently emitting voice activity, newest last (tail = active speaker). */
    private speakingEndpoints: string[] = [];
    private connectionStateCb: ((state: WebRTCConnectionState) => void) | null = null;
    private errorCb: ErrorCallback | null = null;

    /** Watchable unsubscribe handles cleared on dispose. */
    private unwatchers: Array<() => void> = [];
    /** Set once dispose()/leave() runs so an in-flight connect retry loop bails out. */
    private disposed = false;
    /** Set before a deliberate hangup so the resulting Disconnected event is not reported as the server ending the room. */
    private leaving = false;

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

        const core = Core.init({});
        core.registerModules([StreamLoader(), ConferenceLoader()]);
        this.core = core;

        const streamModule = await core.getModuleAsync(streamToken);
        this.streamManager = streamModule.streamManager;
        this.conferenceManager = await core.getModuleAsync(conferenceToken);

        const maxAttempts = 3;
        for (let attempt = 1; attempt <= maxAttempts; attempt++) {
            if (this.disposed) return;
            try {
                await core.client.connect({ node });
                await core.client.login({ username: fullUsername, password: credentials.loginKey });
                if (this.disposed) return;
                // Do NOT emit 'connected' here — gateway login is not the conference session.
                // Gateway-level reconnection surfaces only through the client state watchable.
                this.unwatchers.push(core.client.state.watch((s) => {
                    if (s === ClientState.Reconnecting) this.connectionStateCb?.('reconnecting');
                }));
                return;
            } catch (error: unknown) {
                if (this.disposed) return;
                // Retry only genuinely transient failures. ConnectionInterruptedError means
                // Client.disconnect() was called (deliberate teardown) — never retry it.
                const transient = error instanceof ConnectionTimeoutError
                    || error instanceof ConnectionNetworkError;
                const e = error as { name?: unknown; message?: unknown };
                console.warn(`[VoximplantProvider] connect/login attempt ${attempt}/${maxAttempts} failed:`,
                    e?.name, e?.message);
                if (attempt < maxAttempts && transient) {
                    await new Promise((resolve) => setTimeout(resolve, 500 * attempt));
                    continue;
                }
                this.connectionStateCb?.('error');
                throw error;
            }
        }
    }

    async joinConference(conferenceId: string, contextData?: Record<string, string>): Promise<void> {
        if (!this.conferenceManager || !this.streamManager) throw new Error('Not connected');

        const customData = contextData ? JSON.stringify(contextData) : undefined;

        // Firefox's RID-based simulcast does not interop reliably with the conference bridge —
        // the SDK's own stats API documents missing rid support on Firefox, and joins from
        // Firefox produced one-way media (others never received the Firefox user's stream)
        // while Chromium browsers worked. Send a single encoding from Firefox instead.
        const isFirefox = typeof navigator !== 'undefined' && /firefox/i.test(navigator.userAgent);

        const conference = this.conferenceManager.createConference({
            conferenceName: conferenceId,
            ...(isFirefox ? { simulcast: false } : {}),
            ...(customData ? { customData } : {}),
        });
        this.conference = conference;

        this.unwatchers.push(conference.state.watch((s) => {
            if (s === ConferenceState.Reconnecting) this.connectionStateCb?.('reconnecting');
            else if (s === ConferenceState.Connected) this.connectionStateCb?.('connected');
        }));

        conference.addEventListener(ConferenceEvent.Connected, () => {
            this.connectionStateCb?.('connected');
        });

        conference.addEventListener(ConferenceEvent.Disconnected, (event) => {
            // A deliberate leave/dispose reports its own state — nothing to surface.
            if (this.leaving) return;
            // CONNECTION_LOST: the transport died and the SDK's internal recovery re-creates a
            // NEW Conference object — THIS object (with all our listeners and local streams) is
            // dead for good. The app must do a full fresh join; surface it as 'dropped'.
            // Anything else (REMOTE_ENDED: the scenario/server closed the room) is a clean end.
            const dropped = event.payload.reason === ConferenceDisconnectReason.ConnectionLost;
            console.warn('[VoximplantProvider] Conference disconnected:', event.payload.reason);
            this.connectionStateCb?.(dropped ? 'dropped' : 'disconnected');
        });

        conference.addEventListener(ConferenceEvent.Failed, (event) => {
            console.error('[VoximplantProvider] Conference failed:', event.payload);
            this.connectionStateCb?.('error');
        });

        conference.addEventListener(ConferenceEvent.EndpointAdded, (event) => {
            const endpoint = conference.endpoints.value.get(event.payload.newEndpointId);
            // May be undefined when the event fires before the endpoints map is populated
            // (seen for participants already in the room when we join) — the endpoints
            // watch below reconciles those, so a miss here is not fatal.
            if (endpoint) this.handleEndpoint(endpoint);
        });

        // Reconcile from the endpoints map, because EndpointAdded/Removed alone is lossy for
        // participants already in the room. handleEndpoint dedupes, so a second pass is harmless.
        this.unwatchers.push(conference.endpoints.watch((endpoints) => {
            for (const endpoint of endpoints.values()) this.handleEndpoint(endpoint);
            const gone = [...this.endpointNames].filter(([id]) => !endpoints.has(id));
            for (const [id, displayName] of gone) this.removeEndpoint(id, displayName);
        }));
        conference.addEventListener(ConferenceEvent.Connected, () => {
            for (const endpoint of conference.endpoints.value.values()) this.handleEndpoint(endpoint);
        });

        conference.addEventListener(ConferenceEvent.EndpointRemoved, (event) => {
            const id = event.payload.removedEndpointId;
            const displayName = this.endpointNames.get(id);
            // Only announce a leave for endpoints we actually surfaced as participants.
            if (displayName === undefined) {
                this.remoteMedia.delete(id);
                this.updateSpeaking(id, false);
                return;
            }
            this.removeEndpoint(id, displayName);
        });

        // Audio and video must both be in the initial SDP offer: the conference scenario forwards
        // exactly the tracks it saw at alerting, and a track added after join is never forwarded.
        await this.publishAudio();
        await this.publishVideo();

        await conference.join();
    }

    /** Reports a local stream dying outside our control; the identity check filters out our own
     * closes, since every teardown path clears the reference before calling close(). */
    private watchLocalStreamEnd(device: 'microphone' | 'camera', stream: LocalStream): void {
        stream.addEventListener(StreamEvent.Ended, () => {
            if (device === 'camera' && this.localVideoStream === stream) {
                this.localVideoStream = null;
                this.localStreamCb?.(null);
                this.emitMediaError('camera', { name: 'NotFoundError', message: 'Camera stream ended' });
            } else if (device === 'microphone' && this.localAudioStream === stream) {
                this.localAudioStream = null;
                this.emitMediaError('microphone', { name: 'NotFoundError', message: 'Microphone stream ended' });
            }
        });
    }

    /** Audio must be added before join() (SDK requirement); independent of the camera. */
    private async publishAudio(): Promise<void> {
        if (!this.conference || !this.streamManager || this.localAudioStream) return;
        try {
            const audioStream = await this.streamManager.createAudioStream({ audioProcessing: true }, this.selectedMicId);
            this.localAudioStream = audioStream;
            this.watchLocalStreamEnd('microphone', audioStream);
            await this.conference.addStream(audioStream);
        } catch (error) {
            this.emitMediaError('microphone', error);
        }
    }

    /** Video is added before join() so it rides the initial offer and gets forwarded. */
    private async publishVideo(): Promise<void> {
        if (!this.conference || !this.streamManager || this.localVideoStream) return;
        try {
            const videoStream = await this.streamManager.createVideoStream(VideoQuality.HD, this.selectedCamId);
            this.localVideoStream = videoStream;
            this.watchLocalStreamEnd('camera', videoStream);
            await this.conference.addStream(videoStream);
            this.localStreamCb?.(videoStream.sourceStream);
        } catch (error) {
            this.emitMediaError('camera', error);
        }
    }

    async getDevices(): Promise<DeviceList> {
        const devices = await navigator.mediaDevices.enumerateDevices();
        const pick = (kind: MediaDeviceKind): DeviceOption[] => devices
            .filter((d) => d.kind === kind && d.deviceId)
            .map((d) => ({ deviceId: d.deviceId, label: d.label || 'Unnamed device' }));
        return { microphones: pick('audioinput'), cameras: pick('videoinput') };
    }

    async setAudioInput(deviceId: string): Promise<void> {
        this.selectedMicId = deviceId;
        if (!this.conference || !this.streamManager) return;
        const old = this.localAudioStream;
        try {
            const audioStream = await this.streamManager.createAudioStream({ audioProcessing: true }, deviceId);
            // replaceStream keeps the existing sender; an add+remove pair would negotiate a new
            // media line that is not forwarded to legs already joined. Reference first, then close.
            this.localAudioStream = audioStream;
            this.watchLocalStreamEnd('microphone', audioStream);
            if (old) {
                await this.conference.replaceStream(audioStream, old);
                old.close();
            } else {
                await this.conference.addStream(audioStream);
            }
        } catch (error) {
            this.emitMediaError('microphone', error);
        }
    }

    async setVideoInput(deviceId: string): Promise<void> {
        this.selectedCamId = deviceId;
        if (!this.conference || !this.streamManager || !this.localVideoStream) return;
        const old = this.localVideoStream;
        try {
            const videoStream = await this.streamManager.createVideoStream(VideoQuality.HD, deviceId);
            // Same-sender swap — see setAudioInput. Reference updated before old.close().
            this.localVideoStream = videoStream;
            this.watchLocalStreamEnd('camera', videoStream);
            await this.conference.replaceStream(videoStream, old);
            old.close();
            this.localStreamCb?.(videoStream.sourceStream);
        } catch (error) {
            this.emitMediaError('camera', error);
        }
    }

    private emitMediaError(device: 'microphone' | 'camera', error: unknown): void {
        const name = (error as { name?: string })?.name ?? '';
        const kind = name === 'NotAllowedError' ? 'permission-denied'
            : name === 'NotFoundError' ? 'no-device'
            : name === 'NotReadableError' ? 'device-busy'
            : 'media-error';
        console.error(`[VoximplantProvider] ${device} ${kind}:`, error);
        this.errorCb?.({ kind, device, message: `${device}: ${kind}` });
    }

    private handleEndpoint(endpoint: Endpoint): void {
        // Skip our own endpoint, which the conference reports too. The userName fallback covers
        // the reconcile path, where an endpoint can be seen before endpointId is populated.
        const userName = (endpoint.userName || '').toLowerCase();
        if (endpoint.id === this.conference?.endpointId.value) return;
        if (this.localUserName && userName === this.localUserName) return;

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

        endpoint.addEventListener(EndpointEvent.RemoteMediaRemoved, (event) => {
            this.detachRemoteTrack(endpoint.id, event.payload.stream.sourceStream);
        });

        // The Voximplant cloud pauses/resumes individual remote video streams on poor network
        // (docs: "automatic video stream disabling"). Detach on stop so the tile falls back to
        // the avatar instead of a frozen frame; re-attach when the cloud resumes it.
        endpoint.addEventListener(EndpointEvent.StopReceivingVideoStream, (event) => {
            const stream = [...endpoint.streams.values()].find((s) => s.id === event.payload.streamId);
            if (stream) this.detachRemoteTrack(endpoint.id, stream.sourceStream);
        });
        endpoint.addEventListener(EndpointEvent.StartReceivingVideoStream, (event) => {
            const stream = [...endpoint.streams.values()].find((s) => s.id === event.payload.streamId);
            if (stream) this.attachRemoteTrack(endpoint.id, stream.sourceStream);
        });

        this.unwatchers.push(endpoint.isMicrophoneMuted.watch((muted) => {
            const name = this.endpointNames.get(endpoint.id);
            if (name !== undefined) {
                this.participantUpdatedCb?.({ id: endpoint.id, displayName: name, isMuted: muted, isCameraOff: false });
            }
        }));

        this.unwatchers.push(endpoint.voiceActivityDetected.watch((speaking) => {
            this.updateSpeaking(endpoint.id, speaking);
        }));
    }

    /** Drop a surfaced participant: clear its media/VAD bookkeeping and announce the leave. */
    private removeEndpoint(id: string, displayName: string): void {
        this.endpointNames.delete(id);
        this.remoteMedia.delete(id);
        this.updateSpeaking(id, false);
        this.participantLeftCb?.({ id, displayName, isMuted: false, isCameraOff: false });
    }

    /** Track who is speaking (native VAD); the most-recent speaker is the active one. */
    private updateSpeaking(endpointId: string, speaking: boolean): void {
        const wasActive = this.speakingEndpoints[this.speakingEndpoints.length - 1] ?? null;
        this.speakingEndpoints = this.speakingEndpoints.filter((id) => id !== endpointId);
        if (speaking) this.speakingEndpoints.push(endpointId);
        const nowActive = this.speakingEndpoints[this.speakingEndpoints.length - 1] ?? null;
        if (nowActive !== wasActive) this.activeSpeakerCb?.(nowActive);
    }

    /** Merges an endpoint's separate audio and video streams into one MediaStream per tile. */
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

    private detachRemoteTrack(endpointId: string, source: MediaStream): void {
        const aggregate = this.remoteMedia.get(endpointId);
        if (!aggregate) return;
        for (const track of source.getTracks()) {
            const existing = aggregate.getTracks().find((t) => t.id === track.id);
            if (existing) aggregate.removeTrack(existing);
        }
        this.remoteStreamCb?.(endpointId, aggregate);
    }

    async leaveConference(): Promise<void> {
        this.leaving = true;
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
            const videoStream = await this.streamManager.createVideoStream(VideoQuality.HD, this.selectedCamId);
            this.localVideoStream = videoStream;
            this.watchLocalStreamEnd('camera', videoStream);
            await this.conference.addStream(videoStream);
            this.localStreamCb?.(videoStream.sourceStream);
        } else {
            const videoStream = this.localVideoStream;
            if (!videoStream) return;
            this.localVideoStream = null;
            try { await this.conference.removeStream(videoStream); } catch { /* empty */ }
            videoStream.close();
            this.localStreamCb?.(null);
        }
    }

    private closeLocalStreams(): void {
        // Clear the references BEFORE closing so the StreamEvent.Ended watcher treats these
        // closes as deliberate (it only reacts when the ref still points at the stream).
        const streams = [this.localAudioStream, this.localVideoStream];
        this.localAudioStream = null;
        this.localVideoStream = null;
        for (const stream of streams) {
            if (stream) {
                try { stream.close(); } catch { /* empty */ }
            }
        }
    }

    onParticipantJoined(cb: ParticipantCallback): void {
        this.participantJoinedCb = cb;
    }

    onParticipantLeft(cb: ParticipantCallback): void {
        this.participantLeftCb = cb;
    }

    onParticipantUpdated(cb: ParticipantCallback): void {
        this.participantUpdatedCb = cb;
    }

    onLocalStream(cb: StreamCallback): void {
        this.localStreamCb = cb;
    }

    onRemoteStream(cb: RemoteStreamCallback): void {
        this.remoteStreamCb = cb;
    }

    onError(cb: ErrorCallback): void {
        this.errorCb = cb;
    }

    onActiveSpeaker(cb: ActiveSpeakerCallback): void {
        this.activeSpeakerCb = cb;
    }

    onConnectionStateChange(cb: (state: WebRTCConnectionState) => void): void {
        this.connectionStateCb = cb;
    }

    dispose(): void {
        this.disposed = true;
        this.leaving = true;
        for (const unwatch of this.unwatchers) {
            try { unwatch(); } catch { /* empty */ }
        }
        this.unwatchers = [];

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
        this.errorCb = null;
        this.participantUpdatedCb = null;
        this.activeSpeakerCb = null;
        this.speakingEndpoints = [];
    }
}
