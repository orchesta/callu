/**
 * Provider Section — manages communication provider list + create/edit modal.
 * Extracted from CommunicationsHub for SRP.
 */

import { useState, useId } from "react";
import { useNavigate } from "react-router";
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
    Radio,
    Phone,
    PhoneCall,
    Plus,
    Edit,
    Trash2,
    CheckCircle,
    AlertCircle,
    Info,
    Eye,
    EyeOff,
    Save,
    Settings,
    Send,
} from "lucide-react";
import type { CommunicationProviderDto, SipTrunkDto } from "../../settings/types/communications.types";
import type { useCreateProvider, useUpdateProvider } from "../../settings/hooks/use-communications";
import { communicationsApi } from "../../settings/api/communications.api";
import { dateLocale } from "@/shared/utils/datetime";

interface ProviderSectionProps {
    providers: CommunicationProviderDto[];
    sipTrunks: SipTrunkDto[];
    createProviderMutation: ReturnType<typeof useCreateProvider>;
    updateProviderMutation: ReturnType<typeof useUpdateProvider>;

    isSaving: boolean;
    onRequestDelete: (target: { type: "provider"; id: string; name: string }) => void;
}

const getProviderIcon = (type: string) =>
    type === "voximplant" ? Phone : PhoneCall;

// A voice-only provider labelled "SMS" reads as a misconfiguration when it is working correctly.
const getProviderCapabilities = (type: string) =>
    type === "voximplant" ? "Voice, Video"
        : type === "callu-voice" ? "Voice"
        : "SMS";

type HeaderRow = { key: string; value: string };

