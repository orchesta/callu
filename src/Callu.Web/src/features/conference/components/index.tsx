import { useState, useEffect, useCallback, useRef } from "react";
import { t } from "@/shared/locales/i18n";
import { useParams, useNavigate } from "react-router";
import { Phone, Video, VideoOff, Mic, MicOff, Users, Clock, AlertCircle, CheckCircle, Loader } from "lucide-react";
import { API_URL } from "@/shared/config";
import { motion } from "motion/react";

/** Conference page reached at /conference/:token — validates the participant, joins, then hands media to useWebRTC. */

enum ConferenceState {
  Loading = "loading",
  Lobby = "lobby",
  InConference = "in_conference",
  Ended = "ended",
  Error = "error",
}

import { useValidateParticipant, useJoinConference, useLeaveConference } from "../hooks/use-conference";
import { useWebRTC } from "../hooks/use-webrtc";
import type { ParticipantInfoDto } from "../types/conference.types";
import type { WebRTCParticipant, DeviceList } from "../lib/webrtc-provider";

interface ConferenceData {
  token: string;
  incident: {
    id: string;
    title: string;
    severity: string;
  };
  timeRemaining: number;
  currentUser: {
    id: string;
    name: string;
    initials: string;
  };
}

/** Must match ConferenceRoom.VoximplantConferenceId / routing rule (callu-incident + incident GUID without dashes). */
function resolveVoximplantConferenceId(
  info: ParticipantInfoDto | null | undefined,
  _participantToken: string,
): string {
  const fromApi = info?.voximplantConferenceId?.trim();
  if (fromApi) return fromApi;
  const incidentN = info?.incidentId?.replace(/-/g, "");
  if (incidentN) return `callu-incident-${incidentN}`;
  throw new Error("Unable to resolve Voximplant conference id: missing incidentId and voximplantConferenceId");
}

/** Seconds until expiresAt, falling back to an hour when unparseable — a NaN countdown would tear the call down at once. */
function secondsUntil(expiresAt: string): number {
  const ms = new Date(expiresAt).getTime();
  return Number.isFinite(ms) ? Math.max(0, Math.floor((ms - Date.now()) / 1000)) : 3600;
}

