/** Free-text synthesis: type what a call would say, in as many languages as it would say it, and hear it. */

import { useEffect, useState } from "react";
import { t } from "@/shared/locales/i18n";
import { toast } from "@/shared/utils/toast";
import { Button } from "@/shared/components/ui/button";
import { Card } from "@/shared/components/ui/card";
import { Textarea } from "@/shared/components/ui/textarea";
import {
    Select,
    SelectContent,
    SelectItem,
    SelectTrigger,
    SelectValue,
} from "@/shared/components/ui/select";
import { usePreviewTts } from "@/features/settings/hooks/use-communications";
import { communicationsApi } from "@/features/settings/api/communications.api";
import type { TtsPreviewSegment } from "@/features/settings/types/communications.types";

/** What the voice service can pronounce. Callu ships templates for two of these; the rest still speak. */
const LANGUAGES = [
    { code: "tr-TR", label: "Türkçe" },
    { code: "en-US", label: "English" },
    { code: "de-DE", label: "Deutsch" },
    { code: "es-ES", label: "Español" },
    { code: "fr-FR", label: "Français" },
    { code: "it-IT", label: "Italiano" },
    { code: "pt-BR", label: "Português" },
    { code: "ru-RU", label: "Русский" },
] as const;

const MAX_SEGMENTS = 16;
const MAX_SEGMENT_CHARS = 1000;

interface Draft {
    text: string;
    lang: string;
}

export function TtsPreviewSection() {
    const [segments, setSegments] = useState<Draft[]>([
        { text: "", lang: "tr-TR" },
    ]);
    const [rendered, setRendered] = useState<TtsPreviewSegment[] | null>(null);

    const preview = usePreviewTts();

    const update = (index: number, patch: Partial<Draft>) =>
        setSegments((current) => current.map((s, i) => (i === index ? { ...s, ...patch } : s)));

    const remove = (index: number) =>
        setSegments((current) => (current.length === 1 ? current : current.filter((_, i) => i !== index)));

    const add = () =>
        setSegments((current) =>
            current.length >= MAX_SEGMENTS
                ? current
                : [...current, { text: "", lang: current[current.length - 1]?.lang ?? "tr-TR" }],
        );

    const render = async () => {
        const usable = segments
            .map((s) => ({ text: s.text.trim(), lang: s.lang }))
            .filter((s) => s.text.length > 0);

        if (usable.length === 0) {
            toast.error(t("tts.preview.needsText"));
            return;
        }

        setRendered(null);

        try {
            const result = await preview.mutateAsync({ segments: usable });
            setRendered(result);
        } catch {
            // useApiMutation already surfaces the reason; leaving the previous render on screen would
            // suggest it is what this text sounds like.
            setRendered(null);
        }
    };

    return (
        <Card className="p-6 space-y-4">
            <div>
                <h3 className="text-lg font-semibold">{t("tts.preview.title")}</h3>
                <p className="text-sm text-muted-foreground">{t("tts.preview.description")}</p>
            </div>

            <div className="space-y-3">
                {segments.map((segment, index) => (
                    <div key={index} className="flex gap-2 items-start">
                        <Textarea
                            value={segment.text}
                            maxLength={MAX_SEGMENT_CHARS}
                            rows={2}
                            className="flex-1"
                            placeholder={t("tts.preview.textPlaceholder")}
                            onChange={(e) => update(index, { text: e.target.value })}
                            aria-label={t("tts.preview.segmentText")}
                        />
                        <Select value={segment.lang} onValueChange={(lang) => update(index, { lang })}>
                            <SelectTrigger className="w-40" aria-label={t("tts.preview.segmentLanguage")}>
                                <SelectValue />
                            </SelectTrigger>
                            <SelectContent>
                                {LANGUAGES.map((language) => (
                                    <SelectItem key={language.code} value={language.code}>
                                        {language.label}
                                    </SelectItem>
                                ))}
                            </SelectContent>
                        </Select>
                        <Button
                            variant="ghost"
                            size="sm"
                            disabled={segments.length === 1}
                            onClick={() => remove(index)}
                        >
                            {t("common.remove")}
                        </Button>
                    </div>
                ))}
            </div>

            <div className="flex gap-2">
                <Button variant="outline" size="sm" onClick={add} disabled={segments.length >= MAX_SEGMENTS}>
                    {t("tts.preview.addSegment")}
                </Button>
                <Button size="sm" onClick={render} disabled={preview.isPending}>
                    {preview.isPending ? t("tts.preview.rendering") : t("tts.preview.render")}
                </Button>
            </div>

            {rendered && rendered.length > 0 && (
                <div className="space-y-3 pt-2">
                    {rendered.map((segment) => (
                        <RenderedSegment key={segment.key} segment={segment} />
                    ))}
                </div>
            )}
        </Card>
    );
}

/** One rendered segment: what was spoken, how long it runs, and a player for it. */
export function RenderedSegment({ segment }: { segment: TtsPreviewSegment }) {
    const [src, setSrc] = useState<string | null>(null);
    const [failed, setFailed] = useState(false);

    // The segment owns its object URL and revokes it on unmount. Handing that to the parent through a
    // callback put an unstable value in this dependency list, which revoked a URL still being played.
    useEffect(() => {
        let cancelled = false;
        let objectUrl: string | null = null;

        communicationsApi
            .ttsPreviewAudio(segment.key)
            .then((url) => {
                if (cancelled) {
                    URL.revokeObjectURL(url);
                    return;
                }
                objectUrl = url;
                setSrc(url);
            })
            .catch(() => {
                if (!cancelled) setFailed(true);
            });

        return () => {
            cancelled = true;
            if (objectUrl) URL.revokeObjectURL(objectUrl);
        };
    }, [segment.key]);

    return (
        <div className="rounded-md border p-3 space-y-2">
            {/* The spoken text, not the text that was typed: this is where a number left unread shows. */}
            <p className="text-sm">{segment.normalized}</p>
            <p className="text-xs text-muted-foreground">
                {t("tts.preview.duration", { seconds: segment.duration_s.toFixed(1) })}
            </p>
            {failed ? (
                <p className="text-xs text-destructive">{t("tts.preview.audioUnavailable")}</p>
            ) : (
                src && <audio controls src={src} className="w-full" />
            )}
        </div>
    );
}
