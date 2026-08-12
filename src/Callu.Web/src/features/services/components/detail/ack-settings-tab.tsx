import { useState, useId, type Dispatch, type SetStateAction } from "react";
import { t } from "@/shared/locales/i18n";
import { Button } from "@/shared/components/ui/button";
import { Badge } from "@/shared/components/ui/badge";
import { Input } from "@/shared/components/ui/input";
import { Textarea } from "@/shared/components/ui/textarea";
import { Card } from "@/shared/components/ui/card";
import { Checkbox } from "@/shared/components/ui/checkbox";
import { Switch } from "@/shared/components/ui/switch";
import { TabsContent } from "@/shared/components/ui/tabs";
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/shared/components/ui/dialog";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";
import { Loader2, Pencil, Plus, Trash2, X, Zap } from "lucide-react";
import { EmptyState } from "@/shared/components/empty-state";
import { DeleteConfirmDialog } from "@/shared/components/delete-confirm-dialog";
import {
  useServiceActions,
  useCreateServiceAction,
  useUpdateServiceAction,
  useDeleteServiceAction,
} from "../../hooks/use-service-actions";
import type { ServiceActionDto } from "../../types/service.types";
import type { AckHeader } from "../../utils/ack-headers";

const EVENT_FLAGS = [
  { flag: 1, key: "serviceActions.eventCreated" },
  { flag: 2, key: "serviceActions.eventAcknowledged" },
  { flag: 4, key: "serviceActions.eventResolved" },
  { flag: 8, key: "serviceActions.eventClosed" },
  { flag: 16, key: "serviceActions.eventReopened" },
] as const;

// A stored null means the legacy pair acknowledge+resolve, shown as 2|4.
const LEGACY_EVENTS = 6;

/** Fully controlled by ServiceDetail, so its one Save button sends the ack config with the rest of the
 * service; the setters keep their dispatch signatures because a functional updater is used. */
export interface AckSettingsTabProps {
  formId: string;
  serviceId: string;
  ackEnabled: boolean;
  setAckEnabled: Dispatch<SetStateAction<boolean>>;
  ackUrl: string;
  setAckUrl: Dispatch<SetStateAction<string>>;
  ackHttpMethod: string;
  setAckHttpMethod: Dispatch<SetStateAction<string>>;
  ackContentType: string;
  setAckContentType: Dispatch<SetStateAction<string>>;
  ackHeaders: AckHeader[];
  setAckHeaders: Dispatch<SetStateAction<AckHeader[]>>;
  ackPayloadTemplate: string;
  setAckPayloadTemplate: Dispatch<SetStateAction<string>>;
  ackEvents: number | null;
  setAckEvents: Dispatch<SetStateAction<number | null>>;
  ackSecret: string;
  setAckSecret: Dispatch<SetStateAction<string>>;
  setAckSecretCleared: Dispatch<SetStateAction<boolean>>;
  ackSignatureHeader: string;
  setAckSignatureHeader: Dispatch<SetStateAction<string>>;
  hasAckSecret: boolean;
}