export function ConferencePage() {
  const { token = "" } = useParams<{ token: string }>();
  const navigate = useNavigate();

  const { data: participantInfo, isLoading: isValidating, error: validationError } = useValidateParticipant(token);
  const joinMutation = useJoinConference();
  const leaveMutation = useLeaveConference();

  const webrtc = useWebRTC();

  const localVideoRef = useRef<HTMLVideoElement>(null);
  const remoteVideoRefs = useRef<Map<string, HTMLVideoElement>>(new Map());
  /** Bumped on every deliberate teardown; a recovery rejoin re-checks it afterwards and discards
   * the fresh session if the user left meanwhile, rather than transmitting behind an ended screen. */
  const sessionRef = useRef(0);

  const [state, setState] = useState<ConferenceState>(ConferenceState.Loading);
  const [timeRemaining, setTimeRemaining] = useState(0);
  const [isMicEnabled, setIsMicEnabled] = useState(true);
  const [isCameraEnabled, setIsCameraEnabled] = useState(true);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  useEffect(() => {
    if (localVideoRef.current && webrtc.localStream && localVideoRef.current.srcObject !== webrtc.localStream) {
      localVideoRef.current.srcObject = webrtc.localStream;
    }
  }, [webrtc.localStream]);

  useEffect(() => {
    for (const [participantId, stream] of Object.entries(webrtc.remoteStreams)) {
      const videoEl = remoteVideoRefs.current.get(participantId);
      if (videoEl && videoEl.srcObject !== stream) {
        videoEl.srcObject = stream;
      }
    }
  }, [webrtc.remoteStreams]);

  useEffect(() => {
    if (state !== ConferenceState.InConference) return;
    const url = `${API_URL}/api/v1/conferences/leave/${token}`;

    const notifyLeave = () => {
      const body = new Blob([], { type: 'application/json' });
      if (navigator.sendBeacon) {
        navigator.sendBeacon(url, body);
      } else {
        void fetch(url, { method: 'POST', body, keepalive: true, credentials: 'include' });
      }
    };

    window.addEventListener('pagehide', notifyLeave);
    window.addEventListener('beforeunload', notifyLeave);
    return () => {
      window.removeEventListener('pagehide', notifyLeave);
      window.removeEventListener('beforeunload', notifyLeave);
    };
  }, [state, token]);

  const conference = participantInfo ? {
    token: participantInfo.participantToken,
    incident: {
      id: participantInfo.incidentId,
      title: participantInfo.incidentTitle,
      severity: participantInfo.incidentSeverity as "Critical" | "High" | "Medium" | "Low",
    },
    timeRemaining: secondsUntil(participantInfo.expiresAt),
    currentUser: {
      id: participantInfo.participantToken,
      name: participantInfo.displayName,
      initials: participantInfo.displayName.split(' ').map(n => n[0]).join('').substring(0, 2).toUpperCase(),
    },
  } : null;

  useEffect(() => {
    if (isValidating) return;
    // Only the initial validation drives Loading → Lobby/Error; background refetches must not
    // throw a live call back to the Lobby.
    if (state !== ConferenceState.Loading) return;
    if (validationError || !participantInfo) {
      setErrorMessage(t("conference.errorDesc"));
      setState(ConferenceState.Error);
    } else {
      setTimeRemaining(secondsUntil(participantInfo.expiresAt));
      setState(ConferenceState.Lobby);
    }
  }, [isValidating, validationError, participantInfo, state]);

  useEffect(() => {
    if (state !== ConferenceState.InConference && state !== ConferenceState.Lobby) return;

    const interval = setInterval(() => {
      setTimeRemaining((prev) => (prev <= 1 ? 0 : prev - 1));
    }, 1000);

    return () => clearInterval(interval);
  }, [state]);

  // On expiry, tear the session down (leave the SDK conference + tell the backend) so mic/camera
  // stop transmitting — the page stays mounted on EndedState, so the unmount cleanup won't fire.
  useEffect(() => {
    if (timeRemaining > 0) return;
    if (state !== ConferenceState.InConference && state !== ConferenceState.Lobby) return;
    sessionRef.current++; // cancel any in-flight recovery rejoin
    void webrtc.leave();
    if (state === ConferenceState.InConference) {
      void leaveMutation.mutateAsync(token).catch(() => { /* empty */ });
    }
    setState(ConferenceState.Ended);
  }, [timeRemaining, state, webrtc, leaveMutation, token]);

  // A mid-call connection that FAILS terminally (not a transient reconnect) drops the user out
  // of the call. A clean server-side end (room expired/ended remotely — the provider suppresses
  // the event for deliberate leaves) closes the call screen instead of leaving dead media up.
  useEffect(() => {
    if (state !== ConferenceState.InConference) return;
    if (webrtc.connectionState === "error") {
      setErrorMessage(webrtc.error ?? t("conference.errorDesc"));
      setState(ConferenceState.Error);
    } else if (webrtc.connectionState === "disconnected") {
      setState(ConferenceState.Ended);
    }
  }, [webrtc.connectionState, webrtc.error, state]);

  const formatTime = (seconds: number) => {
    const mins = Math.floor(seconds / 60);
    const secs = seconds % 60;
    return `${mins.toString().padStart(2, "0")}:${secs.toString().padStart(2, "0")}`;
  };

  // Full join: fresh backend credentials (the login key is rotated per join) + a fresh
  // provider. Used by the lobby Join button AND by the automatic recovery after a drop.
  const performJoin = useCallback(async () => {
    const result = await joinMutation.mutateAsync(token);
    if (!result.success) throw new Error(result.error ?? t("conference.errorDesc"));
    const conferenceId = resolveVoximplantConferenceId(participantInfo, token);
    const contextData: Record<string, string> = { participant_token: token };
    if (participantInfo?.incidentId) contextData.incident_id = participantInfo.incidentId;
    if (participantInfo?.voximplantConferenceId) contextData.conference_id = participantInfo.voximplantConferenceId;
    await webrtc.join(result, conferenceId, contextData);
  }, [joinMutation, token, webrtc, participantInfo]);

  const handleJoinConference = useCallback(async () => {
    try {
      // Best-effort: surface the mic/camera permission prompt here in the lobby rather than
      // mid-join. Denial or a missing device is handled per-track after join (with a banner),
      // so we do not block audio-only participants.
      try {
        const probe = await navigator.mediaDevices.getUserMedia({ audio: true, video: true });
        probe.getTracks().forEach((tr) => tr.stop());
      } catch { /* handled post-join */ }

      await performJoin();
      setState(ConferenceState.InConference);
    } catch (err) {
      setErrorMessage(err instanceof Error && err.message ? err.message : t("conference.errorDesc"));
      setState(ConferenceState.Error);
    }
  }, [performJoin]);

  // Automatic recovery after an abnormal drop ('dropped'): the SDK's internal reconnection
  // re-creates its conference object, orphaning our listeners and local media — the session
  // object we hold is dead. A full fresh join (new login key, new provider) is the only
  // reliable recovery. One attempt per drop; a failed rejoin surfaces the Error screen.
  const rejoiningRef = useRef(false);
  useEffect(() => {
    if (state !== ConferenceState.InConference) return;
    if (webrtc.connectionState !== "dropped" || rejoiningRef.current) return;
    rejoiningRef.current = true;
    const session = sessionRef.current;
    void (async () => {
      try {
        await performJoin();
        if (sessionRef.current !== session) {
          // The user left (or the timer expired) while we were recovering — tear the fresh
          // session down; keeping it would transmit live mic/camera behind the Ended screen.
          void webrtc.leave();
          void leaveMutation.mutateAsync(token).catch(() => { /* empty */ });
          return;
        }
        // A fresh join publishes unmuted mic + camera — reapply what the user had chosen,
        // or a muted user would be transmitting live audio/video after the recovery.
        webrtc.toggleMic(isMicEnabled);
        webrtc.toggleCamera(isCameraEnabled);
      } catch {
        // Only surface the failure if this session is still the active one — a deliberate
        // leave mid-recovery must end on the Ended screen, not the Error screen.
        if (sessionRef.current === session) {
          setErrorMessage(t("conference.errorDesc"));
          setState(ConferenceState.Error);
        }
      } finally {
        rejoiningRef.current = false;
      }
    })();
  }, [webrtc, state, performJoin, leaveMutation, token, isMicEnabled, isCameraEnabled]);

  const handleLeaveConference = useCallback(async () => {
    sessionRef.current++; // cancel any in-flight recovery rejoin
    try {
      await webrtc.leave();
      if (state === ConferenceState.InConference) {
        await leaveMutation.mutateAsync(token);
      }
    } catch { /* empty */ }
    setState(ConferenceState.Ended);
  }, [leaveMutation, token, webrtc, state]);

  const handleToggleMic = () => {
    const newState = !isMicEnabled;
    setIsMicEnabled(newState);
    webrtc.toggleMic(newState);
  };

  const handleToggleCamera = () => {
    const newState = !isCameraEnabled;
    setIsCameraEnabled(newState);
    webrtc.toggleCamera(newState);
  };

  return (
    <div className="min-h-screen bg-gray-950">
      {state === ConferenceState.Loading && <LoadingState />}
      {state === ConferenceState.Lobby && conference && (
        <LobbyState
          conference={conference}
          timeRemaining={timeRemaining}
          formatTime={formatTime}
          onJoin={handleJoinConference}
        />
      )}
      {state === ConferenceState.InConference && conference && (
        <InConferenceState
          conference={conference}
          timeRemaining={timeRemaining}
          formatTime={formatTime}
          isMicEnabled={isMicEnabled}
          isCameraEnabled={isCameraEnabled}
          onToggleMic={handleToggleMic}
          onToggleCamera={handleToggleCamera}
          onLeave={handleLeaveConference}
          localVideoRef={localVideoRef}
          localStream={webrtc.localStream}
          participants={webrtc.participants}
          remoteStreams={webrtc.remoteStreams}
          remoteVideoRefs={remoteVideoRefs}
          connectionState={webrtc.connectionState}
          error={webrtc.error}
          devices={webrtc.devices}
          onSelectMic={webrtc.setAudioInput}
          onSelectCamera={webrtc.setVideoInput}
        />
      )}
      {state === ConferenceState.Ended && <EndedState onGoHome={() => navigate("/dashboard")} />}
      {state === ConferenceState.Error && <ErrorState message={errorMessage} onGoHome={() => navigate("/dashboard")} />}
    </div>
  );
}