const labelStyle = { fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" } as const;

/** Voximplant data-center nodes the account may live on (Web SDK ConnectionNode). */
const VOXIMPLANT_NODES = Array.from({ length: 12 }, (_, i) => `NODE_${i + 1}`);

/** Whether the selector names a trunk, as opposed to the empty "not set" entry. */
const isTrunkChosen = (value: string) => !!value && value !== "none";

const emptyFormData = {
    name: "",
    providerType: "voximplant" as string,
    voximplantAccountId: "",
    voximplantApiKey: "",
    voximplantNode: "",
    verimorUsername: "",
    verimorPassword: "",
    verimorSenderId: "",
    httpUrl: "",
    httpMethod: "POST",
    httpContentType: "json",
    httpSenderId: "",
    httpBodyTemplate: "",
    httpHeaders: [] as HeaderRow[],
    httpSuccessMode: "status2xx",
    httpSuccessField: "",
    httpSuccessValue: "",
    httpMessageIdPath: "",
    httpApiKey: "",
    httpUsername: "",
    httpPassword: "",
    sipTrunkId: "",
    voiceBaseUrl: "",
    voiceApiToken: "",
    voiceCallbackUrl: "",
    voiceName: "",
};

export function ProviderSection({
    providers,
    sipTrunks,
    createProviderMutation,
    updateProviderMutation,

    isSaving,
    onRequestDelete,
}: ProviderSectionProps) {
    const formId = useId();
    const navigate = useNavigate();

    const [isModalOpen, setIsModalOpen] = useState(false);
    const [editingProvider, setEditingProvider] = useState<CommunicationProviderDto | null>(null);
    const [showPassword, setShowPassword] = useState(false);


    const [formData, setFormData] = useState({ ...emptyFormData });
    const [testPhone, setTestPhone] = useState("");
    const [testingSms, setTestingSms] = useState(false);

    const activeProvider = providers.find((p) => p.isEnabled);

    const handleCreate = () => {
        setEditingProvider(null);
        setFormData({ ...emptyFormData });
        setTestPhone("");
        setIsModalOpen(true);
    };

    const handleEdit = (provider: CommunicationProviderDto) => {
        setEditingProvider(provider);
        const h = provider.httpSms;
        setFormData({
            ...emptyFormData,
            name: provider.name,
            providerType: provider.providerType,
            voximplantAccountId: provider.voximplantAccountId || "",
            voximplantApiKey: provider.voximplantApiKey || "",
            voximplantNode: provider.voximplantNode || "",
            verimorUsername: provider.verimorUsername || "",
            verimorPassword: provider.verimorPassword || "",
            verimorSenderId: provider.verimorSenderId || "",
            httpUrl: h?.url || "",
            httpMethod: h?.method || "POST",
            httpContentType: h?.contentType || "json",
            httpSenderId: h?.senderId || "",
            httpBodyTemplate: h?.bodyTemplate || "",
            httpHeaders: h?.headers ? Object.entries(h.headers).map(([key, value]) => ({ key, value })) : [],
            httpSuccessMode: h?.successMode || "status2xx",
            httpSuccessField: h?.successField || "",
            httpSuccessValue: h?.successValue || "",
            httpMessageIdPath: h?.messageIdPath || "",
            sipTrunkId: provider.sipTrunkId || "",
            voiceBaseUrl: provider.calluVoice?.baseUrl || "",
            // Never prefilled: the server reports only whether one is stored.
            voiceApiToken: "",
            voiceCallbackUrl: provider.calluVoice?.callbackUrl || "",
            voiceName: provider.calluVoice?.voice || "",
        });
        setTestPhone("");
        setIsModalOpen(true);
    };


    const handleToggleActive = (providerId: string) => {
        const provider = providers.find((p) => p.id === providerId);
        if (provider) {
            updateProviderMutation.mutate({
                id: providerId,
                name: provider.name,
                sipTrunkId: provider.sipTrunkId,
                priority: provider.priority,
                isEnabled: !provider.isEnabled,
            });
        }
    };

    const handleSave = async () => {
        try {
            const config: Record<string, unknown> = {};
            if (formData.providerType === "voximplant") {
                if (formData.voximplantAccountId) config.accountId = formData.voximplantAccountId;
                if (formData.voximplantApiKey) config.apiKey = formData.voximplantApiKey;
                if (formData.voximplantNode) config.node = formData.voximplantNode;

            } else if (formData.providerType === "verimor") {
                if (formData.verimorUsername) config.apiUsername = formData.verimorUsername;
                if (formData.verimorPassword) config.apiPassword = formData.verimorPassword;
                if (formData.verimorSenderId) config.senderId = formData.verimorSenderId;

            } else if (formData.providerType === "callu-voice") {
                config.baseUrl = formData.voiceBaseUrl.trim();
                config.callbackUrl = formData.voiceCallbackUrl.trim();
                if (formData.voiceName) config.voice = formData.voiceName.trim();
                // Left out when blank so editing without retyping it keeps the stored one.
                if (formData.voiceApiToken) config.apiToken = formData.voiceApiToken;

            } else if (formData.providerType === "http-sms") {
                config.url = formData.httpUrl.trim();
                config.method = formData.httpMethod;
                config.contentType = formData.httpContentType;
                if (formData.httpSenderId) config.senderId = formData.httpSenderId;
                if (formData.httpBodyTemplate) config.bodyTemplate = formData.httpBodyTemplate;
                const headers: Record<string, string> = {};
                for (const row of formData.httpHeaders) {
                    if (row.key.trim()) headers[row.key.trim()] = row.value;
                }
                if (Object.keys(headers).length > 0) config.headers = headers;
                config.successMode = formData.httpSuccessMode;
                if (formData.httpSuccessMode === "jsonField") {
                    config.successField = formData.httpSuccessField.trim();
                    config.successValue = formData.httpSuccessValue;
                }
                if (formData.httpMessageIdPath) config.messageIdPath = formData.httpMessageIdPath.trim();
                if (formData.httpApiKey) config.apiKey = formData.httpApiKey;
                if (formData.httpUsername) config.username = formData.httpUsername;
                if (formData.httpPassword) config.password = formData.httpPassword;
            }

            const sipTrunkId = isTrunkChosen(formData.sipTrunkId) ? formData.sipTrunkId : undefined;

            if (editingProvider) {
                await updateProviderMutation.mutateAsync({
                    id: editingProvider.id,
                    name: formData.name,
                    config,
                    sipTrunkId,
                    isEnabled: editingProvider.isEnabled,
                    priority: editingProvider.priority,
                });
                toast.success(t("providers.updated"));
            } else {
                await createProviderMutation.mutateAsync({
                    name: formData.name,
                    providerType: formData.providerType,
                    config,
                    sipTrunkId,
                    priority: 0,
                });
                toast.success(t("providers.created"));
            }
            setIsModalOpen(false);
        } catch (err) {
            toast.error(err instanceof Error ? err.message : t("providers.saveFailed"));
        }
    };

    const handleTestSms = async () => {
        if (!editingProvider || !testPhone.trim()) return;
        setTestingSms(true);
        try {
            const res = await communicationsApi.testSms(editingProvider.id, { to: testPhone.trim() });
            const result = res.data;
            if (result?.success) {
                toast.success(t("providers.testSmsSent") + (result.messageId ? ` (${result.messageId})` : ""));
            } else {
                toast.error(result?.errorMessage || t("providers.testSmsFailed"));
            }
        } catch (err) {
            toast.error(err instanceof Error ? err.message : t("providers.testSmsFailed"));
        } finally {
            setTestingSms(false);
        }
    };

    return (
        <>
            {/* Only the warning earns a banner: "nothing will page anyone" is worth interrupting for,
                while repeating the active provider says what the list below already shows. */}
            {!activeProvider && (
                <div className="p-4 rounded-lg bg-warning-500/10 border-2 border-warning-500/20 flex items-start gap-3">
                    <AlertCircle className="w-5 h-5 text-warning-500 flex-shrink-0 mt-0.5" />
                    <div>
                        <p style={{ fontSize: "0.9375rem", fontWeight: 600 }}>{t("providers.noActiveProvider")}</p>
                        <p style={{ fontSize: "0.8125rem", color: "#94A3B8", marginTop: "0.25rem" }}>
                            {t("providers.noActiveProviderDesc")}
                        </p>
                    </div>
                </div>
            )}

            <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
                <div className="flex items-start gap-4 mb-6">
                    <div className="w-10 h-10 rounded-lg bg-brand-500/10 flex items-center justify-center flex-shrink-0">
                        <Radio className="w-5 h-5 text-brand-500" />
                    </div>
                    <div className="flex-1">
                        <h3 style={{ fontSize: "1.125rem", fontWeight: 600 }}>{t("providers.title")}</h3>
                        <p style={{ fontSize: "0.875rem", color: "#94A3B8", marginTop: "0.25rem" }}>
                            {t("providers.description")}
                        </p>
                    </div>
                    <Button onClick={handleCreate} className="bg-brand-500 hover:bg-brand-600 text-white">
                        <Plus className="w-4 h-4 mr-2" />
                        {t("providers.addProvider")}
                    </Button>
                </div>

                {providers.length > 0 ? (
                    <div className="space-y-4">
                        {providers.map((provider) => {
                            const ProviderIcon = getProviderIcon(provider.providerType);
                            return (
                                <div
                                    key={provider.id}
                                    className={`p-3 rounded-lg border transition-all ${provider.isEnabled
                                        ? "border-brand-500 bg-brand-500/5"
                                        : "border-border bg-surface-light/20 hover:border-border-light"
                                        }`}
                                >
                                    <div className="flex items-start gap-4">
                                        <div
                                            className="w-12 h-12 rounded-lg flex items-center justify-center flex-shrink-0"
                                            style={{
                                                backgroundColor:
                                                    provider.providerType === "voximplant" ? "#3E7BFA20" : "#FB923C20",
                                            }}
                                        >
                                            <ProviderIcon
                                                className="w-6 h-6"
                                                style={{
                                                    color: provider.providerType === "voximplant" ? "#3E7BFA" : "#FB923C",
                                                }}
                                            />
                                        </div>

                                        <div className="flex-1 min-w-0">
                                            <div className="flex items-center gap-2 mb-2">
                                                <p style={{ fontSize: "1.0625rem", fontWeight: 600 }}>{provider.name}</p>
                                                <Badge
                                                    className={`border text-xs ${provider.isEnabled
                                                        ? "bg-success-500/10 text-success-500 border-success-500/20"
                                                        : "bg-muted/10 text-muted-foreground border-muted/20"
                                                        }`}
                                                >
                                                    {provider.isEnabled ? (
                                                        <div className="flex items-center gap-1">
                                                            <div className="w-1.5 h-1.5 bg-success-500 rounded-full animate-pulse" />
                                                            {t("common.active")}
                                                        </div>
                                                    ) : (
                                                        t("common.inactive")
                                                    )}
                                                </Badge>
                                            </div>
                                            <p style={{ fontSize: "0.8125rem", color: "#94A3B8", marginBottom: "0.75rem" }}>
                                                {provider.providerType.toUpperCase()} •{" "}
                                                {getProviderCapabilities(provider.providerType)}
                                            </p>

                                            {provider.sipTrunkId && (
                                                <div className="flex items-center gap-1.5 mb-2">
                                                    <span style={{ fontSize: "0.75rem", color: "#94A3B8" }}>
                                                        SIP: {sipTrunks.find((s) => s.id === provider.sipTrunkId)?.name || "Unknown"}
                                                    </span>
                                                </div>
                                            )}

                                            {provider.lastTestedAt && (
                                                <div className="flex items-center gap-1.5">
                                                    {provider.lastTestResult === "Success" ? (
                                                        <CheckCircle className="w-3 h-3 text-success-500" />
                                                    ) : (
                                                        <AlertCircle className="w-3 h-3 text-error-500" />
                                                    )}
                                                    <span style={{ fontSize: "0.75rem", color: "#94A3B8" }}>
                                                        Last tested:{" "}
                                                        {new Date(provider.lastTestedAt).toLocaleDateString(dateLocale(), {
                                                            month: "short",
                                                            day: "numeric",
                                                            hour: "2-digit",
                                                            minute: "2-digit",
                                                        })}
                                                    </span>
                                                </div>
                                            )}
                                        </div>

                                        <div className="flex flex-wrap gap-2">
                                            <Button
                                                size="sm"
                                                onClick={() => handleToggleActive(provider.id)}
                                                className={
                                                    provider.isEnabled
                                                        ? "bg-input-background"
                                                        : "bg-brand-500 hover:bg-brand-600 text-white"
                                                }
                                                variant={provider.isEnabled ? "outline" : "default"}
                                            >
                                                {provider.isEnabled ? t("providers.deactivate") : t("providers.activate")}
                                            </Button>
                                            {provider.providerType === "voximplant" && (
                                                <Button
                                                    size="sm"
                                                    variant="outline"
                                                    onClick={() =>
                                                        navigate(`/settings/communications/voximplant/${provider.id}`)
                                                    }
                                                    className="bg-input-background"
                                                >
                                                    <Settings className="w-4 h-4 mr-2" />
                                                    {t("common.manage")}
                                                </Button>
                                            )}

                                            <Button
                                                size="sm"
                                                variant="ghost"
                                                title={t("common.edit")}
                                                onClick={() => handleEdit(provider)}
                                            >
                                                <Edit className="w-4 h-4" />
                                            </Button>
                                            <Button
                                                size="sm"
                                                variant="ghost"
                                                onClick={() =>
                                                    onRequestDelete({ type: "provider", id: provider.id, name: provider.name })
                                                }
                                                className="text-error-500 hover:bg-error-500/10"
                                            >
                                                <Trash2 className="w-4 h-4" />
                                            </Button>
                                        </div>
                                    </div>
                                </div>
                            );
                        })}
                    </div>
                ) : (
                    <div className="text-center py-12">
                        <Radio className="w-12 h-12 text-muted-foreground mx-auto mb-3 opacity-50" />
                        <p style={{ fontSize: "0.9375rem", fontWeight: 600, marginBottom: "0.5rem" }}>
                            {t("providers.noProviders")}
                        </p>
                        <p style={{ fontSize: "0.8125rem", color: "#94A3B8", marginBottom: "1.5rem" }}>
                            {t("providers.noProvidersDesc")}
                        </p>
                        <Button onClick={handleCreate} className="bg-brand-500 hover:bg-brand-600">
                            <Plus className="w-4 h-4 mr-2" />
                            {t("providers.addProvider")}
                        </Button>
                    </div>
                )}
            </Card>

            <Dialog open={isModalOpen} onOpenChange={setIsModalOpen}>
                <DialogContent className="bg-card border-border sm:max-w-[700px] max-h-[90vh] overflow-y-auto">
                    <DialogHeader>
                        <DialogTitle style={{ fontSize: "1.5rem", fontWeight: 600 }}>
                            {editingProvider ? t("providers.editProvider") : t("providers.addProvider")}
                        </DialogTitle>
                        <DialogDescription style={{ fontSize: "0.875rem", color: "#94A3B8" }}>
                            {t("providers.configureSettings")}
                        </DialogDescription>
                    </DialogHeader>

                    <div className="space-y-5 py-4">
                        <div>
                            <label htmlFor={`${formId}-name`} style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                                {t("common.name")} <span className="text-error-500">*</span>
                            </label>
                            <Input
                                id={`${formId}-name`}
                                placeholder={t("providers.displayNameExamplePlaceholder")}
                                value={formData.name}
                                onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                                className="bg-input-background"
                            />
                        </div>

                        <div>
                            <label htmlFor={`${formId}-provider-type`} style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                                {t("providers.providerType")}
                            </label>
                            <Select
                                value={formData.providerType}
                                onValueChange={(value: string) => setFormData({ ...formData, providerType: value })}
                                disabled={!!editingProvider}
                            >
                                <SelectTrigger id={`${formId}-provider-type`} className="bg-input-background">
                                    <SelectValue />
                                </SelectTrigger>
                                <SelectContent>
                                    <SelectItem value="voximplant">Voximplant</SelectItem>
                                    <SelectItem value="callu-voice">{t("providers.calluVoiceType")}</SelectItem>
                                    <SelectItem value="verimor">Verimor (SMS)</SelectItem>
                                    <SelectItem value="http-sms">{t("providers.httpSmsType")}</SelectItem>
                                </SelectContent>
                            </Select>
                        </div>

                        {formData.providerType === "voximplant" && (
                            <>
                                <div>
                                    <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                                        {t("providers.accountId")}
                                    </label>
                                    <Input
                                        placeholder={t("providers.accountIdExamplePlaceholder")}
                                        value={formData.voximplantAccountId}
                                        onChange={(e) => setFormData({ ...formData, voximplantAccountId: e.target.value })}
                                        className="bg-input-background"
                                    />
                                </div>

                                <div>
                                    <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                                        {t("providers.apiKey")}
                                    </label>
                                    <div className="relative">
                                        <Input
                                            type={showPassword ? "text" : "password"}
                                            placeholder={t("providers.enterApiKey")}
                                            value={formData.voximplantApiKey}
                                            onChange={(e) => setFormData({ ...formData, voximplantApiKey: e.target.value })}
                                            className="bg-input-background pr-10"
                                        />
                                        <button
                                            type="button"
                                            onClick={() => setShowPassword(!showPassword)}
                                            className="absolute right-3 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground"
                                        >
                                            {showPassword ? <EyeOff className="w-4 h-4" /> : <Eye className="w-4 h-4" />}
                                        </button>
                                    </div>
                                </div>

                                <div>
                                    <label htmlFor={`${formId}-voximplant-node`} style={labelStyle}>
                                        {t("providers.voximplantNode")}
                                    </label>
                                    <Select
                                        value={formData.voximplantNode}
                                        onValueChange={(value) => setFormData({ ...formData, voximplantNode: value })}
                                    >
                                        <SelectTrigger id={`${formId}-voximplant-node`} className="bg-input-background">
                                            <SelectValue placeholder={t("providers.voximplantNodePlaceholder")} />
                                        </SelectTrigger>
                                        <SelectContent>
                                            {VOXIMPLANT_NODES.map((node) => (
                                                <SelectItem key={node} value={node}>
                                                    {node}
                                                </SelectItem>
                                            ))}
                                        </SelectContent>
                                    </Select>
                                    <p style={{ fontSize: "0.75rem", color: "#94A3B8", marginTop: "0.375rem" }}>
                                        {t("providers.voximplantNodeHelp")}
                                    </p>
                                </div>

                                <div className="p-4 rounded-lg bg-brand-500/5 border border-brand-500/20 flex gap-3">
                                    <Info className="w-5 h-5 text-brand-500 flex-shrink-0 mt-0.5" />
                                    <p style={{ fontSize: "0.8125rem", color: "#94A3B8" }}>
                                        Applications, rules and other settings can be configured from the{" "}
                                        <strong>Manage</strong> page after creating the provider.
                                    </p>
                                </div>
                            </>
                        )}

                        {formData.providerType === "verimor" && (
                            <>
                                <div>
                                    <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                                        API Username
                                    </label>
                                    <Input
                                        placeholder={t("providers.usernameExamplePlaceholder")}
                                        value={formData.verimorUsername}
                                        onChange={(e) => setFormData({ ...formData, verimorUsername: e.target.value })}
                                        className="bg-input-background"
                                    />
                                </div>

                                <div>
                                    <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                                        API Password
                                    </label>
                                    <Input
                                        type="password"
                                        value={formData.verimorPassword}
                                        onChange={(e) => setFormData({ ...formData, verimorPassword: e.target.value })}
                                        className="bg-input-background"
                                    />
                                </div>

                                <div>
                                    <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                                        Sender ID
                                    </label>
                                    <Input
                                        placeholder={t("providers.applicationNamePlaceholder")}
                                        value={formData.verimorSenderId}
                                        onChange={(e) => setFormData({ ...formData, verimorSenderId: e.target.value })}
                                        className="bg-input-background"
                                    />
                                </div>
                            </>
                        )}

                        {formData.providerType === "http-sms" && (
                            <>
                                <div>
                                    <label style={labelStyle}>API URL <span className="text-error-500">*</span></label>
                                    <Input
                                        placeholder="https://api.example.com/sms/send"
                                        value={formData.httpUrl}
                                        onChange={(e) => setFormData({ ...formData, httpUrl: e.target.value })}
                                        className="bg-input-background"
                                    />
                                </div>

                                <div className="grid grid-cols-2 gap-3">
                                    <div>
                                        <label htmlFor={`${formId}-http-method`} style={labelStyle}>Method</label>
                                        <Select value={formData.httpMethod} onValueChange={(v) => setFormData({ ...formData, httpMethod: v })}>
                                            <SelectTrigger id={`${formId}-http-method`} className="bg-input-background"><SelectValue /></SelectTrigger>
                                            <SelectContent>
                                                <SelectItem value="POST">POST</SelectItem>
                                                <SelectItem value="GET">GET</SelectItem>
                                            </SelectContent>
                                        </Select>
                                    </div>
                                    <div>
                                        <label htmlFor={`${formId}-http-content-type`} style={labelStyle}>Body format</label>
                                        <Select value={formData.httpContentType} onValueChange={(v) => setFormData({ ...formData, httpContentType: v })}>
                                            <SelectTrigger id={`${formId}-http-content-type`} className="bg-input-background"><SelectValue /></SelectTrigger>
                                            <SelectContent>
                                                <SelectItem value="json">JSON</SelectItem>
                                                <SelectItem value="form">Form (urlencoded)</SelectItem>
                                            </SelectContent>
                                        </Select>
                                    </div>
                                </div>

                                <div>
                                    <label style={labelStyle}>Headers</label>
                                    <div className="space-y-2">
                                        {formData.httpHeaders.map((row, i) => (
                                            <div key={i} className="flex gap-2">
                                                <Input
                                                    placeholder="Header-Name"
                                                    value={row.key}
                                                    onChange={(e) => {
                                                        const h = [...formData.httpHeaders];
                                                        h[i] = { ...h[i], key: e.target.value };
                                                        setFormData({ ...formData, httpHeaders: h });
                                                    }}
                                                    className="bg-input-background"
                                                />
                                                <Input
                                                    placeholder="Value (e.g. Bearer {apiKey})"
                                                    value={row.value}
                                                    onChange={(e) => {
                                                        const h = [...formData.httpHeaders];
                                                        h[i] = { ...h[i], value: e.target.value };
                                                        setFormData({ ...formData, httpHeaders: h });
                                                    }}
                                                    className="bg-input-background"
                                                />
                                                <Button variant="outline" size="sm" onClick={() => setFormData({ ...formData, httpHeaders: formData.httpHeaders.filter((_, j) => j !== i) })}>
                                                    <Trash2 className="w-3 h-3" />
                                                </Button>
                                            </div>
                                        ))}
                                        <Button variant="outline" size="sm" onClick={() => setFormData({ ...formData, httpHeaders: [...formData.httpHeaders, { key: "", value: "" }] })}>
                                            <Plus className="w-3 h-3 mr-1" /> Add header
                                        </Button>
                                    </div>
                                </div>

                                {formData.httpMethod === "POST" && (
                                    <div>
                                        <label style={labelStyle}>Body template</label>
                                        <textarea
                                            className="w-full p-3 rounded-lg bg-input-background border border-border text-sm min-h-[110px] resize-y font-mono"
                                            placeholder={'{"phone":"{to}","text":"{message}","from":"{sender}"}'}
                                            value={formData.httpBodyTemplate}
                                            onChange={(e) => setFormData({ ...formData, httpBodyTemplate: e.target.value })}
                                        />
                                    </div>
                                )}

                                <div>
                                    <label style={labelStyle}>Sender ID</label>
                                    <Input value={formData.httpSenderId} onChange={(e) => setFormData({ ...formData, httpSenderId: e.target.value })} className="bg-input-background" placeholder="ACME" />
                                </div>

                                <div className="grid grid-cols-3 gap-3">
                                    <div>
                                        <label style={labelStyle}>API Key</label>
                                        <Input type="password" value={formData.httpApiKey} onChange={(e) => setFormData({ ...formData, httpApiKey: e.target.value })} className="bg-input-background" placeholder={editingProvider?.httpSms?.hasApiKey ? "••••••••" : ""} />
                                    </div>
                                    <div>
                                        <label style={labelStyle}>Username</label>
                                        <Input value={formData.httpUsername} onChange={(e) => setFormData({ ...formData, httpUsername: e.target.value })} className="bg-input-background" placeholder={editingProvider?.httpSms?.hasUsername ? "••••••••" : ""} />
                                    </div>
                                    <div>
                                        <label style={labelStyle}>Password</label>
                                        <Input type="password" value={formData.httpPassword} onChange={(e) => setFormData({ ...formData, httpPassword: e.target.value })} className="bg-input-background" placeholder={editingProvider?.httpSms?.hasPassword ? "••••••••" : ""} />
                                    </div>
                                </div>

                                <div className="grid grid-cols-2 gap-3">
                                    <div>
                                        <label htmlFor={`${formId}-http-success-mode`} style={labelStyle}>Success when</label>
                                        <Select value={formData.httpSuccessMode} onValueChange={(v) => setFormData({ ...formData, httpSuccessMode: v })}>
                                            <SelectTrigger id={`${formId}-http-success-mode`} className="bg-input-background"><SelectValue /></SelectTrigger>
                                            <SelectContent>
                                                <SelectItem value="status2xx">HTTP 2xx</SelectItem>
                                                <SelectItem value="jsonField">JSON field equals…</SelectItem>
                                            </SelectContent>
                                        </Select>
                                    </div>
                                    {formData.httpSuccessMode === "jsonField" && (
                                        <div className="grid grid-cols-2 gap-2">
                                            <Input value={formData.httpSuccessField} onChange={(e) => setFormData({ ...formData, httpSuccessField: e.target.value })} className="bg-input-background" placeholder="status (dotted path)" />
                                            <Input value={formData.httpSuccessValue} onChange={(e) => setFormData({ ...formData, httpSuccessValue: e.target.value })} className="bg-input-background" placeholder="ok" />
                                        </div>
                                    )}
                                </div>

                                <div>
                                    <label style={labelStyle}>Message ID path (optional)</label>
                                    <Input value={formData.httpMessageIdPath} onChange={(e) => setFormData({ ...formData, httpMessageIdPath: e.target.value })} className="bg-input-background" placeholder="data.id" />
                                </div>

                                <div className="p-4 rounded-lg bg-brand-500/5 border border-brand-500/20 flex gap-3">
                                    <Info className="w-5 h-5 text-brand-500 flex-shrink-0 mt-0.5" />
                                    <p style={{ fontSize: "0.8125rem", color: "#94A3B8" }}>
                                        Placeholders for URL, headers and body: <code>{"{to}"}</code> <code>{"{message}"}</code> <code>{"{sender}"}</code> <code>{"{apiKey}"}</code> <code>{"{username}"}</code> <code>{"{password}"}</code>. Values are escaped automatically for the target format (JSON / URL).
                                    </p>
                                </div>
                            </>
                        )}

                        {formData.providerType === "callu-voice" && (
                            <>
                                <div>
                                    <label htmlFor={`${formId}-voice-base`} style={labelStyle}>
                                        {t("providers.voiceBaseUrl")}
                                    </label>
                                    <Input
                                        id={`${formId}-voice-base`}
                                        placeholder="http://callu-voice:8090"
                                        value={formData.voiceBaseUrl}
                                        onChange={(e) => setFormData({ ...formData, voiceBaseUrl: e.target.value })}
                                        className="bg-input-background"
                                    />
                                    <p className="mt-1.5 text-[13px] text-dim">{t("providers.voiceBaseUrlHint")}</p>
                                </div>

                                <div>
                                    <label htmlFor={`${formId}-voice-token`} style={labelStyle}>
                                        {t("providers.voiceApiToken")}
                                    </label>
                                    <Input
                                        id={`${formId}-voice-token`}
                                        type="password"
                                        placeholder={editingProvider && editingProvider.calluVoice?.hasApiToken
                                            ? t("providers.voiceTokenStored")
                                            : ""}
                                        value={formData.voiceApiToken}
                                        onChange={(e) => setFormData({ ...formData, voiceApiToken: e.target.value })}
                                        className="bg-input-background"
                                    />
                                    <p className="mt-1.5 text-[13px] text-dim">{t("providers.voiceApiTokenHint")}</p>
                                </div>

                                <div>
                                    <label htmlFor={`${formId}-voice-callback`} style={labelStyle}>
                                        {t("providers.voiceCallbackUrl")}
                                    </label>
                                    <Input
                                        id={`${formId}-voice-callback`}
                                        placeholder="http://callu-api:5095"
                                        value={formData.voiceCallbackUrl}
                                        onChange={(e) => setFormData({ ...formData, voiceCallbackUrl: e.target.value })}
                                        className="bg-input-background"
                                    />
                                    <p className="mt-1.5 text-[13px] text-dim">{t("providers.voiceCallbackUrlHint")}</p>
                                </div>

                                <div>
                                    <label htmlFor={`${formId}-voice-name`} style={labelStyle}>
                                        {t("providers.voiceName")}
                                    </label>
                                    <Input
                                        id={`${formId}-voice-name`}
                                        value={formData.voiceName}
                                        onChange={(e) => setFormData({ ...formData, voiceName: e.target.value })}
                                        className="bg-input-background"
                                    />
                                    <p className="mt-1.5 text-[13px] text-dim">{t("providers.voiceNameHint")}</p>
                                </div>

                                <div>
                                    <label htmlFor={`${formId}-voice-trunk`} style={labelStyle}>
                                        {t("sipTrunk.title")}
                                    </label>
                                    <Select
                                        value={formData.sipTrunkId || "none"}
                                        onValueChange={(value) => setFormData({ ...formData, sipTrunkId: value })}
                                    >
                                        <SelectTrigger id={`${formId}-voice-trunk`} className="bg-input-background">
                                            <SelectValue placeholder={t("sipTrunk.selectSipTrunk")} />
                                        </SelectTrigger>
                                        <SelectContent>
                                            <SelectItem value="none">{t("providers.voiceTrunkNone")}</SelectItem>
                                            {sipTrunks.map((trunk) => (
                                                <SelectItem key={trunk.id} value={trunk.id}>
                                                    {trunk.name}
                                                </SelectItem>
                                            ))}
                                        </SelectContent>
                                    </Select>
                                    <p className="mt-1.5 text-[13px] text-dim">{t("providers.voiceTrunkHint")}</p>
                                    {/* Saving here is the only thing that removes a carrier, so it says so before you do. */}
                                    {!!editingProvider?.sipTrunkId && !isTrunkChosen(formData.sipTrunkId) && (
                                        <p className="mt-1.5 text-[13px] text-warning-500">
                                            {t("providers.voiceTrunkRemovalWarning")}
                                        </p>
                                    )}
                                </div>
                            </>
                        )}

                        {formData.providerType === "voximplant" && (
                            <div>
                                <label htmlFor={`${formId}-sip-trunk`} style={labelStyle}>
                                    {t("sipTrunk.title")}
                                </label>
                                <Select
                                    value={formData.sipTrunkId}
                                    onValueChange={(value) => setFormData({ ...formData, sipTrunkId: value })}
                                >
                                    <SelectTrigger id={`${formId}-sip-trunk`} className="bg-input-background">
                                        <SelectValue placeholder={t("sipTrunk.selectSipTrunk")} />
                                    </SelectTrigger>
                                    <SelectContent>
                                        <SelectItem value="none">None</SelectItem>
                                        {sipTrunks.map((trunk) => (
                                            <SelectItem key={trunk.id} value={trunk.id}>
                                                {trunk.name}
                                            </SelectItem>
                                        ))}
                                    </SelectContent>
                                </Select>
                            </div>
                        )}

                        {editingProvider && formData.providerType !== "voximplant" && formData.providerType !== "callu-voice" && (
                            <div className="p-4 rounded-lg bg-white/5 border border-border space-y-2">
                                <label style={labelStyle}>{t("providers.sendTestSms")}</label>
                                <div className="flex gap-2">
                                    <Input placeholder="+905551112233" value={testPhone} onChange={(e) => setTestPhone(e.target.value)} className="bg-input-background" />
                                    <Button variant="outline" onClick={handleTestSms} disabled={testingSms || !testPhone.trim()}>
                                        {testingSms ? <div className="w-4 h-4 border-2 border-white/30 border-t-white rounded-full animate-spin" /> : <Send className="w-4 h-4" />}
                                    </Button>
                                </div>
                                <p style={{ fontSize: "0.75rem", color: "#94A3B8" }}>{t("providers.sendTestSmsHint")}</p>
                            </div>
                        )}
                    </div>

                    <DialogFooter>
                        <Button variant="outline" onClick={() => setIsModalOpen(false)} className="bg-input-background">
                            {t("common.cancel")}
                        </Button>
                        <Button
                            disabled={!formData.name || isSaving}
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
                                    {editingProvider ? t("providers.updateProvider") : t("providers.createProvider")}
                                </>
                            )}
                        </Button>
                    </DialogFooter>
                </DialogContent>
            </Dialog>
        </>
    );
}