export function AckSettingsTab({
  formId,
  serviceId,
  ackEnabled,
  setAckEnabled,
  ackUrl,
  setAckUrl,
  ackHttpMethod,
  setAckHttpMethod,
  ackContentType,
  setAckContentType,
  ackHeaders,
  setAckHeaders,
  ackPayloadTemplate,
  setAckPayloadTemplate,
  ackEvents,
  setAckEvents,
  ackSecret,
  setAckSecret,
  setAckSecretCleared,
  ackSignatureHeader,
  setAckSignatureHeader,
  hasAckSecret,
}: AckSettingsTabProps) {
  const effectiveEvents = ackEvents ?? LEGACY_EVENTS;
  const toggleEvent = (flag: number, checked: boolean) => {
    setAckEvents((prev) => {
      const current = prev ?? LEGACY_EVENTS;
      return checked ? current | flag : current & ~flag;
    });
  };

  return (
          <TabsContent value="ack-settings" className="space-y-6">
            <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
              <div className="lg:col-span-2 space-y-6">
                <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
                  <div className="flex items-center justify-between mb-6">
                    <div>
                      <h3 style={{ fontSize: "1.125rem", fontWeight: 600 }}>{t("serviceActions.eventCallbackTitle")}</h3>
                      <p style={{ fontSize: "0.8125rem", color: "#94A3B8", marginTop: "0.25rem" }}>
                        {t("serviceActions.eventCallbackSubtitle")}
                      </p>
                    </div>
                    <Switch checked={ackEnabled} onCheckedChange={setAckEnabled} />
                  </div>

                  {ackEnabled && (
                    <div className="space-y-4">
                      <div>
                        <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                          {t("serviceActions.eventsLabel")}
                        </label>
                        <div className="flex flex-wrap gap-4">
                          {EVENT_FLAGS.map(({ flag, key }) => (
                            <label key={key} className="flex items-center gap-2 text-sm cursor-pointer">
                              <Checkbox
                                checked={(effectiveEvents & flag) !== 0}
                                onCheckedChange={(checked) => toggleEvent(flag, checked === true)}
                              />
                              {t(key)}
                            </label>
                          ))}
                        </div>
                      </div>

                      <div>
                        <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                          Callback URL
                        </label>
                        <Input
                          value={ackUrl}
                          onChange={(e) => setAckUrl(e.target.value)}
                          placeholder={t("services.ackCallbackUrlPlaceholder")}
                          className="bg-input-background font-mono text-sm"
                        />
                      </div>

                      <div className="grid grid-cols-2 gap-4">
                        <div>
                          <label htmlFor={`${formId}-ack-http-method`} style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                            HTTP Method
                          </label>
                          <Select value={ackHttpMethod} onValueChange={setAckHttpMethod}>
                            <SelectTrigger id={`${formId}-ack-http-method`} className="bg-input-background"><SelectValue /></SelectTrigger>
                            <SelectContent>
                              <SelectItem value="POST">POST</SelectItem>
                              <SelectItem value="PUT">PUT</SelectItem>
                              <SelectItem value="PATCH">PATCH</SelectItem>
                            </SelectContent>
                          </Select>
                        </div>
                        <div>
                          <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                            Content-Type
                          </label>
                          <Input
                            value={ackContentType}
                            onChange={(e) => setAckContentType(e.target.value)}
                            className="bg-input-background font-mono text-sm"
                          />
                        </div>
                      </div>

                      <div>
                        <div className="flex items-center justify-between mb-2">
                          <label style={{ fontSize: "0.875rem", fontWeight: 600 }}>{t("services.ackCustomHeaders")}</label>
                          <Button
                            type="button"
                            variant="outline"
                            size="sm"
                            className="bg-input-background"
                            onClick={() => setAckHeaders([...ackHeaders, { key: "", value: "" }])}
                          >
                            <Plus className="w-3 h-3 mr-1" /> Add Header
                          </Button>
                        </div>
                        {ackHeaders.length > 0 ? (
                          <div className="space-y-2">
                            {ackHeaders.map((h, i) => (
                              <div key={i} className="flex gap-2">
                                <Input
                                  value={h.key}
                                  onChange={(e) => {
                                    const next = [...ackHeaders];
                                    next[i] = { ...next[i], key: e.target.value };
                                    setAckHeaders(next);
                                  }}
                                  placeholder={t("services.headerNamePlaceholder")}
                                  className="bg-input-background font-mono text-sm flex-1"
                                />
                                <Input
                                  value={h.value}
                                  onChange={(e) => {
                                    const next = [...ackHeaders];
                                    next[i] = { ...next[i], value: e.target.value };
                                    setAckHeaders(next);
                                  }}
                                  placeholder={t("services.headerValuePlaceholder")}
                                  className="bg-input-background font-mono text-sm flex-1"
                                />
                                <Button
                                  type="button"
                                  variant="ghost"
                                  size="icon"
                                  className="text-error-500 hover:bg-error-500/10 flex-shrink-0"
                                  onClick={() => setAckHeaders(ackHeaders.filter((_, j) => j !== i))}
                                >
                                  <X className="w-4 h-4" />
                                </Button>
                              </div>
                            ))}
                          </div>
                        ) : (
                          <p style={{ fontSize: "0.8125rem", color: "#94A3B8" }}>
                            {t("services.ackHeadersHint")}
                          </p>
                        )}
                      </div>

                      <div className="grid grid-cols-2 gap-4">
                        <div>
                          <label htmlFor={`${formId}-ack-secret`} style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                            {t("serviceActions.ackSecretLabel")}
                          </label>
                          <div className="flex gap-2">
                            <Input
                              id={`${formId}-ack-secret`}
                              type="password"
                              value={ackSecret}
                              onChange={(e) => {
                                setAckSecret(e.target.value);
                                setAckSecretCleared(false);
                              }}
                              maxLength={256}
                              autoComplete="new-password"
                              className="bg-input-background flex-1"
                            />
                            {hasAckSecret && (
                              <Button
                                type="button"
                                variant="outline"
                                className="bg-input-background flex-shrink-0"
                                onClick={() => {
                                  setAckSecret("");
                                  setAckSecretCleared(true);
                                }}
                              >
                                {t("serviceActions.clearSecret")}
                              </Button>
                            )}
                          </div>
                          {hasAckSecret && (
                            <p style={{ fontSize: "0.75rem", color: "#94A3B8", marginTop: "0.25rem" }}>
                              {t("serviceActions.ackSecretSetHint")}
                            </p>
                          )}
                        </div>
                        <div>
                          <label htmlFor={`${formId}-ack-signature-header`} style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                            {t("serviceActions.ackSignatureHeaderLabel")}
                          </label>
                          <Input
                            id={`${formId}-ack-signature-header`}
                            value={ackSignatureHeader}
                            onChange={(e) => setAckSignatureHeader(e.target.value)}
                            maxLength={100}
                            className="bg-input-background font-mono text-sm"
                          />
                        </div>
                      </div>

                      <div>
                        <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                          {t("services.ackPayloadTemplateLabel")}
                        </label>
                        <Textarea
                          value={ackPayloadTemplate}
                          onChange={(e) => setAckPayloadTemplate(e.target.value)}
                          rows={12}
                          className="bg-input-background resize-none font-mono text-sm"
                          placeholder={`{\n  "action": "{{ ack_type }}",\n  "incident_id": "{{ incident.external_id }}",\n  "title": "{{ incident.title }}",\n  "status": "{{ incident.status }}"\n}`}
                        />
                      </div>
                    </div>
                  )}
                </Card>
              </div>

              <div className="space-y-6">
                <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
                  <h3 style={{ fontSize: "1.125rem", fontWeight: 600, marginBottom: "1rem" }}>{t("services.templateVariables")}</h3>
                  <div className="space-y-4">
                    <div>
                      <p style={{ fontSize: "0.8125rem", fontWeight: 600, color: "#94A3B8", marginBottom: "0.5rem" }}>{t("services.varGroupIncident")}</p>
                      <div className="space-y-1">
                        {["incident.id", "incident.title", "incident.description", "incident.severity", "incident.status", "incident.external_id", "incident.started_at", "incident.resolved_at"].map(v => (
                          <button
                            key={v}
                            type="button"
                            className="block w-full text-left px-2 py-1 rounded text-xs font-mono hover:bg-brand-500/10 text-muted-foreground hover:text-brand-500 transition-colors"
                            onClick={() => setAckPayloadTemplate(prev => prev + `{{ ${v} }}`)}
                          >
                            {`{{ ${v} }}`}
                          </button>
                        ))}
                      </div>
                    </div>
                    <div>
                      <p style={{ fontSize: "0.8125rem", fontWeight: 600, color: "#94A3B8", marginBottom: "0.5rem" }}>{t("services.varGroupService")}</p>
                      <div className="space-y-1">
                        {["service.id", "service.name"].map(v => (
                          <button
                            key={v}
                            type="button"
                            className="block w-full text-left px-2 py-1 rounded text-xs font-mono hover:bg-brand-500/10 text-muted-foreground hover:text-brand-500 transition-colors"
                            onClick={() => setAckPayloadTemplate(prev => prev + `{{ ${v} }}`)}
                          >
                            {`{{ ${v} }}`}
                          </button>
                        ))}
                      </div>
                    </div>
                    <div>
                      <p style={{ fontSize: "0.8125rem", fontWeight: 600, color: "#94A3B8", marginBottom: "0.5rem" }}>{t("services.varGroupAckType")}</p>
                      <div className="space-y-1">
                        <button
                          type="button"
                          className="block w-full text-left px-2 py-1 rounded text-xs font-mono hover:bg-brand-500/10 text-muted-foreground hover:text-brand-500 transition-colors"
                          onClick={() => setAckPayloadTemplate(prev => prev + '{{ ack_type }}')}
                        >
                          {'{{ ack_type }}'}
                        </button>
                        <p style={{ fontSize: "0.6875rem", color: "#64748b", paddingLeft: "0.5rem" }}>{t("services.ackTypeHint")}</p>
                      </div>
                    </div>
                  </div>
                </Card>

                <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
                  <h3 style={{ fontSize: "1.125rem", fontWeight: 600, marginBottom: "1rem" }}>{t("services.exampleTemplate")}</h3>
                  <pre className="p-3 rounded-lg bg-surface-light/20 border border-border text-xs font-mono overflow-x-auto whitespace-pre">{`{
  "action": "{{ ack_type }}",
  "alert_id": "{{ incident.external_id }}",
  "title": "{{ incident.title }}",
  "status": "{{ incident.status }}",
  "resolved_at": "{{ incident.resolved_at }}"
}`}</pre>
                </Card>
              </div>
            </div>

            <ManualActionsSection serviceId={serviceId} />
          </TabsContent>
  );
}

