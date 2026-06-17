import { useEffect, useMemo, useState } from "react";
import { Alert, AlertDescription, AlertTitle } from "@/shared/components/ui/alert";
import { Card } from "@/shared/components/ui/card";
import { Button } from "@/shared/components/ui/button";
import { Badge } from "@/shared/components/ui/badge";
import { Input } from "@/shared/components/ui/input";
import { Switch } from "@/shared/components/ui/switch";
import { Checkbox } from "@/shared/components/ui/checkbox";
import { Separator } from "@/shared/components/ui/separator";
import { ScrollArea } from "@/shared/components/ui/scroll-area";
import {
    Dialog,
    DialogContent,
    DialogHeader,
    DialogTitle,
    DialogFooter,
    DialogDescription,
} from "@/shared/components/ui/dialog";
import {
    Select,
    SelectContent,
    SelectItem,
    SelectTrigger,
    SelectValue,
} from "@/shared/components/ui/select";
import {
    Loader2,
    Plus,
    Pencil,
    Trash2,
    Send,
    Bell,
    Hash,
    ExternalLink,
    Filter,
    Radio,
    Copy,
    AlertCircle,
} from "lucide-react";
import { LoadingState } from "@/shared/components/loading-state";
import { ErrorState } from "@/shared/components/error-state";
import { onLocaleChange, t } from "@/shared/locales/i18n";
import { getErrorMessage, isApiError } from "@/shared/api/api-errors";
import { toast } from "@/shared/utils/toast";
import {
    useNotificationChannels,
    useCreateNotificationChannel,
    useUpdateNotificationChannel,
    useToggleNotificationChannel,
    useTestNotificationChannel,
    useDeleteNotificationChannel,
    useChannelTypes,
    useSeverityOptions,
} from "../hooks/use-notification-channels";
import { useServices } from "@/features/services/hooks/use-services";
import type {
    NotificationChannelDto,
    CreateNotificationChannelRequest,
    ChannelTypeField,
} from "../types/notification-channels.types";

const TYPE_STYLE: Record<string, string> = {
    Slack: "bg-green-500/10 text-green-400 border-green-500/20",
    MicrosoftTeams: "bg-purple-500/10 text-purple-400 border-purple-500/20",
    Email: "bg-blue-500/10 text-blue-400 border-blue-500/20",
    Webhook: "bg-orange-500/10 text-orange-400 border-orange-500/20",
};

const SEVERITY_ALL = "__all__";

function linesFromApiError(error: unknown): string[] {
    if (!isApiError(error)) {
        return [getErrorMessage(error)];
    }
    const lines: string[] = [];
    const msg = error.message?.trim();
    if (msg) {
        lines.push(msg);
    }
    if (error.errors) {
        for (const [key, msgs] of Object.entries(error.errors)) {
            for (const m of msgs) {
                lines.push(key && key !== "General" ? `${key}: ${m}` : m);
            }
        }
    }
    return lines.length > 0 ? lines : [getErrorMessage(error)];
}

function ConfigFieldInput({
    field,
    value,
    onChange,
}: {
    field: ChannelTypeField;
    value: string;
    onChange: (v: string) => void;
}) {
    if (field.input === "select" && field.options?.length) {
        return (
            <Select value={value || field.options[0]!.value} onValueChange={onChange}>
                <SelectTrigger className="mt-1">
                    <SelectValue placeholder={field.placeholder} />
                </SelectTrigger>
                <SelectContent>
                    {field.options.map((o) => (
                        <SelectItem key={o.value} value={o.value}>
                            {o.label}
                        </SelectItem>
                    ))}
                </SelectContent>
            </Select>
        );
    }

    const inputType =
        field.input === "password"
            ? "password"
            : field.input === "url" || field.input === "email"
              ? "url"
              : "text";

    return (
        <Input
            className="mt-1"
            type={field.input === "email" ? "email" : inputType}
            autoComplete={field.input === "password" ? "new-password" : undefined}
            placeholder={field.placeholder ?? field.key}
            value={value}
            onChange={(e) => onChange(e.target.value)}
        />
    );
}

