import { useState, type ReactNode } from "react";
import { Link } from "react-router";
import { t } from "@/shared/locales/i18n";
import { toast } from "@/shared/utils/toast";
import { copyText } from "@/shared/utils/clipboard";
import { Button } from "@/shared/components/ui/button";
import { Badge } from "@/shared/components/ui/badge";
import { Card } from "@/shared/components/ui/card";
import { Input } from "@/shared/components/ui/input";
import { Switch } from "@/shared/components/ui/switch";
import {
  Dialog,
  DialogContent,
  DialogDescription,
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
import {
  Check,
  Copy,
  Link2,
  Loader2,
  Pencil,
  Plus,
  Radio,
  RefreshCw,
  Trash2,
} from "lucide-react";
import { LoadingState } from "@/shared/components/loading-state";
import { ErrorState } from "@/shared/components/error-state";
import { EmptyState } from "@/shared/components/empty-state";
import { DeleteConfirmDialog } from "@/shared/components/delete-confirm-dialog";
import { useAuth } from "@/shared/auth/auth.context";
import { hasPermission, PERMISSIONS } from "@/shared/auth/roles";
import { useServices } from "@/features/services/hooks/use-services";
import { useWebhookTemplates } from "@/features/settings/hooks/use-webhook-templates";
import {
  useIntegrations,
  useCreateIntegration,
  useUpdateIntegration,
  useDeleteIntegration,
  useRotateIntegrationCredentials,
  useBindIntegrationService,
} from "../hooks/use-integrations";
import type {
  IntegrationDto,
  IntegrationSecretsDto,
} from "../types/integrations.types";

const NONE = "__none__";

function absoluteWebhookUrl(path: string) {
  return `${window.location.origin}${path.startsWith("/") ? path : `/${path}`}`;
}

export function ApplicationEndpoints() {
  const { user } = useAuth();
  const canRotate = hasPermission(user?.role, PERMISSIONS.ManageIntegrations);
  const { data: items, isLoading, error } = useIntegrations();
  const { data: services } = useServices();
  const { data: templates } = useWebhookTemplates();
  const createMutation = useCreateIntegration();
  const updateMutation = useUpdateIntegration();
  const deleteMutation = useDeleteIntegration();
  const rotateMutation = useRotateIntegrationCredentials();
  const bindMutation = useBindIntegrationService();

  const [createOpen, setCreateOpen] = useState(false);
  const [editing, setEditing] = useState<IntegrationDto | null>(null);
  const [deleting, setDeleting] = useState<IntegrationDto | null>(null);
  const [binding, setBinding] = useState<IntegrationDto | null>(null);
  const [bindServiceId, setBindServiceId] = useState(NONE);
  const [secrets, setSecrets] = useState<IntegrationSecretsDto | null>(null);
  const [copied, setCopied] = useState<string | null>(null);

  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [serviceId, setServiceId] = useState("");
  const [templateId, setTemplateId] = useState(NONE);
  const [listeningMode, setListeningMode] = useState(false);
  const [signatureSecret, setSignatureSecret] = useState("");
  const [signatureHeader, setSignatureHeader] = useState("X-Callu-Signature");
  const [isActive, setIsActive] = useState(true);
  const [webhookEnabled, setWebhookEnabled] = useState(true);
  const [clearSecret, setClearSecret] = useState(false);

  const copy = async (text: string, field: string) => {
    // One of these copies an API key that is shown once; a checkmark it did not earn costs the operator the key.
    if (!(await copyText(text))) {
      toast.error(t("common.copyFailed"));
      return;
    }

    setCopied(field);
    setTimeout(() => setCopied(null), 2000);
  };

  const resetForm = () => {
    setName("");
    setDescription("");
    setServiceId("");
    setTemplateId(NONE);
    setListeningMode(false);
    setSignatureSecret("");
    setSignatureHeader("X-Callu-Signature");
    setIsActive(true);
    setWebhookEnabled(true);
    setClearSecret(false);
  };

  const openCreate = () => {
    resetForm();
    setCreateOpen(true);
  };

  const openEdit = (item: IntegrationDto) => {
    setName(item.name);
    setDescription(item.description ?? "");
    setServiceId(item.serviceId ?? "");
    setTemplateId(item.webhookTemplateId ?? NONE);
    setListeningMode(item.listeningMode);
    setSignatureSecret("");
    setSignatureHeader(item.signatureHeaderName ?? "X-Callu-Signature");
    setIsActive(item.isActive);
    setWebhookEnabled(item.webhookEnabled);
    setClearSecret(false);
    setEditing(item);
  };

  const openBind = (item: IntegrationDto) => {
    setBindServiceId(item.serviceId ?? NONE);
    setBinding(item);
  };

  const handleCreate = async () => {
    if (!name.trim()) return;
    const result = await createMutation.mutateAsync({
      name: name.trim(),
      type: "Webhook",
      description: description.trim() || undefined,
      serviceId: serviceId && serviceId !== NONE ? serviceId : undefined,
      webhookTemplateId: templateId === NONE ? undefined : templateId,
      listeningMode,
      webhookSecret: signatureSecret.trim() || undefined,
      webhookSignatureHeader: signatureSecret.trim()
        ? signatureHeader.trim() || undefined
        : undefined,
    });
    setCreateOpen(false);
    resetForm();
    if (result) setSecrets(result);
  };

  const handleUpdate = async () => {
    if (!editing || !name.trim()) return;
    let webhookSecret: string | undefined;
    if (clearSecret) webhookSecret = "";
    else if (signatureSecret.trim()) webhookSecret = signatureSecret.trim();

    await updateMutation.mutateAsync({
      id: editing.id,
      name: name.trim(),
      description: description.trim() || undefined,
      teamId: editing.teamId ?? undefined,
      webhookTemplateId: templateId === NONE ? undefined : templateId,
      isActive,
      webhookEnabled,
      listeningMode,
      webhookSecret,
      webhookSignatureHeader: clearSecret
        ? undefined
        : signatureHeader.trim() || undefined,
    });
    setEditing(null);
    resetForm();
  };

  const handleBind = async () => {
    if (!binding) return;
    await bindMutation.mutateAsync({
      id: binding.id,
      serviceId: bindServiceId === NONE ? null : bindServiceId,
    });
    toast.success(t("applications.bindSuccess"));
    setBinding(null);
  };

  const handleRotate = async (item: IntegrationDto) => {
    if (!confirm(t("inboundWebhooks.rotateConfirm"))) return;
    const result = await rotateMutation.mutateAsync(item.id);
    if (result) setSecrets(result);
  };

  if (isLoading) return <LoadingState message={t("common.loading")} />;

  if (error) {
    return (
      <ErrorState
        title={t("inboundWebhooks.title")}
        message={error instanceof Error ? error.message : t("common.errorOccurred")}
      />
    );
  }

  const list = items ?? [];

  return (
    <>
      <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
        <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between mb-4">
          <div className="flex items-start gap-3">
            <div className="w-10 h-10 rounded-lg bg-brand-500/10 border border-brand-500/20 flex items-center justify-center flex-shrink-0">
              <Radio className="w-5 h-5 text-brand-500" />
            </div>
            <div>
              <h3 className="text-lg font-semibold">{t("inboundWebhooks.title")}</h3>
              <p className="text-sm text-muted-foreground mt-1">
                {t("inboundWebhooks.subtitle")}
              </p>
            </div>
          </div>
          <Button onClick={openCreate} className="bg-brand-500 hover:bg-brand-600 text-white">
            <Plus className="w-4 h-4 mr-2" />
            {t("inboundWebhooks.create")}
          </Button>
        </div>

        <p className="text-sm text-muted-foreground mb-6">
          {t("inboundWebhooks.helpBody")}
        </p>

        {list.length === 0 ? (
          <EmptyState
            icon={Radio}
            title={t("inboundWebhooks.emptyTitle")}
            description={t("inboundWebhooks.emptyBody")}
          />
        ) : (
          <div className="space-y-3">
            {list.map((item) => (
              <div
                key={item.id}
                className="flex flex-col gap-3 rounded-lg border border-border p-4 lg:flex-row lg:items-center lg:justify-between"
              >
                <div className="min-w-0 space-y-1">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="font-semibold">{item.name}</span>
                    {item.isActive && item.webhookEnabled ? (
                      <Badge className="bg-success-500/10 text-success-500 border-success-500/20 border text-xs">
                        {t("common.active")}
                      </Badge>
                    ) : (
                      <Badge className="bg-muted/10 text-muted-foreground border-muted/20 border text-xs">
                        {t("common.inactive")}
                      </Badge>
                    )}
                    {item.listeningMode && (
                      <Badge className="bg-brand-500/10 text-brand-500 border-brand-500/20 border text-xs">
                        {t("applications.listeningBadge")}
                      </Badge>
                    )}
                  </div>
                  <p className="text-sm text-muted-foreground">
                    {item.serviceName
                      ? t("inboundWebhooks.feedsService", { service: item.serviceName })
                      : t("applications.unboundCaptureOnly")}
                    {item.webhookTemplateName
                      ? ` · ${item.webhookTemplateName}`
                      : ` · ${t("inboundWebhooks.noTemplate")}`}
                  </p>
                  {item.webhookUrl && (
                    <code className="block text-xs font-mono text-muted-foreground break-all">
                      {absoluteWebhookUrl(item.webhookUrl)}
                    </code>
                  )}
                  <p className="text-xs text-muted-foreground">
                    {t("inboundWebhooks.received", {
                      count: item.webhooksReceivedCount,
                    })}
                    {item.maskedApiKey ? ` · ${item.maskedApiKey}` : ""}
                    {item.capturedCount > 0 && (
                      <>
                        {" · "}
                        {t("applications.capturedCount", { count: item.capturedCount })}{" "}
                        <Link
                          to={`/applications/${item.id}/captures`}
                          className="text-brand-500 hover:underline"
                        >
                          {t("applications.viewCaptures")}
                        </Link>
                      </>
                    )}
                  </p>
                </div>
                <div className="flex flex-wrap gap-2">
                  {item.webhookUrl && (
                    <Button
                      variant="outline"
                      size="sm"
                      className="bg-input-background"
                      onClick={() => copy(absoluteWebhookUrl(item.webhookUrl!), "url-" + item.id)}
                    >
                      {copied === "url-" + item.id ? (
                        <Check className="w-4 h-4 text-success-500" />
                      ) : (
                        <Copy className="w-4 h-4" />
                      )}
                      <span className="ml-2">{t("inboundWebhooks.copyUrl")}</span>
                    </Button>
                  )}
                  <Button
                    variant="outline"
                    size="sm"
                    className="bg-input-background"
                    onClick={() => openBind(item)}
                  >
                    <Link2 className="w-4 h-4 mr-2" />
                    {t("applications.bindService")}
                  </Button>
                  <Button
                    variant="outline"
                    size="sm"
                    className="bg-input-background"
                    onClick={() => openEdit(item)}
                  >
                    <Pencil className="w-4 h-4 mr-2" />
                    {t("common.edit")}
                  </Button>
                  {canRotate && (
                    <Button
                      variant="outline"
                      size="sm"
                      className="bg-input-background"
                      onClick={() => void handleRotate(item)}
                      disabled={rotateMutation.isPending}
                    >
                      <RefreshCw className={`w-4 h-4 mr-2 ${rotateMutation.isPending ? "animate-spin" : ""}`} />
                      {t("inboundWebhooks.rotate")}
                    </Button>
                  )}
                  <Button
                    variant="outline"
                    size="sm"
                    className="bg-input-background text-error-400"
                    onClick={() => setDeleting(item)}
                  >
                    <Trash2 className="w-4 h-4" />
                  </Button>
                </div>
              </div>
            ))}
          </div>
        )}
      </Card>

      <Dialog open={createOpen} onOpenChange={setCreateOpen}>
        <DialogContent className="sm:max-w-lg">
          <DialogHeader>
            <DialogTitle>{t("inboundWebhooks.createTitle")}</DialogTitle>
            <DialogDescription>{t("inboundWebhooks.createHint")}</DialogDescription>
          </DialogHeader>
          <div className="space-y-4 py-2">
            <Field label={t("inboundWebhooks.name")}>
              <Input
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder={t("inboundWebhooks.namePlaceholder")}
                className="bg-input-background"
              />
            </Field>
            <Field label={t("inboundWebhooks.description")}>
              <Input
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                className="bg-input-background"
              />
            </Field>
            <Field label={t("inboundWebhooks.service")}>
              <Select
                value={serviceId || undefined}
                onValueChange={(value) => {
                  setServiceId(value);
                  setListeningMode(value === NONE);
                }}
              >
                <SelectTrigger className="bg-input-background">
                  <SelectValue placeholder={t("inboundWebhooks.servicePlaceholder")} />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={NONE}>{t("applications.noServiceCaptureOnly")}</SelectItem>
                  {(services ?? []).map((s) => (
                    <SelectItem key={s.id} value={s.id}>{s.name}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </Field>
            <Field label={t("inboundWebhooks.template")}>
              <Select value={templateId} onValueChange={setTemplateId}>
                <SelectTrigger className="bg-input-background">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={NONE}>{t("inboundWebhooks.noTemplate")}</SelectItem>
                  {(templates ?? []).filter((tpl) => tpl.isActive).map((tpl) => (
                    <SelectItem key={tpl.id} value={tpl.id}>{tpl.name}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </Field>
            <div className="flex items-center justify-between rounded-lg border border-border p-3">
              <div>
                <p className="text-sm font-semibold">{t("applications.listeningMode")}</p>
                <p className="text-xs text-muted-foreground">{t("applications.listeningModeHint")}</p>
              </div>
              <Switch checked={listeningMode} onCheckedChange={setListeningMode} />
            </div>
            <Field label={t("inboundWebhooks.signatureSecretOptional")}>
              <Input
                type="password"
                value={signatureSecret}
                onChange={(e) => setSignatureSecret(e.target.value)}
                className="bg-input-background"
                autoComplete="new-password"
              />
            </Field>
            {signatureSecret.trim() && (
              <Field label={t("inboundWebhooks.signatureHeader")}>
                <Input
                  value={signatureHeader}
                  onChange={(e) => setSignatureHeader(e.target.value)}
                  className="bg-input-background"
                />
              </Field>
            )}
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setCreateOpen(false)}>
              {t("common.cancel")}
            </Button>
            <Button
              onClick={() => void handleCreate()}
              disabled={!name.trim() || createMutation.isPending}
              className="bg-brand-500 hover:bg-brand-600 text-white"
            >
              {createMutation.isPending ? (
                <Loader2 className="w-4 h-4 mr-2 animate-spin" />
              ) : null}
              {t("inboundWebhooks.create")}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog open={!!editing} onOpenChange={(open) => { if (!open) { setEditing(null); resetForm(); } }}>
        <DialogContent className="sm:max-w-lg">
          <DialogHeader>
            <DialogTitle>{t("inboundWebhooks.editTitle")}</DialogTitle>
            <DialogDescription>
              {editing?.serviceName
                ? t("inboundWebhooks.feedsService", { service: editing.serviceName })
                : t("applications.unboundCaptureOnly")}
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-4 py-2">
            <Field label={t("inboundWebhooks.name")}>
              <Input
                value={name}
                onChange={(e) => setName(e.target.value)}
                className="bg-input-background"
              />
            </Field>
            <Field label={t("inboundWebhooks.description")}>
              <Input
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                className="bg-input-background"
              />
            </Field>
            <Field label={t("inboundWebhooks.template")}>
              <Select value={templateId} onValueChange={setTemplateId}>
                <SelectTrigger className="bg-input-background">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={NONE}>{t("inboundWebhooks.noTemplate")}</SelectItem>
                  {(templates ?? []).filter((tpl) => tpl.isActive).map((tpl) => (
                    <SelectItem key={tpl.id} value={tpl.id}>{tpl.name}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </Field>
            <div className="flex items-center justify-between rounded-lg border border-border p-3">
              <div>
                <p className="text-sm font-semibold">{t("inboundWebhooks.active")}</p>
                <p className="text-xs text-muted-foreground">{t("inboundWebhooks.activeHint")}</p>
              </div>
              <Switch checked={isActive} onCheckedChange={setIsActive} />
            </div>
            <div className="flex items-center justify-between rounded-lg border border-border p-3">
              <div>
                <p className="text-sm font-semibold">{t("applications.enabled")}</p>
                <p className="text-xs text-muted-foreground">{t("inboundWebhooks.listeningHint")}</p>
              </div>
              <Switch checked={webhookEnabled} onCheckedChange={setWebhookEnabled} />
            </div>
            <div className="flex items-center justify-between rounded-lg border border-border p-3">
              <div>
                <p className="text-sm font-semibold">{t("applications.listeningMode")}</p>
                <p className="text-xs text-muted-foreground">{t("applications.listeningModeHint")}</p>
              </div>
              <Switch checked={listeningMode} onCheckedChange={setListeningMode} />
            </div>
            <Field label={t("inboundWebhooks.signatureSecretOptional")}>
              <Input
                type="password"
                value={signatureSecret}
                onChange={(e) => {
                  setSignatureSecret(e.target.value);
                  if (e.target.value) setClearSecret(false);
                }}
                disabled={clearSecret}
                placeholder={editing?.hasSignatureSecret
                  ? t("inboundWebhooks.secretKeep")
                  : undefined}
                className="bg-input-background"
                autoComplete="new-password"
              />
            </Field>
            {editing?.hasSignatureSecret && (
              <label className="flex items-center gap-2 text-sm cursor-pointer">
                <input
                  type="checkbox"
                  checked={clearSecret}
                  onChange={(e) => {
                    setClearSecret(e.target.checked);
                    if (e.target.checked) setSignatureSecret("");
                  }}
                />
                {t("inboundWebhooks.clearSecret")}
              </label>
            )}
            {(signatureSecret.trim() || (editing?.hasSignatureSecret && !clearSecret)) && (
              <Field label={t("inboundWebhooks.signatureHeader")}>
                <Input
                  value={signatureHeader}
                  onChange={(e) => setSignatureHeader(e.target.value)}
                  className="bg-input-background"
                />
              </Field>
            )}
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => { setEditing(null); resetForm(); }}>
              {t("common.cancel")}
            </Button>
            <Button
              onClick={() => void handleUpdate()}
              disabled={!name.trim() || updateMutation.isPending}
              className="bg-brand-500 hover:bg-brand-600 text-white"
            >
              {updateMutation.isPending ? (
                <Loader2 className="w-4 h-4 mr-2 animate-spin" />
              ) : null}
              {t("common.save")}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog open={!!binding} onOpenChange={(open) => { if (!open) setBinding(null); }}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>{t("applications.bindService")}</DialogTitle>
            <DialogDescription>{t("applications.bindServiceHint")}</DialogDescription>
          </DialogHeader>
          <div className="py-2">
            <Field label={t("inboundWebhooks.service")}>
              <Select value={bindServiceId} onValueChange={setBindServiceId}>
                <SelectTrigger className="bg-input-background">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={NONE}>{t("applications.unbindService")}</SelectItem>
                  {(services ?? []).map((s) => (
                    <SelectItem key={s.id} value={s.id}>{s.name}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </Field>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setBinding(null)}>
              {t("common.cancel")}
            </Button>
            <Button
              onClick={() => void handleBind()}
              disabled={bindMutation.isPending}
              className="bg-brand-500 hover:bg-brand-600 text-white"
            >
              {bindMutation.isPending ? (
                <Loader2 className="w-4 h-4 mr-2 animate-spin" />
              ) : null}
              {t("common.save")}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog open={!!secrets} onOpenChange={(open) => { if (!open) setSecrets(null); }}>
        <DialogContent className="sm:max-w-lg">
          <DialogHeader>
            <DialogTitle>{t("inboundWebhooks.secretsTitle")}</DialogTitle>
            <DialogDescription>{t("inboundWebhooks.secretsHint")}</DialogDescription>
          </DialogHeader>
          {secrets && (
            <div className="space-y-3 py-2">
              <SecretRow
                label={t("inboundWebhooks.webhookUrl")}
                value={absoluteWebhookUrl(secrets.webhookUrl)}
                copied={copied === "secret-url"}
                onCopy={() => copy(absoluteWebhookUrl(secrets.webhookUrl), "secret-url")}
              />
              <SecretRow
                label={t("inboundWebhooks.apiKey")}
                value={secrets.apiKey}
                copied={copied === "secret-key"}
                onCopy={() => copy(secrets.apiKey, "secret-key")}
                prefix="X-Callu-Api-Key: "
              />
            </div>
          )}
          <DialogFooter>
            <Button onClick={() => setSecrets(null)} className="bg-brand-500 hover:bg-brand-600 text-white">
              {t("inboundWebhooks.secretsDone")}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <DeleteConfirmDialog
        open={!!deleting}
        onOpenChange={(open) => { if (!open) setDeleting(null); }}
        title={t("inboundWebhooks.deleteTitle")}
        message={t("inboundWebhooks.deleteBody", { name: deleting?.name ?? "" })}
        onConfirm={() => {
          if (!deleting) return;
          void deleteMutation.mutateAsync(deleting.id).then(() => setDeleting(null));
        }}
        isLoading={deleteMutation.isPending}
      />
    </>
  );
}

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div>
      <label className="mb-1.5 block text-sm font-semibold">{label}</label>
      {children}
    </div>
  );
}

function SecretRow({
  label,
  value,
  copied,
  onCopy,
  prefix,
}: {
  label: string;
  value: string;
  copied: boolean;
  onCopy: () => void;
  prefix?: string;
}) {
  return (
    <div className="rounded-lg border border-warning-500/30 bg-warning-500/10 p-3">
      <div className="mb-2 flex items-center justify-between gap-2">
        <p className="text-xs font-semibold text-warning-500">{label}</p>
        <Button variant="ghost" size="sm" className="h-6 px-2" onClick={onCopy}>
          {copied ? <Check className="w-3 h-3 text-success-500" /> : <Copy className="w-3 h-3" />}
        </Button>
      </div>
      <code className="block break-all rounded bg-input-background px-2 py-1 text-xs">
        {prefix}
        {value}
      </code>
    </div>
  );
}
