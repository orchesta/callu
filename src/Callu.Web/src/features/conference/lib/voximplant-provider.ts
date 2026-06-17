/* eslint-disable @typescript-eslint/no-explicit-any -- Voximplant SDK event payloads lack precise typings */
/**
 * Voximplant Web SDK adapter. init → connect → loginWithOneTimeKey(user, hash)
 * → callConference. The login hash is computed server-side; the client never sees
 * the user password. SDK: voximplant-websdk@4.x (classic SDK).
 */

import * as VoxImplant from 'voximplant-websdk';
import type {
    IWebRTCProvider,
    WebRTCCredentials,
    ParticipantCallback,
    RemoteStreamCallback,
    StreamCallback,
    WebRTCConnectionState,
} from './webrtc-provider';

type VoxClient = ReturnType<typeof VoxImplant.getInstance>;

export class VoximplantWebRTCProvider implements IWebRTCProvider {
    private sdk: VoxClient | null = null;
    private currentCall: any = null;
    private credentials: Extract<WebRTCCredentials, { provider: 'voximplant' }> | null = null;

    private participantJoinedCb: ParticipantCallback | null = null;
    private participantLeftCb: ParticipantCallback | null = null;
    private localStreamCb: StreamCallback | null = null;
    private remoteStreamCb: RemoteStreamCallback | null = null;
    private connectionStateCb: ((state: WebRTCConnectionState) => void) | null = null;

    async connect(credentials: WebRTCCredentials): Promise<void> {
        if (credentials.provider !== 'voximplant') {
            throw new Error('VoximplantWebRTCProvider only accepts voximplant credentials');
        }

        this.credentials = credentials;
        this.connectionStateCb?.('connecting');

        try {
            this.sdk = VoxImplant.getInstance();

            if (!this.sdk.alreadyInitialized) {
                await this.sdk.init({
                    micRequired: true,
                    videoConstraints: true,
                });
            }

            await this.sdk.connect();

            const fullUsername = `${credentials.username}@${credentials.appName}.${credentials.accountName}.voximplant.com`;
            await this.sdk.loginWithOneTimeKey(fullUsername, credentials.loginKey);

            this.connectionStateCb?.('connected');
        } catch (error) {
            this.connectionStateCb?.('error');
            throw error;
        }
    }

    async joinConference(conferenceId: string, contextData?: Record<string, string>): Promise<void> {
        if (!this.sdk) throw new Error('Not connected');

        const customData = contextData ? JSON.stringify(contextData) : undefined;

        this.currentCall = this.sdk.callConference({
            number: conferenceId,
            video: { sendVideo: true, receiveVideo: true },
            simulcast: true,
            ...(customData ? { customData } : {}),
        });

        this.currentCall.on(VoxImplant.CallEvents.Connected, () => {
            this.connectionStateCb?.('connected');
            void this.currentCall?.sendVideo?.(true)?.catch((err: unknown) => {
                console.error('[VoximplantProvider] sendVideo after connect failed:', err);
            });
        });

        const extractStream = (ev: any): MediaStream | undefined =>
            ev.mediaRenderer?.stream || ev.videoStream?.stream || ev.stream;

        this.currentCall.on(VoxImplant.CallEvents.LocalVideoStreamAdded, (ev: any) => {
            const stream = extractStream(ev);
            if (stream) this.localStreamCb?.(stream);
        });

        this.currentCall.on('LocalVideoAdded', (ev: any) => {
            const stream = extractStream(ev);
            if (stream) this.localStreamCb?.(stream);
        });

        this.currentCall.on(VoxImplant.CallEvents.Disconnected, () => {
            this.connectionStateCb?.('disconnected');
        });

        this.currentCall.on(VoxImplant.CallEvents.Failed, (e: any) => {
            console.error('[VoximplantProvider] Conference call failed:', e?.code, e?.reason);
            this.connectionStateCb?.('error');
        });

        this.currentCall.on(VoxImplant.CallEvents.EndpointAdded, (e: any) => {
            const endpoint = e.endpoint;
            const endpointUserPrefix =
                typeof endpoint.username === 'string' && endpoint.username.includes('@')
                    ? endpoint.username.split('@', 1)[0]
                    : endpoint.username;
            const isLocalEndpoint =
                endpoint.isDefault === true ||
                (this.credentials && endpointUserPrefix === this.credentials.username);

            if (!isLocalEndpoint) {
                this.participantJoinedCb?.({
                    id: endpoint.id,
                    displayName: endpoint.displayName || 'Unknown',
                    isMuted: false,
                    isCameraOff: false,
                });
            }

            endpoint.on(VoxImplant.EndpointEvents.RemoteMediaAdded, (ev: any) => {
                const stream = ev.mediaRenderer?.stream as MediaStream | undefined;
                if (!stream) return;
                if (isLocalEndpoint) {
                    this.localStreamCb?.(stream);
                } else {
                    this.remoteStreamCb?.(endpoint.id, stream);
                }
            });

            endpoint.on(VoxImplant.EndpointEvents.Removed, () => {
                if (isLocalEndpoint) return;
                this.participantLeftCb?.({
                    id: endpoint.id,
                    displayName: endpoint.displayName || 'Unknown',
                    isMuted: false,
                    isCameraOff: false,
                });
            });
        });
    }

    async leaveConference(): Promise<void> {
        if (this.currentCall) {
            this.currentCall.hangup();
            this.currentCall = null;
        }
    }

    toggleMic(enabled: boolean): void {
        this.currentCall?.sendAudio?.(enabled);
    }

    toggleCamera(enabled: boolean): void {
        this.currentCall?.sendVideo?.(enabled);
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
        if (this.currentCall) {
            try { this.currentCall.hangup(); } catch { /* empty */ }
            this.currentCall = null;
        }

        if (this.sdk) {
            try { this.sdk.disconnect(); } catch { /* empty */ }
            this.sdk = null;
        }

        this.participantJoinedCb = null;
        this.participantLeftCb = null;
        this.localStreamCb = null;
        this.remoteStreamCb = null;
        this.connectionStateCb = null;
    }
}