function LoadingState() {
  return (
    <div className="flex min-h-screen items-center justify-center">
      <motion.div initial={{ opacity: 0, scale: 0.9 }} animate={{ opacity: 1, scale: 1 }} className="text-center">
        <div className="mb-4 inline-flex h-16 w-16 items-center justify-center">
          <Loader className="h-12 w-12 animate-spin text-brand-500" />
        </div>
        <p className="text-lg text-gray-300">{t("conference.loading")}</p>
      </motion.div>
    </div>
  );
}

function LobbyState({
  conference,
  timeRemaining,
  formatTime,
  onJoin,
}: {
  conference: ConferenceData;
  timeRemaining: number;
  formatTime: (s: number) => string;
  onJoin: () => void;
}) {
  const severityColors = {
    Critical: "bg-red-500/20 text-red-300 border-red-500/30",
    High: "bg-orange-500/20 text-orange-300 border-orange-500/30",
    Medium: "bg-blue-500/20 text-blue-300 border-blue-500/30",
    Low: "bg-gray-500/20 text-gray-300 border-gray-500/30",
  };

  return (
    <div className="flex min-h-screen items-center justify-center p-6">
      <motion.div
        initial={{ opacity: 0, y: 20 }}
        animate={{ opacity: 1, y: 0 }}
        className="w-full max-w-2xl space-y-6 text-center"
      >
        <div className="rounded-xl border border-white/10 bg-white/5 p-6 backdrop-blur-xl">
          <div className="mb-4 flex items-center justify-center gap-2">
            <span
              className={`rounded-lg border px-3 py-1 text-sm font-semibold ${(severityColors as Record<string, string>)[conference.incident.severity] ?? severityColors.Low}`}
            >
              {conference.incident.severity}
            </span>
          </div>
          <h1 className="text-3xl font-bold text-white">{conference.incident.title}</h1>
        </div>

        <div className="grid gap-4 sm:grid-cols-2">
          <div className="rounded-xl border border-white/10 bg-white/5 p-6 backdrop-blur-xl">
            <div className="mb-2 flex items-center justify-center">
              <div className="rounded-lg bg-brand-500/20 p-2">
                <Clock className="h-5 w-5 text-brand-400" />
              </div>
            </div>
            <p className="text-sm text-gray-400">{t("conference.lobbyDesc")}</p>
            <p className="text-2xl font-bold text-white">{formatTime(timeRemaining)}</p>
          </div>

          <div className="rounded-xl border border-white/10 bg-white/5 p-6 backdrop-blur-xl">
            <div className="mb-2 flex items-center justify-center">
              <div className="rounded-lg bg-purple-500/20 p-2">
                <Users className="h-5 w-5 text-purple-400" />
              </div>
            </div>
            <p className="text-sm text-gray-400">{t("conference.participants")}</p>
            <p className="text-2xl font-bold text-white">{t("conference.lobbyTitle")}</p>
          </div>
        </div>

        <div className="relative aspect-video overflow-hidden rounded-xl border border-white/10 bg-gradient-to-br from-brand-500/10 to-purple-500/10">
          <div className="absolute inset-0 flex items-center justify-center">
            <div className="text-center">
              <div className="mx-auto mb-4 flex h-20 w-20 items-center justify-center rounded-full bg-brand-500/20">
                <Video className="h-10 w-10 text-brand-400" />
              </div>
              <p className="text-lg font-medium text-white">{t("conference.lobbyTitle")}, {conference.currentUser.name}</p>
              <p className="mt-2 text-sm text-gray-400">{t("conference.lobbyDesc")}</p>
            </div>
          </div>
        </div>

        <motion.button
          onClick={onJoin}
          whileHover={{ scale: 1.02 }}
          whileTap={{ scale: 0.98 }}
          className="w-full rounded-xl bg-gradient-to-r from-brand-500 to-brand-600 py-4 text-lg font-semibold text-white shadow-lg transition-all hover:shadow-brand-500/50"
        >
          {t("conference.joinConference")}
        </motion.button>
      </motion.div>
    </div>
  );
}

