/**
 * TTS Template Section — manages TTS template list + create/edit modal.
 * Dynamically loads all TTS message keys from the backend.
 * Groups keys by category (Call Flow, Conference).
 */

import { useState, useEffect, useCallback } from "react";
import { toast } from "@/shared/utils/toast";
import { t } from "@/shared/locales/i18n";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Badge } from "@/shared/components/ui/badge";
import { Card } from "@/shared/components/ui/card";
import {
    Select,
    SelectContent,
    SelectItem,
    SelectTrigger,
    SelectValue,
} from "@/shared/components/ui/select";
import {
    Dialog,
    DialogContent,
    DialogDescription,
    DialogHeader,
    DialogTitle,
    DialogFooter,
} from "@/shared/components/ui/dialog";
import {
    Collapsible,
    CollapsibleContent,
    CollapsibleTrigger,
} from "@/shared/components/ui/collapsible";
import {
    Languages,
    Plus,
    Edit,
    Trash2,
    Save,
    Star,
    RotateCcw,
    ChevronDown,
} from "lucide-react";
import type { UseMutationResult } from "@tanstack/react-query";
import type {
    TtsTemplateDto,
    TtsKeyDescriptor,
    TtsTemplateSaveRequest,
} from "../../settings/types/communications.types";
import type { ApiError } from "@/shared/api";
import { communicationsApi } from "../../settings/api/communications.api";

const languageOptions = [
    { value: "tr-TR", label: "🇹🇷 Türkçe" },
    { value: "en-US", label: "🇺🇸 English (US)" },
    { value: "en-GB", label: "🇬🇧 English (UK)" },
    { value: "de-DE", label: "🇩🇪 Deutsch" },
    { value: "fr-FR", label: "🇫🇷 Français" },
    { value: "es-ES", label: "🇪🇸 Español" },
    { value: "it-IT", label: "🇮🇹 Italiano" },
    { value: "pt-BR", label: "🇧🇷 Português (BR)" },
    { value: "nl-NL", label: "🇳🇱 Nederlands" },
    { value: "ja-JP", label: "🇯🇵 日本語" },
    { value: "ko-KR", label: "🇰🇷 한국어" },
    { value: "zh-CN", label: "🇨🇳 中文" },
    { value: "ar-SA", label: "🇸🇦 العربية" },
    { value: "ru-RU", label: "🇷🇺 Русский" },
];

function getFlagEmoji(langCode: string) {
    const map: Record<string, string> = {
        "tr-TR": "🇹🇷", "en-US": "🇺🇸", "en-GB": "🇬🇧", "de-DE": "🇩🇪",
        "fr-FR": "🇫🇷", "es-ES": "🇪🇸", "it-IT": "🇮🇹", "pt-BR": "🇧🇷",
        "nl-NL": "🇳🇱", "ja-JP": "🇯🇵", "ko-KR": "🇰🇷", "zh-CN": "🇨🇳",
        "ar-SA": "🇸🇦", "ru-RU": "🇷🇺",
    };
    return map[langCode] || "🌐";
}

interface TtsTemplateSectionProps {
    ttsTemplates: TtsTemplateDto[];
    saveTtsTemplateMutation: UseMutationResult<unknown, ApiError, TtsTemplateSaveRequest>;
    isSaving: boolean;
    onRequestDelete: (target: { type: "tts"; id: string; name: string }) => void;
}