function ManualActionsSection({ serviceId }: { serviceId: string }) {
  const formId = useId();
  const { data: actions, isLoading } = useServiceActions(serviceId);
  const createMutation = useCreateServiceAction(serviceId);
  const updateMutation = useUpdateServiceAction(serviceId);
  const deleteMutation = useDeleteServiceAction(serviceId);

  const [dialogOpen, setDialogOpen] = useState(false);
  const [editing, setEditing] = useState<ServiceActionDto | null>(null);
  const [deleting, setDeleting] = useState<ServiceActionDto | null>(null);

  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [url, setUrl] = useState("");
  const [httpMethod, setHttpMethod] = useState("POST");
  const [contentType, setContentType] = useState("application/json");
  const [headersJson, setHeadersJson] = useState("");
  const [payloadTemplate, setPayloadTemplate] = useState("");
  const [secret, setSecret] = useState("");
  const [secretCleared, setSecretCleared] = useState(false);
  const [isEnabled, setIsEnabled] = useState(true);

  const list = actions ?? [];

  const resetForm = () => {
    setName("");
    setDescription("");
    setUrl("");
    setHttpMethod("POST");
    setContentType("application/json");
    setHeadersJson("");
    setPayloadTemplate("");
    setSecret("");
    setSecretCleared(false);
    setIsEnabled(true);
  };

  const openCreate = () => {
    resetForm();
    setEditing(null);
    setDialogOpen(true);
  };

  const openEdit = (action: ServiceActionDto) => {
    setName(action.name);
    setDescription(action.description ?? "");
    setUrl(action.url);
    setHttpMethod(action.httpMethod || "POST");
    setContentType(action.contentType || "application/json");
    setHeadersJson(action.headersJson ?? "");
    setPayloadTemplate(action.payloadTemplate ?? "");
    setSecret("");
    setSecretCleared(false);
    setIsEnabled(action.isEnabled);
    setEditing(action);
    setDialogOpen(true);
  };

  const closeDialog = () => {
    setDialogOpen(false);
    setEditing(null);
    resetForm();
  };

  const handleSubmit = async () => {
    if (!name.trim() || !url.trim()) return;
    if (editing) {
      await updateMutation.mutateAsync({
        actionId: editing.id,
        name: name.trim(),
        description: description.trim(),
        url: url.trim(),
        httpMethod,
        contentType: contentType.trim(),
        headersJson: headersJson.trim(),
        payloadTemplate: payloadTemplate.trim(),
        // Omitted keeps the stored secret, a typed value replaces it, "" clears it.
        ...(secret ? { secret } : secretCleared ? { secret: "" } : {}),
        isEnabled,
      });
    } else {
      await createMutation.mutateAsync({
        name: name.trim(),
        description: description.trim() || undefined,
        url: url.trim(),
        httpMethod,
        contentType,
        headersJson: headersJson.trim() || undefined,
        payloadTemplate: payloadTemplate.trim() || undefined,
        secret: secret || undefined,
        isEnabled,
        displayOrder: list.length,
      });
    }
    closeDialog();
  };

  const handleDelete = () => {
    if (!deleting) return;
    void deleteMutation.mutateAsync(deleting.id).then(() => setDeleting(null));
  };

  return (
    <>
      <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
        <div className="flex items-center justify-between mb-6">
          <div>
            <h3 style={{ fontSize: "1.125rem", fontWeight: 600 }}>{t("serviceActions.manualTitle")}</h3>
            <p style={{ fontSize: "0.8125rem", color: "#94A3B8", marginTop: "0.25rem" }}>
              {t("serviceActions.manualSubtitle")}
            </p>
          </div>
          <Button onClick={openCreate} className="bg-brand-500 hover:bg-brand-600 text-white">
            <Plus className="w-4 h-4 mr-2" />
            {t("serviceActions.addAction")}
          </Button>
        </div>

        {isLoading ? (
          <div className="flex justify-center py-8">
            <Loader2 className="w-6 h-6 animate-spin text-brand-500" />
          </div>
        ) : list.length === 0 ? (
          <EmptyState icon={Zap} title={t("serviceActions.noActions")} />
        ) : (
          <div className="space-y-3">
            {list.map((action) => (
              <div
                key={action.id}
                className="flex flex-col gap-3 rounded-lg border border-border p-4 sm:flex-row sm:items-center sm:justify-between"
              >
                <div className="min-w-0 space-y-1">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="font-semibold">{action.name}</span>
                    <Badge className="bg-brand-500/10 text-brand-500 border-brand-500/20 border text-xs">
                      {action.httpMethod}
                    </Badge>
                    {!action.isEnabled && (
                      <Badge className="bg-muted/10 text-muted-foreground border-muted/20 border text-xs">
                        {t("serviceActions.disabledBadge")}
                      </Badge>
                    )}
                  </div>
                  {action.description && (
                    <p className="text-sm text-muted-foreground">{action.description}</p>
                  )}
                  <code className="block text-xs font-mono text-muted-foreground break-all">
                    {action.url}
                  </code>
                </div>
                <div className="flex gap-2">
                  <Button
                    variant="outline"
                    size="icon"
                    className="bg-input-background"
                    aria-label={t("serviceActions.editAction")}
                    onClick={() => openEdit(action)}
                  >
                    <Pencil className="w-4 h-4" />
                  </Button>
                  <Button
                    variant="outline"
                    size="icon"
                    className="bg-input-background text-error-400"
                    aria-label={t("serviceActions.deleteAction")}
                    onClick={() => setDeleting(action)}
                  >
                    <Trash2 className="w-4 h-4" />
                  </Button>
                </div>
              </div>
            ))}
          </div>
        )}
      </Card>

      <Dialog open={dialogOpen} onOpenChange={(open) => { if (!open) closeDialog(); }}>
        <DialogContent className="bg-card border-border sm:max-w-lg">
          <DialogHeader>
            <DialogTitle style={{ fontSize: "1.5rem", fontWeight: 600 }}>
              {editing ? t("serviceActions.editAction") : t("serviceActions.addAction")}
            </DialogTitle>
          </DialogHeader>
          <div className="space-y-4 py-2">
            <div>
              <label htmlFor={`${formId}-action-name`} style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                {t("serviceActions.nameLabel")}
              </label>
              <Input
                id={`${formId}-action-name`}
                value={name}
                onChange={(e) => setName(e.target.value)}
                maxLength={100}
                className="bg-input-background"
              />
            </div>
            <div>
              <label htmlFor={`${formId}-action-description`} style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                {t("serviceActions.descriptionLabel")}
              </label>
              <Input
                id={`${formId}-action-description`}
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                maxLength={500}
                className="bg-input-background"
              />
            </div>
            <div>
              <label htmlFor={`${formId}-action-url`} style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                {t("serviceActions.urlLabel")}
              </label>
              <Input
                id={`${formId}-action-url`}
                value={url}
                onChange={(e) => setUrl(e.target.value)}
                maxLength={500}
                className="bg-input-background font-mono text-sm"
              />
            </div>
            <div className="grid grid-cols-2 gap-4">
              <div>
                <label htmlFor={`${formId}-action-method`} style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                  {t("serviceActions.methodLabel")}
                </label>
                <Select value={httpMethod} onValueChange={setHttpMethod}>
                  <SelectTrigger id={`${formId}-action-method`} className="bg-input-background"><SelectValue /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value="GET">GET</SelectItem>
                    <SelectItem value="POST">POST</SelectItem>
                    <SelectItem value="PUT">PUT</SelectItem>
                    <SelectItem value="PATCH">PATCH</SelectItem>
                  </SelectContent>
                </Select>
              </div>
              <div>
                <label htmlFor={`${formId}-action-content-type`} style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                  {t("serviceActions.contentTypeLabel")}
                </label>
                <Input
                  id={`${formId}-action-content-type`}
                  value={contentType}
                  onChange={(e) => setContentType(e.target.value)}
                  maxLength={50}
                  className="bg-input-background font-mono text-sm"
                />
              </div>
            </div>
            <div>
              <label htmlFor={`${formId}-action-headers`} style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                {t("serviceActions.headersLabel")}
              </label>
              <Textarea
                id={`${formId}-action-headers`}
                value={headersJson}
                onChange={(e) => setHeadersJson(e.target.value)}
                rows={3}
                maxLength={4000}
                placeholder={t("serviceActions.headersPlaceholder")}
                className="bg-input-background resize-none font-mono text-sm"
              />
            </div>
            <div>
              <label htmlFor={`${formId}-action-payload`} style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                {t("serviceActions.payloadLabel")}
              </label>
              <Textarea
                id={`${formId}-action-payload`}
                value={payloadTemplate}
                onChange={(e) => setPayloadTemplate(e.target.value)}
                rows={5}
                maxLength={8000}
                className="bg-input-background resize-none font-mono text-sm"
              />
            </div>
            <div>
              <label htmlFor={`${formId}-action-secret`} style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                {t("serviceActions.secretLabel")}
              </label>
              <div className="flex gap-2">
                <Input
                  id={`${formId}-action-secret`}
                  type="password"
                  value={secret}
                  onChange={(e) => {
                    setSecret(e.target.value);
                    setSecretCleared(false);
                  }}
                  maxLength={256}
                  autoComplete="new-password"
                  className="bg-input-background flex-1"
                />
                {editing?.hasSecret && (
                  <Button
                    type="button"
                    variant="outline"
                    className="bg-input-background flex-shrink-0"
                    onClick={() => {
                      setSecret("");
                      setSecretCleared(true);
                    }}
                  >
                    {t("serviceActions.clearSecret")}
                  </Button>
                )}
              </div>
              {editing?.hasSecret && (
                <p style={{ fontSize: "0.75rem", color: "#94A3B8", marginTop: "0.25rem" }}>
                  {t("serviceActions.ackSecretSetHint")}
                </p>
              )}
            </div>
            <div className="flex items-center justify-between rounded-lg border border-border p-3">
              <p style={{ fontSize: "0.875rem", fontWeight: 600 }}>{t("serviceActions.enabledLabel")}</p>
              <Switch checked={isEnabled} onCheckedChange={setIsEnabled} />
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={closeDialog} className="bg-input-background">
              {t("common.cancel")}
            </Button>
            <Button
              onClick={() => void handleSubmit()}
              disabled={!name.trim() || !url.trim() || createMutation.isPending || updateMutation.isPending}
              className="bg-brand-500 hover:bg-brand-600 text-white"
            >
              {(createMutation.isPending || updateMutation.isPending) ? (
                <Loader2 className="w-4 h-4 mr-2 animate-spin" />
              ) : null}
              {t("common.save")}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <DeleteConfirmDialog
        open={!!deleting}
        onOpenChange={(open) => { if (!open) setDeleting(null); }}
        title={t("serviceActions.deleteAction")}
        message={t("serviceActions.deleteConfirm")}
        onConfirm={handleDelete}
        isLoading={deleteMutation.isPending}
      />
    </>
  );
}
