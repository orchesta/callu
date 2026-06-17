/**
 * useActiveSpeaker — Detects the loudest speaker using Web Audio API.
 *
 * Since Voximplant does not have a native "dominant speaker" event,
 * we analyse each remote participant's audio stream every 150ms and
 * track the one with the highest RMS volume level.
 *
 * Returns: activeSpeakerId (string | null)
 */

import { useState, useEffect, useRef } from 'react';

const POLL_INTERVAL_MS = 150;
const SILENCE_THRESHOLD = 5;

export function useActiveSpeaker(
    remoteStreams: Record<string, MediaStream>,
): string | null {
    const [activeSpeakerId, setActiveSpeakerId] = useState<string | null>(null);
    const audioContextRef = useRef<AudioContext | null>(null);
    const analysersRef = useRef<Map<string, AnalyserNode>>(new Map());
    const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null);

    useEffect(() => {
        if (!audioContextRef.current) {
            audioContextRef.current = new AudioContext();
        }
        const ctx = audioContextRef.current;
        const current = analysersRef.current;
        const incomingIds = new Set(Object.keys(remoteStreams));

        for (const [id, stream] of Object.entries(remoteStreams)) {
            if (!current.has(id)) {
                try {
                    const source = ctx.createMediaStreamSource(stream);
                    const analyser = ctx.createAnalyser();
                    analyser.fftSize = 256;
                    source.connect(analyser);
                    current.set(id, analyser);
                } catch {
                    /* empty */
                }
            }
        }

        for (const id of current.keys()) {
            if (!incomingIds.has(id)) {
                current.delete(id);
            }
        }
    }, [remoteStreams]);

    useEffect(() => {
        intervalRef.current = setInterval(() => {
            const analysers = analysersRef.current;
            if (analysers.size === 0) {
                setActiveSpeakerId(null);
                return;
            }

            let maxRms = SILENCE_THRESHOLD;
            let loudestId: string | null = null;
            const dataArray = new Uint8Array(256);

            for (const [id, analyser] of analysers) {
                analyser.getByteTimeDomainData(dataArray);
                let sumSquares = 0;
                for (const v of dataArray) {
                    const normalized = (v - 128) / 128;
                    sumSquares += normalized * normalized;
                }
                const rms = Math.sqrt(sumSquares / dataArray.length) * 255;
                if (rms > maxRms) {
                    maxRms = rms;
                    loudestId = id;
                }
            }

            setActiveSpeakerId((prev) => (loudestId !== prev ? loudestId : prev));
        }, POLL_INTERVAL_MS);

        return () => {
            if (intervalRef.current) clearInterval(intervalRef.current);
        };
    }, []);

    useEffect(() => {
        return () => {
            if (intervalRef.current) clearInterval(intervalRef.current);
            if (audioContextRef.current) {
                audioContextRef.current.close();
                audioContextRef.current = null;
            }
        };
    }, []);

    return activeSpeakerId;
}