const avatarColors = [
  "bg-brand-500", "bg-purple-500", "bg-indigo-500", "bg-cyan-500",
  "bg-emerald-500", "bg-amber-500", "bg-rose-500", "bg-teal-500",
];

function getGridClass(total: number): string {
  if (total <= 1) return "grid-cols-1";
  if (total <= 2) return "grid-cols-2";
  if (total <= 4) return "grid-cols-2 grid-rows-2";
  if (total <= 9) return "grid-cols-3";
  return "grid-cols-4";
}

function InConferenceState({
  conference,
  timeRemaining,
  formatTime,
  isMicEnabled,
  isCameraEnabled,
  onToggleMic,
  onToggleCamera,
  onLeave,
  localVideoRef,
  localStream,
  participants,
  remoteStreams,
  remoteVideoRefs,
  connectionState,
  error,
  devices,
  onSelectMic,
  onSelectCamera,
}: {
  conference: ConferenceData;
  timeRemaining: number;
  formatTime: (s: number) => string;
  isMicEnabled: boolean;
  isCameraEnabled: boolean;
  onToggleMic: () => void;
  onToggleCamera: () => void;
  onLeave: () => void;
  localVideoRef: React.RefObject<HTMLVideoElement | null>;
  localStream: MediaStream | null;
  participants: WebRTCParticipant[];
  remoteStreams: Record<string, MediaStream>;
  remoteVideoRefs: React.MutableRefObject<Map<string, HTMLVideoElement>>;
  connectionState: string;
  error: string | null;
  devices: DeviceList;
  onSelectMic: (deviceId: string) => void;
  onSelectCamera: (deviceId: string) => void;
}) {
  return (
    <div className="flex min-h-screen flex-col bg-gray-950 p-4">
      {error && (
        <div className="mb-3 flex items-center gap-2 rounded-lg border border-amber-500/30 bg-amber-500/10 px-4 py-2 text-sm text-amber-200">
          <AlertCircle className="h-4 w-4 shrink-0" />
          <span>{error}</span>
        </div>
      )}
      <div className="mb-4 flex items-center justify-between rounded-xl border border-white/10 bg-white/5 px-6 py-3 backdrop-blur-xl">
        <div>
          <h2 className="text-lg font-semibold text-white">{conference.incident.title}</h2>
          <p className="text-sm text-gray-400">{t("conference.conferenceActive")}</p>
        </div>
        <div className="flex items-center gap-4">
          <div className="flex items-center gap-2 text-gray-400">
            <Clock className="h-4 w-4" />
            <span className="font-mono text-sm">{formatTime(timeRemaining)}</span>
          </div>
          <div className="flex items-center gap-2 text-gray-400">
            <Users className="h-4 w-4" />
            <span className="text-sm">{participants.length + 1}</span>
          </div>
          {connectionState === "connecting" && (
            <div className="flex items-center gap-1 text-amber-400">
              <Loader className="h-3 w-3 animate-spin" />
              <span className="text-xs">{t("conference.joining")}</span>
            </div>
          )}
          {(connectionState === "reconnecting" || connectionState === "dropped") && (
            <div className="flex items-center gap-1 text-amber-400">
              <Loader className="h-3 w-3 animate-spin" />
              <span className="text-xs">{t("conference.reconnecting")}</span>
            </div>
          )}
        </div>
      </div>

      <div className={`flex-1 grid gap-4 ${getGridClass(participants.length + 1)}`}>
        <motion.div
          initial={{ opacity: 0, scale: 0.9 }}
          animate={{ opacity: 1, scale: 1 }}
          className="relative min-h-[200px] overflow-hidden rounded-xl border border-white/10 bg-gradient-to-br from-brand-500/20 to-purple-500/20"
        >
          {localStream ? (
            <video
              // Attaches on mount, which an effect keyed on localStream alone would miss; the
              // !== guard stops the per-second countdown re-render from reloading the media.
              ref={(el) => {
                localVideoRef.current = el;
                if (el && localStream && el.srcObject !== localStream) el.srcObject = localStream;
              }}
              autoPlay
              muted
              playsInline
              className="absolute inset-0 h-full w-full object-contain"
            />
          ) : (
            <div className="absolute inset-0 flex items-center justify-center">
              <div className="text-center">
                <div className="mx-auto mb-2 flex h-16 w-16 items-center justify-center rounded-full bg-brand-500 text-2xl font-bold text-white">
                  {conference.currentUser.initials}
                </div>
                <p className="font-medium text-white">{conference.currentUser.name}</p>
              </div>
            </div>
          )}
          <div className="absolute bottom-2 left-2 rounded-md bg-black/60 px-2 py-1 text-xs text-white backdrop-blur-sm">
            {conference.currentUser.name} ({t("conference.participants")})
          </div>
          {!isCameraEnabled && (
            <div className="absolute inset-0 flex items-center justify-center bg-black/60">
              <VideoOff className="h-8 w-8 text-gray-400" />
            </div>
          )}
        </motion.div>

        {participants.map((participant, index) => {
          const stream = remoteStreams[participant.id];
          // Keep the <video> mounted whenever a stream exists so audio keeps playing, but
          // overlay the avatar when the remote camera is off (no video tracks) — otherwise
          // the tile renders as a black rectangle.
          const hasVideo = !!stream && stream.getVideoTracks().length > 0;
          const color = avatarColors[index % avatarColors.length];
          const initials = participant.displayName.split(' ').map(n => n[0]).join('').substring(0, 2).toUpperCase();

          return (
            <motion.div
              key={participant.id}
              initial={{ opacity: 0, scale: 0.9 }}
              animate={{ opacity: 1, scale: 1 }}
              transition={{ delay: index * 0.1 }}
              className="relative min-h-[200px] overflow-hidden rounded-xl border border-white/10 bg-gradient-to-br from-purple-500/20 to-indigo-500/20"
            >
              {stream && (
                <video
                  ref={(el) => {
                    if (el) remoteVideoRefs.current.set(participant.id, el);
                    else remoteVideoRefs.current.delete(participant.id);
                  }}
                  autoPlay
                  playsInline
                  className="absolute inset-0 h-full w-full object-contain"
                />
              )}
              {!hasVideo && (
                <div className="absolute inset-0 flex items-center justify-center">
                  <div className="text-center">
                    <div className={`mx-auto mb-2 flex h-16 w-16 items-center justify-center rounded-full ${color} text-2xl font-bold text-white`}>
                      {initials}
                    </div>
                    <p className="font-medium text-white">{participant.displayName}</p>
                  </div>
                </div>
              )}
              <div className="absolute bottom-2 left-2 rounded-md bg-black/60 px-2 py-1 text-xs text-white backdrop-blur-sm">
                {participant.displayName}
                {participant.isMuted && <MicOff className="ml-1 inline h-3 w-3 text-red-400" />}
              </div>
            </motion.div>
          );
        })}
      </div>

      {(devices.microphones.length > 1 || devices.cameras.length > 1) && (
        <div className="mt-4 flex flex-wrap items-center justify-center gap-3">
          {devices.microphones.length > 1 && (
            <select
              onChange={(e) => onSelectMic(e.target.value)}
              aria-label="Select microphone"
              className="max-w-[220px] rounded-lg border border-white/10 bg-gray-800 px-3 py-1.5 text-xs text-gray-200"
            >
              {devices.microphones.map((d) => (
                <option key={d.deviceId} value={d.deviceId}>{d.label}</option>
              ))}
            </select>
          )}
          {devices.cameras.length > 1 && (
            <select
              onChange={(e) => onSelectCamera(e.target.value)}
              aria-label="Select camera"
              className="max-w-[220px] rounded-lg border border-white/10 bg-gray-800 px-3 py-1.5 text-xs text-gray-200"
            >
              {devices.cameras.map((d) => (
                <option key={d.deviceId} value={d.deviceId}>{d.label}</option>
              ))}
            </select>
          )}
        </div>
      )}

      <div className="mt-4 flex items-center justify-center gap-4">
        <button
          onClick={onToggleMic}
          className={`flex h-14 w-14 items-center justify-center rounded-full transition-colors ${isMicEnabled ? "bg-gray-700 hover:bg-gray-600" : "bg-red-600 hover:bg-red-700"
            }`}
        >
          {isMicEnabled ? <Mic className="h-6 w-6 text-white" /> : <MicOff className="h-6 w-6 text-white" />}
        </button>

        <button
          onClick={onToggleCamera}
          className={`flex h-14 w-14 items-center justify-center rounded-full transition-colors ${isCameraEnabled ? "bg-gray-700 hover:bg-gray-600" : "bg-red-600 hover:bg-red-700"
            }`}
        >
          {isCameraEnabled ? <Video className="h-6 w-6 text-white" /> : <VideoOff className="h-6 w-6 text-white" />}
        </button>

        <button
          onClick={onLeave}
          className="flex h-16 w-16 items-center justify-center rounded-full bg-red-600 transition-colors hover:bg-red-700"
        >
          <Phone className="h-6 w-6 rotate-[135deg] text-white" />
        </button>
      </div>
    </div>
  );
}