export function TtsTemplateSection({
    ttsTemplates,
    saveTtsTemplateMutation,
    isSaving,
    onRequestDelete,
}: TtsTemplateSectionProps) {
    const [isModalOpen, setIsModalOpen] = useState(false);
    const [editingTts, setEditingTts] = useState<TtsTemplateDto | null>(null);
    const [ttsKeys, setTtsKeys] = useState<TtsKeyDescriptor[]>([]);
    const [loadingDefaults, setLoadingDefaults] = useState(false);

    const [formData, setFormData] = useState({
        languageCode: "",
        displayName: "",
        isDefault: false,
        messages: {} as Record<string, string>,
    });

    useEffect(() => {
        communicationsApi.getTtsKeys().then((res) => {
            if (res.success && res.data) {
                setTtsKeys(res.data);
            }
        });
    }, []);

    const groupedKeys = ttsKeys.reduce<Record<string, TtsKeyDescriptor[]>>((acc, key) => {
        if (!acc[key.group]) acc[key.group] = [];
        acc[key.group].push(key);
        return acc;
    }, {});

    const loadDefaults = useCallback(async (langCode: string) => {
        if (!langCode) return;
        setLoadingDefaults(true);
        try {
            const res = await communicationsApi.getTtsDefaults(langCode);
            if (res.success && res.data) {
                setFormData((prev) => ({
                    ...prev,
                    messages: { ...res.data },
                }));
            }
        } catch {
            /* empty */
        } finally {
            setLoadingDefaults(false);
        }
    }, []);

    const handleCreate = () => {
        setEditingTts(null);
        setFormData({ languageCode: "", displayName: "", isDefault: false, messages: {} });
        setIsModalOpen(true);
    };

    const handleEdit = (template: TtsTemplateDto) => {
        setEditingTts(template);
        setFormData({
            languageCode: template.languageCode,
            displayName: template.displayName,
            isDefault: template.isDefault,
            messages: template.messages || {},
        });
        setIsModalOpen(true);
    };

    const handleSave = async () => {
        try {
            await saveTtsTemplateMutation.mutateAsync({
                languageCode: formData.languageCode,
                displayName: formData.displayName,
                isDefault: formData.isDefault,
                messages: formData.messages,
            });
            toast.success(editingTts ? t("tts.updated") : t("tts.created"));
            setIsModalOpen(false);
            setFormData({ languageCode: "", displayName: "", isDefault: false, messages: {} });
        } catch (err) {
            toast.error(err instanceof Error ? err.message : t("tts.saveFailed"));
        }
    };

    const handleLanguageChange = (value: string) => {
        setFormData({ ...formData, languageCode: value });
        if (!editingTts) {
            loadDefaults(value);
        }
    };

    return (
        <>
            <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
                <div className="flex items-start gap-4 mb-6">
                    <div className="w-10 h-10 rounded-lg bg-purple-500/10 flex items-center justify-center flex-shrink-0">
                        <Languages className="w-5 h-5 text-purple-500" />
                    </div>
                    <div className="flex-1">
                        <h3 style={{ fontSize: "1.125rem", fontWeight: 600 }}>{t("tts.title")}</h3>
                        <p style={{ fontSize: "0.875rem", color: "#94A3B8", marginTop: "0.25rem" }}>
                            {t("tts.description")}
                        </p>
                    </div>
                    <Button
                        onClick={handleCreate}
                        variant="outline"
                        className="bg-input-background"
                    >
                        <Plus className="w-4 h-4 mr-2" />
                        {t("tts.addLanguage")}
                    </Button>
                </div>

                {ttsTemplates.length > 0 ? (
                    <div className="space-y-3">
                        {ttsTemplates.map((template) => (
                            <div
                                key={template.id}
                                className="p-4 rounded-lg border border-border bg-surface-light/20 hover:border-border-light transition-all flex items-center gap-4"
                            >
                                <div className="w-10 h-10 rounded-lg bg-purple-500/10 flex items-center justify-center flex-shrink-0 text-xl">
                                    {getFlagEmoji(template.languageCode)}
                                </div>
                                <div className="flex-1 min-w-0">
                                    <div className="flex items-center gap-2">
                                        <p style={{ fontSize: "0.9375rem", fontWeight: 600 }}>{template.displayName}</p>
                                        {template.isDefault && (
                                            <Badge className="bg-amber-500/10 text-amber-500 border border-amber-500/20 text-xs">
                                                <Star className="w-3 h-3 mr-1" />
                                                {t("common.default")}
                                            </Badge>
                                        )}
                                    </div>
                                    <p style={{ fontSize: "0.8125rem", color: "#94A3B8" }}>
                                        {template.languageCode} •{" "}
                                        {Object.keys(template.messages || {}).length} {t("tts.messages")}
                                    </p>
                                </div>
                                <div className="flex gap-2">
                                    <Button size="sm" variant="ghost" onClick={() => handleEdit(template)}>
                                        <Edit className="w-4 h-4" />
                                    </Button>
                                    <Button
                                        size="sm"
                                        variant="ghost"
                                        onClick={() =>
                                            onRequestDelete({ type: "tts", id: template.id, name: template.displayName })
                                        }
                                        className="text-error-500 hover:bg-error-500/10"
                                    >
                                        <Trash2 className="w-4 h-4" />
                                    </Button>
                                </div>
                            </div>
                        ))}
                    </div>
                ) : (
                    <div className="text-center py-8">
                        <Languages className="w-10 h-10 text-muted-foreground mx-auto mb-3 opacity-50" />
                        <p style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.25rem" }}>
                            {t("tts.noTemplates")}
                        </p>
                        <p style={{ fontSize: "0.8125rem", color: "#94A3B8" }}>
                            {t("tts.noTemplatesDesc")}
                        </p>
                    </div>
                )}
            </Card>

            <Dialog open={isModalOpen} onOpenChange={setIsModalOpen}>
                <DialogContent className="bg-card border-border sm:max-w-[750px] max-h-[90vh] overflow-y-auto">
                    <DialogHeader>
                        <DialogTitle style={{ fontSize: "1.5rem", fontWeight: 600 }}>
                            {editingTts ? `${t("tts.editMessages")} — ${editingTts.displayName}` : t("tts.addTemplate")}
                        </DialogTitle>
                        <DialogDescription style={{ fontSize: "0.875rem", color: "#94A3B8" }}>
                            {t("tts.configureVoice")}
                        </DialogDescription>
                    </DialogHeader>

                    <div className="space-y-5 py-4">
                        <div>
                            <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                                {t("tts.language")} <span className="text-error-500">*</span>
                            </label>
                            <Select
                                value={formData.languageCode}
                                onValueChange={handleLanguageChange}
                                disabled={!!editingTts}
                            >
                                <SelectTrigger className="bg-input-background">
                                    <SelectValue placeholder={t("tts.selectLanguage")} />
                                </SelectTrigger>
                                <SelectContent>
                                    {languageOptions.map((lang) => (
                                        <SelectItem key={lang.value} value={lang.value}>
                                            {lang.label}
                                        </SelectItem>
                                    ))}
                                </SelectContent>
                            </Select>
                        </div>

                        <div>
                            <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                                {t("tts.displayName")}
                            </label>
                            <Input
                                placeholder={t("tts.displayNamePlaceholder")}
                                value={formData.displayName}
                                onChange={(e) => setFormData({ ...formData, displayName: e.target.value })}
                                className="bg-input-background"
                            />
                        </div>

                        <div className="flex items-center gap-2 p-3 rounded-lg bg-surface-light/20">
                            <input
                                type="checkbox"
                                id="is-default-tts"
                                checked={formData.isDefault}
                                onChange={(e) => setFormData({ ...formData, isDefault: e.target.checked })}
                                className="w-4 h-4 rounded border-border bg-input-background"
                            />
                            <label htmlFor="is-default-tts" style={{ fontSize: "0.875rem", cursor: "pointer" }}>
                                {t("tts.setDefault")}
                            </label>
                        </div>

                        <div>
                            <div className="flex items-center justify-between mb-4">
                                <h4 style={{ fontSize: "0.9375rem", fontWeight: 600 }}>
                                    {t("tts.messageTemplates")}
                                </h4>
                                <Button
                                    variant="outline"
                                    size="sm"
                                    className="bg-input-background text-xs"
                                    disabled={!formData.languageCode || loadingDefaults}
                                    onClick={() => loadDefaults(formData.languageCode)}
                                >
                                    {loadingDefaults ? (
                                        <div className="w-3 h-3 border-2 border-current/30 border-t-current rounded-full animate-spin mr-1.5" />
                                    ) : (
                                        <RotateCcw className="w-3 h-3 mr-1.5" />
                                    )}
                                    {t("tts.loadDefaults")}
                                </Button>
                            </div>

                            {Object.entries(groupedKeys).map(([group, keys]) => (
                                <Collapsible key={group} defaultOpen className="mb-3">
                                    <CollapsibleTrigger className="flex items-center gap-2 w-full p-3 rounded-lg bg-surface-light/20 hover:bg-surface-light/30 transition-colors text-left group">
                                        <ChevronDown className="w-4 h-4 text-muted-foreground transition-transform group-data-[state=closed]:-rotate-90" />
                                        <span style={{ fontSize: "0.8125rem", fontWeight: 600, textTransform: "uppercase", letterSpacing: "0.05em" }}>
                                            {t(`tts.group.${group}`)}
                                        </span>
                                        <span style={{ fontSize: "0.75rem", color: "#94A3B8", marginLeft: "auto" }}>
                                            {keys.length} {t("tts.messages")}
                                        </span>
                                    </CollapsibleTrigger>
                                    <CollapsibleContent className="space-y-3 pt-3 pl-2">
                                        {keys.map((keyDesc) => (
                                            <div key={keyDesc.key}>
                                                <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.25rem", display: "block" }}>
                                                    {keyDesc.label}
                                                </label>
                                                <p style={{ fontSize: "0.75rem", color: "#94A3B8", marginBottom: "0.5rem" }}>
                                                    {keyDesc.description}
                                                </p>
                                                <Input
                                                    value={formData.messages[keyDesc.key] || ""}
                                                    onChange={(e) =>
                                                        setFormData({
                                                            ...formData,
                                                            messages: { ...formData.messages, [keyDesc.key]: e.target.value },
                                                        })
                                                    }
                                                    className="bg-input-background"
                                                />
                                            </div>
                                        ))}
                                    </CollapsibleContent>
                                </Collapsible>
                            ))}
                        </div>
                    </div>

                    <DialogFooter>
                        <Button variant="outline" onClick={() => setIsModalOpen(false)} className="bg-input-background">
                            {t("common.cancel")}
                        </Button>
                        <Button
                            disabled={!formData.languageCode || !formData.displayName || isSaving}
                            className="bg-brand-500 hover:bg-brand-600 text-white"
                            onClick={handleSave}
                        >
                            {isSaving ? (
                                <>
                                    <div className="w-4 h-4 border-2 border-white/30 border-t-white rounded-full animate-spin mr-2" />
                                    {t("common.saving")}
                                </>
                            ) : (
                                <>
                                    <Save className="w-4 h-4 mr-2" />
                                    {editingTts ? t("tts.updateTemplate") : t("tts.createTemplate")}
                                </>
                            )}
                        </Button>
                    </DialogFooter>
                </DialogContent>
            </Dialog>
        </>
    );
}