export function NotificationChannelsSettings() {
    const { data: channels, isLoading, error } = useNotificationChannels();
    const { data: channelTypes = [] } = useChannelTypes();
    const { data: severityOptions = [] } = useSeverityOptions();
    const { data: services = [] } = useServices();
    const createMutation = useCreateNotificationChannel();
    const updateMutation = useUpdateNotificationChannel();
    const toggleMutation = useToggleNotificationChannel();
    const testMutation = useTestNotificationChannel();
    const deleteMutation = useDeleteNotificationChannel();

    const [isEditorOpen, setIsEditorOpen] = useState(false);
    const [editing, setEditing] = useState<NotificationChannelDto | null>(null);
    const [formApiLines, setFormApiLines] = useState<string[] | null>(null);
    const [form, setForm] = useState<CreateNotificationChannelRequest>({
        name: "",
        channelType: "Slack",
        configuration: {},
        serviceFilter: [],
        notifyOnIncidentCreated: true,
        notifyOnIncidentAcknowledged: false,
        notifyOnIncidentResolved: false,
    });

    const [i18nTick, setI18nTick] = useState(0);
    useEffect(() => onLocaleChange(() => setI18nTick((n) => n + 1)), []);

    const webhookSampleJson = useMemo(
        () =>
            `{
  "source": "CalluApp",
  "timestamp": "2026-04-03T12:00:00.000Z",
  "event": "incident.created",
  "incidentId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "title": ${JSON.stringify(t("notificationChannels.webhookSampleTitle"))},
  "severity": "Critical",
  "serviceId": "3fa85f64-5717-4562-b3fc-2c963f66afa7",
  "message": ${JSON.stringify(t("notificationChannels.webhookSampleMessage"))}
}`,
        // eslint-disable-next-line react-hooks/exhaustive-deps
        [i18nTick],
    );

    const serviceNameById = useMemo(() => {
        const m = new Map<string, string>();
        for (const s of services) m.set(s.id, s.name);
        return m;
    }, [services]);

    const openCreate = () => {
        setFormApiLines(null);
        setEditing(null);
        setForm({
            name: "",
            channelType: "Slack",
            configuration: {},
            serviceFilter: [],
            notifyOnIncidentCreated: true,
            notifyOnIncidentAcknowledged: false,
            notifyOnIncidentResolved: false,
        });
        setIsEditorOpen(true);
    };

    const openEdit = (ch: NotificationChannelDto) => {
        setFormApiLines(null);
        setEditing(ch);
        setForm({
            name: ch.name,
            channelType: ch.channelType,
            configuration: { ...ch.configuration },
            minimumSeverity: ch.minimumSeverity,
            serviceFilter: [...ch.serviceFilter],
            notifyOnIncidentCreated: ch.notifyOnIncidentCreated ?? true,
            notifyOnIncidentAcknowledged: ch.notifyOnIncidentAcknowledged ?? false,
            notifyOnIncidentResolved: ch.notifyOnIncidentResolved ?? false,
        });
        setIsEditorOpen(true);
    };

    const handleSave = async () => {
        setFormApiLines(null);
        try {
            if (editing) {
                await updateMutation.mutateAsync({
                    id: editing.id,
                    data: {
                        name: form.name,
                        configuration: form.configuration,
                        isEnabled: editing.isEnabled,
                        minimumSeverity: form.minimumSeverity,
                        serviceFilter: form.serviceFilter,
                        notifyOnIncidentCreated: form.notifyOnIncidentCreated,
                        notifyOnIncidentAcknowledged: form.notifyOnIncidentAcknowledged,
                        notifyOnIncidentResolved: form.notifyOnIncidentResolved,
                    },
                });
            } else {
                await createMutation.mutateAsync(form);
            }
            setIsEditorOpen(false);
        } catch (e) {
            setFormApiLines(linesFromApiError(e));
        }
    };

    const selectedType = channelTypes.find((x) => x.value === form.channelType);
    const typeFields = selectedType?.fields ?? [];

    const requiredConfigSatisfied = typeFields.every(
        (f) => !f.required || Boolean(form.configuration[f.key]?.trim()),
    );

    const lifecycleAny =
        form.notifyOnIncidentCreated ||
        form.notifyOnIncidentAcknowledged ||
        form.notifyOnIncidentResolved;

    const saveBlockedReasons = useMemo(() => {
        const r: string[] = [];
        if (!form.name.trim()) {
            r.push(t("notificationChannels.saveBlockedName"));
        }
        if (!requiredConfigSatisfied) {
            r.push(t("notificationChannels.saveBlockedConfig"));
        }
        if (!lifecycleAny) {
            r.push(t("notificationChannels.saveBlockedLifecycle"));
        }
        return r;
    }, [form.name, requiredConfigSatisfied, lifecycleAny]);

    const toggleServiceFilter = (serviceId: string) => {
        setForm((f) => {
            const next = new Set(f.serviceFilter);
            if (next.has(serviceId)) next.delete(serviceId);
            else next.add(serviceId);
            return { ...f, serviceFilter: Array.from(next) };
        });
    };

    const severitySelectValue = form.minimumSeverity ? form.minimumSeverity : SEVERITY_ALL;

    if (isLoading) {
        return <LoadingState message={t("settings.notifications.loading")} />;
    }

    if (error) {
        return <ErrorState title={t("settings.notifications.loadFailed")} message={error.message} />;
    }

    return (
        <div className="space-y-6">
            <Card className="p-5 bg-gradient-to-br from-brand-500/10 via-card/90 to-card border-border">
                <div className="flex gap-3">
                    <div className="rounded-lg bg-brand-500/15 p-2.5 h-fit">
                        <Radio className="w-5 h-5 text-brand-400" aria-hidden />
                    </div>
                    <div className="space-y-1 min-w-0">
                        <h3 className="text-sm font-semibold text-foreground">{t("notificationChannels.railTitle")}</h3>
                        <p className="text-sm text-muted-foreground leading-relaxed">{t("notificationChannels.railBody")}</p>
                        <ul className="text-xs text-muted-foreground list-disc pl-4 space-y-0.5 pt-1">
                            <li>{t("notificationChannels.railBullet1")}</li>
                            <li>{t("notificationChannels.railBullet2")}</li>
                            <li>{t("notificationChannels.railBullet3")}</li>
                        </ul>
                    </div>
                </div>
            </Card>

            <div className="flex items-center justify-between gap-4">
                <div>
                    <h2 className="text-xl font-semibold">{t("settings.notifications.title")}</h2>
                    <p className="text-sm text-muted-foreground mt-1">{t("notificationChannels.subtitle")}</p>
                </div>
                <Button size="sm" onClick={openCreate}>
                    <Plus className="w-4 h-4 mr-2" /> {t("settings.notifications.add")}
                </Button>
            </div>

            {!channels || channels.length === 0 ? (
                <Card className="p-10 bg-card/80 backdrop-blur-sm border-border text-center">
                    <Bell className="w-10 h-10 mx-auto mb-3 text-muted-foreground" />
                    <p className="font-semibold">{t("settings.notifications.noChannels")}</p>
                    <p className="text-sm text-muted-foreground mt-1">{t("notificationChannels.emptyDesc")}</p>
                </Card>
            ) : (
                <div className="space-y-3">
                    {channels.map((ch) => {
                        const typeInfo = channelTypes.find((x) => x.value === ch.channelType);
                        return (
                            <Card
                                key={ch.id}
                                className="p-4 bg-card/80 backdrop-blur-sm border-border hover:border-brand-500/30 transition-colors"
                            >
                                <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                                    <div className="flex items-start gap-3 min-w-0">
                                        <span className="text-2xl shrink-0">{typeInfo?.icon ?? "🔔"}</span>
                                        <div className="min-w-0 space-y-2">
                                            <div className="flex flex-wrap items-center gap-2">
                                                <span className="font-semibold truncate">{ch.name}</span>
                                                <Badge className={`border text-xs ${TYPE_STYLE[ch.channelType] ?? ""}`}>
                                                    {typeInfo?.label ?? ch.channelType}
                                                </Badge>
                                                {ch.minimumSeverity && (
                                                    <Badge className="bg-yellow-500/10 text-yellow-400 border-yellow-500/20 border text-xs">
                                                        ≥ {ch.minimumSeverity}
                                                    </Badge>
                                                )}
                                                {(ch.notifyOnIncidentCreated ?? true) && (
                                                    <Badge variant="outline" className="text-[10px] font-normal">
                                                        {t("notificationChannels.badgeCreated")}
                                                    </Badge>
                                                )}
                                                {ch.notifyOnIncidentAcknowledged && (
                                                    <Badge variant="outline" className="text-[10px] font-normal">
                                                        {t("notificationChannels.badgeAck")}
                                                    </Badge>
                                                )}
                                                {ch.notifyOnIncidentResolved && (
                                                    <Badge variant="outline" className="text-[10px] font-normal">
                                                        {t("notificationChannels.badgeResolved")}
                                                    </Badge>
                                                )}
                                            </div>
                                            {ch.serviceFilter.length > 0 && (
                                                <div className="flex flex-wrap items-center gap-1.5">
                                                    <Filter className="w-3.5 h-3.5 text-muted-foreground shrink-0" />
                                                    {ch.serviceFilter.map((id) => (
                                                        <Badge
                                                            key={id}
                                                            variant="outline"
                                                            className="text-[10px] font-normal border-border"
                                                        >
                                                            {serviceNameById.get(id) ?? id.slice(0, 8)}
                                                        </Badge>
                                                    ))}
                                                </div>
                                            )}
                                            <div className="flex flex-wrap items-center gap-3 text-xs text-muted-foreground">
                                                <span className="flex items-center gap-1">
                                                    <Hash className="w-3 h-3" />
                                                    {t("notificationChannels.sentCount", {
                                                        count: String(ch.notificationCount),
                                                    })}
                                                </span>
                                                {ch.lastNotifiedAt && (
                                                    <span>
                                                        {t("notificationChannels.lastSent")}{" "}
                                                        {new Date(ch.lastNotifiedAt).toLocaleString()}
                                                    </span>
                                                )}
                                            </div>
                                        </div>
                                    </div>
                                    <div className="flex items-center gap-2 shrink-0">
                                        <Switch
                                            checked={ch.isEnabled}
                                            onCheckedChange={() => toggleMutation.mutate(ch.id)}
                                        />
                                        <Button
                                            size="sm"
                                            variant="outline"
                                            onClick={() => testMutation.mutate({ id: ch.id, message: t("notificationChannels.testMessageFromSettings") })}
                                            disabled={testMutation.isPending}
                                        >
                                            <Send className="w-3 h-3" />
                                        </Button>
                                        <Button size="sm" variant="outline" onClick={() => openEdit(ch)}>
                                            <Pencil className="w-3 h-3" />
                                        </Button>
                                        <Button
                                            size="sm"
                                            variant="outline"
                                            className="text-error-400"
                                            onClick={() => deleteMutation.mutate(ch.id)}
                                        >
                                            <Trash2 className="w-3 h-3" />
                                        </Button>
                                    </div>
                                </div>
                            </Card>
                        );
                    })}
                </div>
            )}

            <Dialog
                open={isEditorOpen}
                onOpenChange={(open) => {
                    setIsEditorOpen(open);
                    if (!open) {
                        setFormApiLines(null);
                    }
                }}
            >
                <DialogContent className="max-w-2xl max-h-[90vh] flex flex-col p-0 gap-0 overflow-hidden">
                    <DialogHeader className="p-6 pb-4 shrink-0 border-b border-border">
                        <DialogTitle>
                            {editing ? `${t("common.edit")} — ${t("notificationChannels.channel")}` : t("settings.notifications.add")}
                        </DialogTitle>
                        {selectedType?.description && (
                            <DialogDescription className="text-left">{selectedType.description}</DialogDescription>
                        )}
                    </DialogHeader>

                    <ScrollArea className="flex-1 min-h-0 max-h-[calc(90vh-12rem)]">
                        <div className="p-6 space-y-6">
                            {formApiLines && formApiLines.length > 0 && (
                                <Alert variant="destructive">
                                    <AlertCircle />
                                    <AlertTitle>{t("notificationChannels.serverErrorTitle")}</AlertTitle>
                                    <AlertDescription>
                                        <ul className="list-disc pl-4 space-y-1 text-sm">
                                            {formApiLines.map((line, i) => (
                                                <li key={i}>{line}</li>
                                            ))}
                                        </ul>
                                    </AlertDescription>
                                </Alert>
                            )}
                            {!formApiLines && saveBlockedReasons.length > 0 && (
                                <Alert>
                                    <AlertTitle>{t("notificationChannels.saveBlockedTitle")}</AlertTitle>
                                    <AlertDescription>
                                        <ul className="list-disc pl-4 space-y-1 text-sm">
                                            {saveBlockedReasons.map((line, i) => (
                                                <li key={i}>{line}</li>
                                            ))}
                                        </ul>
                                    </AlertDescription>
                                </Alert>
                            )}
                            <div className="space-y-2">
                                <label className="text-xs font-semibold text-muted-foreground uppercase tracking-wide">
                                    {t("notificationChannels.fieldName")}
                                </label>
                                <Input
                                    placeholder={t("notificationChannels.namePlaceholder")}
                                    value={form.name}
                                    onChange={(e) => setForm((f) => ({ ...f, name: e.target.value }))}
                                />
                            </div>

                            {!editing && (
                                <div className="space-y-2">
                                    <label className="text-xs font-semibold text-muted-foreground uppercase tracking-wide">
                                        {t("notificationChannels.fieldType")}
                                    </label>
                                    <Select
                                        value={form.channelType}
                                        onValueChange={(v) =>
                                            setForm((f) => ({
                                                ...f,
                                                channelType: v,
                                                configuration: {},
                                            }))
                                        }
                                    >
                                        <SelectTrigger>
                                            <SelectValue />
                                        </SelectTrigger>
                                        <SelectContent>
                                            {channelTypes.map((ct) => (
                                                <SelectItem key={ct.value} value={ct.value}>
                                                    {ct.icon} {ct.label}
                                                </SelectItem>
                                            ))}
                                        </SelectContent>
                                    </Select>
                                </div>
                            )}

                            {typeFields.length > 0 && (
                                <>
                                    <div>
                                        <h4 className="text-xs font-semibold text-muted-foreground uppercase tracking-wide mb-3">
                                            {t("notificationChannels.sectionConnection")}
                                        </h4>
                                        <div className="space-y-4">
                                            {typeFields.map((field) => (
                                                <div key={field.key}>
                                                    <div className="flex items-center justify-between gap-2">
                                                        <label className="text-sm font-medium text-foreground">
                                                            {field.label}
                                                            {field.required && (
                                                                <span className="text-destructive ml-0.5" aria-hidden>
                                                                    *
                                                                </span>
                                                            )}
                                                        </label>
                                                        {field.helpUrl && (
                                                            <a
                                                                href={field.helpUrl}
                                                                target="_blank"
                                                                rel="noopener noreferrer"
                                                                className="inline-flex items-center gap-1 text-xs text-brand-400 hover:text-brand-300"
                                                            >
                                                                {t("notificationChannels.docs")}
                                                                <ExternalLink className="w-3 h-3" />
                                                            </a>
                                                        )}
                                                    </div>
                                                    <ConfigFieldInput
                                                        field={field}
                                                        value={form.configuration[field.key] ?? ""}
                                                        onChange={(v) =>
                                                            setForm((f) => ({
                                                                ...f,
                                                                configuration: { ...f.configuration, [field.key]: v },
                                                            }))
                                                        }
                                                    />
                                                </div>
                                            ))}
                                        </div>
                                    </div>
                                    {form.channelType === "Webhook" && (
                                        <div className="mt-4 space-y-2 rounded-md border border-border bg-muted/15 p-3">
                                            <div className="flex flex-wrap items-center justify-between gap-2">
                                                <h5 className="text-sm font-medium text-foreground">
                                                    {t("notificationChannels.webhookPayloadTitle")}
                                                </h5>
                                                <Button
                                                    type="button"
                                                    size="sm"
                                                    variant="outline"
                                                    className="shrink-0 h-8"
                                                    onClick={async () => {
                                                        try {
                                                            await navigator.clipboard.writeText(webhookSampleJson);
                                                            toast.success(t("common.copied"));
                                                        } catch {
                                                            toast.error(t("common.error"), t("common.copyFailed"));
                                                        }
                                                    }}
                                                >
                                                    <Copy className="w-3.5 h-3.5 mr-1.5" />
                                                    {t("notificationChannels.copySample")}
                                                </Button>
                                            </div>
                                            <p className="text-xs text-muted-foreground">
                                                {t("notificationChannels.webhookPayloadHint")}
                                            </p>
                                            <pre className="text-xs font-mono whitespace-pre-wrap break-all rounded border border-border bg-background/80 p-3 max-h-40 overflow-y-auto">
                                                {webhookSampleJson}
                                            </pre>
                                        </div>
                                    )}
                                    <Separator />
                                </>
                            )}

                            <div>
                                <h4 className="text-xs font-semibold text-muted-foreground uppercase tracking-wide mb-3">
                                    {t("notificationChannels.sectionFilters")}
                                </h4>
                                <div className="space-y-4">
                                    <div className="space-y-3 rounded-md border border-border bg-muted/10 p-3">
                                        <label className="text-sm font-medium">{t("notificationChannels.lifecycleTitle")}</label>
                                        <p className="text-xs text-muted-foreground">{t("notificationChannels.lifecycleHint")}</p>
                                        <div className="space-y-2">
                                            <label className="flex items-center justify-between gap-3 text-sm cursor-pointer">
                                                <span>{t("notificationChannels.triggerCreated")}</span>
                                                <Switch
                                                    checked={form.notifyOnIncidentCreated}
                                                    onCheckedChange={(v) =>
                                                        setForm((f) => ({ ...f, notifyOnIncidentCreated: Boolean(v) }))
                                                    }
                                                />
                                            </label>
                                            <label className="flex items-center justify-between gap-3 text-sm cursor-pointer">
                                                <span>{t("notificationChannels.triggerAcknowledged")}</span>
                                                <Switch
                                                    checked={form.notifyOnIncidentAcknowledged}
                                                    onCheckedChange={(v) =>
                                                        setForm((f) => ({ ...f, notifyOnIncidentAcknowledged: Boolean(v) }))
                                                    }
                                                />
                                            </label>
                                            <label className="flex items-center justify-between gap-3 text-sm cursor-pointer">
                                                <span>{t("notificationChannels.triggerResolved")}</span>
                                                <Switch
                                                    checked={form.notifyOnIncidentResolved}
                                                    onCheckedChange={(v) =>
                                                        setForm((f) => ({ ...f, notifyOnIncidentResolved: Boolean(v) }))
                                                    }
                                                />
                                            </label>
                                        </div>
                                    </div>

                                    <div className="space-y-2">
                                        <label className="text-sm font-medium">{t("notificationChannels.minimumSeverity")}</label>
                                        <Select
                                            value={severitySelectValue}
                                            onValueChange={(v) =>
                                                setForm((f) => ({
                                                    ...f,
                                                    minimumSeverity: v === SEVERITY_ALL ? undefined : v,
                                                }))
                                            }
                                        >
                                            <SelectTrigger>
                                                <SelectValue placeholder={t("notificationChannels.allSeverities")} />
                                            </SelectTrigger>
                                            <SelectContent>
                                                <SelectItem value={SEVERITY_ALL}>{t("notificationChannels.allSeverities")}</SelectItem>
                                                {severityOptions.map((s: string) => (
                                                    <SelectItem key={s} value={s}>
                                                        {s}
                                                    </SelectItem>
                                                ))}
                                            </SelectContent>
                                        </Select>
                                    </div>

                                    <div className="space-y-2">
                                        <label className="text-sm font-medium">{t("notificationChannels.serviceScope")}</label>
                                        <p className="text-xs text-muted-foreground">{t("notificationChannels.serviceScopeHint")}</p>
                                        {services.length === 0 ? (
                                            <p className="text-sm text-muted-foreground italic">{t("notificationChannels.noServices")}</p>
                                        ) : (
                                            <div className="rounded-md border border-border bg-muted/20 p-3 max-h-48 overflow-y-auto space-y-2">
                                                {services.map((svc) => (
                                                    <label
                                                        key={svc.id}
                                                        className="flex items-center gap-2 text-sm cursor-pointer hover:bg-muted/40 rounded px-1 py-0.5"
                                                    >
                                                        <Checkbox
                                                            checked={form.serviceFilter.includes(svc.id)}
                                                            onCheckedChange={() => toggleServiceFilter(svc.id)}
                                                        />
                                                        <span className="truncate">{svc.name}</span>
                                                    </label>
                                                ))}
                                            </div>
                                        )}
                                    </div>
                                </div>
                            </div>
                        </div>
                    </ScrollArea>

                    <DialogFooter className="p-6 pt-4 border-t border-border shrink-0">
                        <Button variant="outline" onClick={() => setIsEditorOpen(false)}>
                            {t("common.cancel")}
                        </Button>
                        <Button
                            onClick={handleSave}
                            disabled={
                                !form.name.trim() ||
                                !requiredConfigSatisfied ||
                                !lifecycleAny ||
                                createMutation.isPending ||
                                updateMutation.isPending
                            }
                        >
                            {(createMutation.isPending || updateMutation.isPending) && (
                                <Loader2 className="w-4 h-4 mr-2 animate-spin" />
                            )}
                            {editing ? t("common.save") : t("common.create")}
                        </Button>
                    </DialogFooter>
                </DialogContent>
            </Dialog>
        </div>
    );
}