function EndedState({ onGoHome }: { onGoHome: () => void }) {
  return (
    <div className="flex min-h-screen items-center justify-center p-6">
      <motion.div
        initial={{ opacity: 0, scale: 0.9 }}
        animate={{ opacity: 1, scale: 1 }}
        className="w-full max-w-md text-center"
      >
        <div className="mb-6 inline-flex h-20 w-20 items-center justify-center rounded-full bg-green-500/20">
          <CheckCircle className="h-10 w-10 text-green-400" />
        </div>
        <h1 className="text-3xl font-bold text-white">{t("conference.ended")}</h1>
        <p className="mt-4 text-gray-400">{t("conference.endedDesc")}</p>
        <button
          onClick={onGoHome}
          className="mt-8 rounded-lg bg-brand-500 px-6 py-3 font-medium text-white transition-colors hover:bg-brand-600"
        >
          {t("conference.returnToIncident")}
        </button>
      </motion.div>
    </div>
  );
}

function ErrorState({ message, onGoHome }: { message: string | null; onGoHome: () => void }) {
  return (
    <div className="flex min-h-screen items-center justify-center p-6">
      <motion.div
        initial={{ opacity: 0, scale: 0.9 }}
        animate={{ opacity: 1, scale: 1 }}
        className="w-full max-w-md text-center"
      >
        <div className="mb-6 inline-flex h-20 w-20 items-center justify-center rounded-full bg-red-500/20">
          <AlertCircle className="h-10 w-10 text-red-400" />
        </div>
        <h1 className="text-3xl font-bold text-white">{t("conference.errorTitle")}</h1>
        <p className="mt-4 text-gray-400">{message || t("conference.errorDesc")}</p>
        <button
          onClick={onGoHome}
          className="mt-8 rounded-lg bg-brand-500 px-6 py-3 font-medium text-white transition-colors hover:bg-brand-600"
        >
          {t("conference.tryAgain")}
        </button>
      </motion.div>
    </div>
  );
}
