import { useState } from "react";
import { Link } from "react-router";
import { t } from "@/shared/locales/i18n";
import { Button } from "@/shared/components/ui/button";
import { Badge } from "@/shared/components/ui/badge";
import { Card } from "@/shared/components/ui/card";
import { Switch } from "@/shared/components/ui/switch";
import { TabsContent } from "@/shared/components/ui/tabs";
import {
  Activity,
  Check,
  Code,
  Copy,
  ExternalLink,
  Eye,
  Globe,
  Key,
  Loader2,
  Plus,
  Radio,
  RefreshCw,
  PowerOff,
} from "lucide-react";
import {
  useDisableWebhook,
  useSetWebhookTemplate,
  useRegenerateToken,
  useRegenerateApiKey,
  useToggleListeningMode,
  useSetSignature,
  useClearSignature,
} from "../../hooks/use-webhook-settings";
import { DeleteConfirmDialog } from "@/shared/components/delete-confirm-dialog";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/shared/components/ui/select";
import { useWebhookTemplates } from "@/features/settings/hooks/use-webhook-templates";
import type { ServiceWebhookSettingsDto } from "../../types/webhook-settings.types";
import { formatRelativeTime } from "../../utils/service-badges";

export interface WebhooksTabProps {
  serviceId: string;
  webhookSettings: ServiceWebhookSettingsDto | undefined;
  isWebhookLoading: boolean;
  /** Shown in the stats card; the tab does not otherwise need the service. */
  incidentCount: number;
}

export function WebhooksTab({ serviceId: id, webhookSettings, isWebhookLoading, incidentCount }: WebhooksTabProps) {
  const disableWebhookMutation = useDisableWebhook();
  const setTemplateMutation = useSetWebhookTemplate();
  const { data: allTemplates } = useWebhookTemplates();
  const regenerateTokenMutation = useRegenerateToken();
  const regenerateApiKeyMutation = useRegenerateApiKey();
  const toggleListeningModeMutation = useToggleListeningMode();
  const setSignatureMutation = useSetSignature();
  const clearSignatureMutation = useClearSignature();

  const [signatureForm, setSignatureForm] = useState({ secret: "", headerName: "X-Callu-Signature" });
  // Shown once, right after generation — the server never returns these again.
  const [signaturePlaintext, setSignaturePlaintext] = useState<string | null>(null);
  const [apiKeyPlaintext, setApiKeyPlaintext] = useState<string | null>(null);
  const [showHowToCall, setShowHowToCall] = useState(false);
  const [isDisabling, setIsDisabling] = useState(false);
  const [copiedField, setCopiedField] = useState<string | null>(null);

  const copyToClipboard = (text: string, field: string) => {
    navigator.clipboard.writeText(text);
    setCopiedField(field);
    setTimeout(() => setCopiedField(null), 2000);
  };

  return (
          <TabsContent value="webhooks" className="space-y-6">
            {isWebhookLoading ? (
              <div className="flex items-center justify-center py-12">
                <Loader2 className="w-6 h-6 animate-spin text-brand-500" />
              </div>
            ) : (
              <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
                <Card className="p-6 bg-card/80 backdrop-blur-sm border-border lg:col-span-2">
                  <div className="flex items-center justify-between mb-4">
                    <div className="flex items-center gap-2">
                      <Globe className="w-5 h-5 text-brand-500" />
                      <h3 style={{ fontSize: "1.125rem", fontWeight: 600 }}>{t("services.webhookEndpoint")}</h3>
                    </div>
                    <div className="flex items-center gap-2">
                    <Badge className={webhookSettings?.webhookEnabled
                      ? "bg-success-500/10 text-success-500 border-success-500/20 border"
                      : "bg-muted/10 text-muted-foreground border-muted/20 border"
                    }>
                      {webhookSettings?.webhookEnabled ? t("common.active") : t("common.inactive")}
                    </Badge>
                      {webhookSettings?.webhookEnabled && (
                        <Button
                          variant="ghost"
                          size="sm"
                          className="text-error-400"
                          aria-label={t("services.disableWebhookAria")}
                          onClick={() => setIsDisabling(true)}
                        >
                          <PowerOff className="w-4 h-4 mr-2" />
                          {t("services.disableWebhook")}
                        </Button>
                      )}
                    </div>
                  </div>

                  {webhookSettings?.webhookUrl ? (
                    <div className="space-y-4">
                      <div>
                        <label style={{ fontSize: "0.8125rem", fontWeight: 600, color: "#94A3B8", display: "block", marginBottom: "0.5rem" }}>
                          {t("services.webhookUrlLabel")}
                        </label>
                        <div className="flex gap-2">
                          <div className="flex-1 px-3 py-2 rounded-lg bg-surface-light/20 border border-border font-mono text-sm select-all break-all">
                            {`${window.location.origin}${webhookSettings.webhookUrl}`}
                          </div>
                          <Button
                            variant="outline"
                            size="icon"
                            className="flex-shrink-0 bg-input-background"
                            onClick={() =>
                              copyToClipboard(`${window.location.origin}${webhookSettings.webhookUrl!}`, "url")
                            }
                          >
                            {copiedField === "url" ? <Check className="w-4 h-4 text-success-500" /> : <Copy className="w-4 h-4" />}
                          </Button>
                          <Button
                            variant="outline"
                            size="icon"
                            className="flex-shrink-0 bg-input-background hover:text-warning-500"
                            onClick={() => {
                              if (confirm(t("services.regenerateTokenConfirm")))
                                regenerateTokenMutation.mutate(id!);
                            }}
                            disabled={regenerateTokenMutation.isPending}
                          >
                            <RefreshCw className={`w-4 h-4 ${regenerateTokenMutation.isPending ? "animate-spin" : ""}`} />
                          </Button>
                        </div>
                        <p style={{ fontSize: "0.75rem", color: "#94A3B8", marginTop: "0.5rem" }}>
                          {t("services.webhookUrlHint")}
                          {!webhookSettings.hasApiKey && !apiKeyPlaintext && " " + t("services.generateApiKeyHint")}
                        </p>

                        {apiKeyPlaintext && (
                          <div className="mt-3 p-3 rounded-lg bg-yellow-500/10 border border-yellow-500/30">
                            <div className="flex items-center justify-between mb-2 gap-2">
                              <p style={{ fontSize: "0.75rem", fontWeight: 600, color: "#F59E0B" }}>
                                {t("services.apiKeyShowOnceWarn")}
                              </p>
                              <Button
                                variant="ghost"
                                size="sm"
                                className="h-6 px-2"
                                onClick={() => copyToClipboard(apiKeyPlaintext, "apiKeyOnce")}
                              >
                                {copiedField === "apiKeyOnce"
                                  ? <Check className="w-3 h-3 text-success-500" />
                                  : <Copy className="w-3 h-3" />}
                              </Button>
                            </div>
                            <code className="block px-2 py-1 rounded bg-input-background text-xs break-all">
                              <span className="text-muted-foreground">X-Callu-Api-Key: </span>
                              {apiKeyPlaintext}
                            </code>
                          </div>
                        )}

                        <div className="mt-3 flex flex-wrap gap-2">
                          <Button
                            variant="outline"
                            size="sm"
                            className="bg-input-background"
                            onClick={async () => {
                              const msg = webhookSettings?.hasApiKey
                                ? t("services.regenerateApiKeyConfirm")
                                : t("services.generateApiKeyConfirm");
                              if (!confirm(msg)) return;
                              const resp = await regenerateApiKeyMutation.mutateAsync(id!);
                              if (resp?.apiKey) setApiKeyPlaintext(resp.apiKey);
                            }}
                            disabled={regenerateApiKeyMutation.isPending}
                          >
                            {regenerateApiKeyMutation.isPending ? (
                              <><div className="w-4 h-4 border-2 border-white/30 border-t-white rounded-full animate-spin mr-2" /> {t("services.generating")}</>
                            ) : (
                              <><Key className="w-4 h-4 mr-2" /> {webhookSettings?.hasApiKey ? t("services.regenerate") : t("services.generate")} {t("services.apiKey")}</>
                            )}
                          </Button>
                          <Button
                            variant="outline"
                            size="sm"
                            className="bg-input-background"
                            onClick={() => setShowHowToCall((v) => !v)}
                          >
                            <Code className="w-4 h-4 mr-2" />
                            {showHowToCall ? t("services.hideHowToCall") : t("services.showHowToCall")}
                          </Button>
                        </div>

                        {showHowToCall && (
                          <div className="mt-4 p-4 rounded-lg bg-surface-light/10 border border-border space-y-3">
                            <div>
                              <p style={{ fontSize: "0.8125rem", fontWeight: 600, marginBottom: "0.25rem" }}>
                                {t("services.howToCallTitle")}
                              </p>
                              <p style={{ fontSize: "0.75rem", color: "#94A3B8" }}>
                                {t("services.howToCallBody")}
                              </p>
                            </div>
                            <pre className="text-xs font-mono p-3 rounded bg-input-background border border-border overflow-x-auto whitespace-pre">
{`curl -X POST '${window.location.origin}${webhookSettings.webhookUrl}' \\
  -H 'Content-Type: application/json' \\${webhookSettings.hasApiKey || apiKeyPlaintext ? `
  -H 'X-Callu-Api-Key: ${apiKeyPlaintext ?? "<API_KEY>"}' \\` : ""}${webhookSettings.hasSignatureSecret ? `
  -H '${webhookSettings.signatureHeaderName ?? "X-Callu-Signature"}: sha256=<HMAC_HEX>' \\` : ""}
  -d '{"alert":"example","severity":"high"}'`}
                            </pre>
                            {webhookSettings.hasSignatureSecret && (
                              <div className="text-xs text-muted-foreground space-y-1">
                                <p style={{ fontWeight: 600, color: "#94A3B8" }}>{t("services.howToHmacTitle")}</p>
                                <p>{t("services.howToHmacBody")}</p>
                                <pre className="text-xs font-mono p-3 rounded bg-input-background border border-border overflow-x-auto whitespace-pre">
{`HMAC_HEX=$(printf '%s' "$BODY" | openssl dgst -sha256 -hmac "$SECRET" | awk '{print $2}')`}
                                </pre>
                              </div>
                            )}
                          </div>
                        )}
                      </div>
                    </div>
                  ) : (
                    <div className="text-center py-8">
                      <Globe className="w-12 h-12 text-muted-foreground mx-auto mb-3 opacity-50" />
                      <p style={{ fontSize: "0.9375rem", fontWeight: 600, marginBottom: "0.5rem" }}>
                        No webhook configured
                      </p>
                      <p style={{ fontSize: "0.8125rem", color: "#94A3B8", marginBottom: "1rem" }}>
                        Enable listening mode below to start receiving webhooks
                      </p>
                    </div>
                  )}
                </Card>

                <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
                  <div className="flex items-center gap-2 mb-4">
                    <Key className="w-5 h-5 text-brand-500" />
                    <h3 style={{ fontSize: "1.125rem", fontWeight: 600 }}>HMAC Signature</h3>
                    {webhookSettings?.hasSignatureSecret && (
                      <Badge className="bg-success-500/10 text-success-500 border border-success-500/20 ml-2">
                        Configured
                      </Badge>
                    )}
                  </div>
                  <p style={{ fontSize: "0.8125rem", color: "#94A3B8", marginBottom: "1rem" }}>
                    {t("services.hmacExplain")}
                  </p>

                  {signaturePlaintext && (
                    <div className="mb-4 p-3 rounded-lg bg-yellow-500/10 border border-yellow-500/30">
                      <p style={{ fontSize: "0.75rem", fontWeight: 600, color: "#F59E0B", marginBottom: "0.5rem" }}>
                        Copy this secret now — it will not be shown again
                      </p>
                      <code className="block px-2 py-1 rounded bg-input-background text-xs break-all">
                        {signaturePlaintext}
                      </code>
                    </div>
                  )}

                  {!webhookSettings?.hasSignatureSecret ? (
                    <div className="space-y-2">
                      <input
                        type="text"
                        className="w-full px-3 py-2 rounded-lg bg-input-background border border-border text-sm font-mono"
                        placeholder="Paste or generate a 32+ char secret"
                        value={signatureForm.secret}
                        onChange={(e) => setSignatureForm((f) => ({ ...f, secret: e.target.value }))}
                      />
                      <input
                        type="text"
                        className="w-full px-3 py-2 rounded-lg bg-input-background border border-border text-sm font-mono"
                        placeholder="Header name (default X-Callu-Signature)"
                        value={signatureForm.headerName}
                        onChange={(e) => setSignatureForm((f) => ({ ...f, headerName: e.target.value }))}
                      />
                      <div className="flex gap-2">
                        <Button
                          variant="outline"
                          size="sm"
                          onClick={() => {
                            const bytes = new Uint8Array(36);
                            crypto.getRandomValues(bytes);
                            const generated = btoa(String.fromCharCode(...bytes))
                              .replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
                            setSignatureForm((f) => ({ ...f, secret: generated }));
                          }}
                        >
                          <RefreshCw className="w-3 h-3 mr-1" /> Generate
                        </Button>
                        <Button
                          size="sm"
                          disabled={signatureForm.secret.length < 32 || setSignatureMutation.isPending}
                          onClick={async () => {
                            const resp = await setSignatureMutation.mutateAsync({
                              serviceId: id!,
                              body: {
                                secret: signatureForm.secret,
                                headerName: signatureForm.headerName || undefined,
                              },
                            });
                            if (resp) {
                              setSignaturePlaintext(resp.secret);
                              setSignatureForm({ secret: "", headerName: "X-Callu-Signature" });
                            }
                          }}
                        >
                          {setSignatureMutation.isPending ? "Saving…" : "Set Signature"}
                        </Button>
                      </div>
                    </div>
                  ) : (
                    <div className="flex items-center justify-between gap-3">
                      <div>
                        <p style={{ fontSize: "0.875rem", marginBottom: "0.25rem" }}>
                          Header: <code className="px-1.5 py-0.5 rounded bg-input-background text-xs">{webhookSettings.signatureHeaderName}</code>
                        </p>
                        <p style={{ fontSize: "0.75rem", color: "#64748B" }}>
                          Secret hidden. Clear and re-create to rotate.
                        </p>
                      </div>
                      <Button
                        variant="outline"
                        size="sm"
                        className="text-error-400 hover:text-error-500"
                        disabled={clearSignatureMutation.isPending}
                        onClick={() => {
                          if (confirm("Clear the HMAC signature secret? Inbound webhooks will no longer require a signature.")) {
                            clearSignatureMutation.mutate(id!);
                            setSignaturePlaintext(null);
                          }
                        }}
                      >
                        Clear
                      </Button>
                    </div>
                  )}
                </Card>

                <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
                  <div className="flex items-center justify-between mb-4">
                    <div className="flex items-center gap-2">
                      <Eye className="w-5 h-5 text-brand-500" />
                      <h3 style={{ fontSize: "1.125rem", fontWeight: 600 }}>{t("services.listeningMode")}</h3>
                    </div>
                    <Switch
                      checked={webhookSettings?.listeningMode ?? false}
                      onCheckedChange={(checked) => {
                        toggleListeningModeMutation.mutate({ serviceId: id!, enabled: checked });
                      }}
                      disabled={toggleListeningModeMutation.isPending}
                    />
                  </div>
                  <p style={{ fontSize: "0.8125rem", color: "#94A3B8", marginBottom: "1rem" }}>
                    {t("services.listeningModeDescription")}
                  </p>
                  {webhookSettings?.listeningMode && (
                    <div className="space-y-3">
                      <div className="p-3 rounded-lg bg-success-500/5 border border-success-500/20">
                        <div className="flex items-center justify-between">
                          <div className="flex items-center gap-2">
                            <div className="w-2 h-2 bg-success-500 rounded-full animate-pulse" />
                            <span style={{ fontSize: "0.8125rem", fontWeight: 600, color: "#22C55E" }}>
                              {t("services.listeningForWebhooks")}
                            </span>
                          </div>
                          <Badge className="bg-brand-500/10 text-brand-500 border-brand-500/20 border text-xs tabular-nums">
                            {webhookSettings.capturedCount} {t("services.captured")}
                          </Badge>
                        </div>
                      </div>
                      {webhookSettings.capturedCount > 0 && (
                        <Link to={`/services/${id}/captures`}>
                          <Button variant="outline" size="sm" className="w-full bg-input-background">
                            <Radio className="w-4 h-4 mr-2" />
                            {t("services.viewCapturedWebhooks")} ({webhookSettings.capturedCount})
                            <ExternalLink className="w-3 h-3 ml-auto" />
                          </Button>
                        </Link>
                      )}
                    </div>
                  )}
                </Card>

                <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
                  <div className="flex items-center gap-2 mb-4">
                    <Code className="w-5 h-5 text-brand-500" />
                    <h3 style={{ fontSize: "1.125rem", fontWeight: 600 }}>{t("services.webhookTemplate")}</h3>
                  </div>
                  {webhookSettings?.templateName ? (
                    <div className="space-y-4">
                      <div className="flex items-center justify-between p-3 rounded-lg bg-surface-light/20 border border-border">
                        <div className="flex items-center gap-2">
                          <Code className="w-4 h-4 text-brand-500" />
                          <span style={{ fontSize: "0.875rem", fontWeight: 600 }}>{webhookSettings.templateName}</span>
                        </div>
                        <Badge className="bg-brand-500/10 text-brand-500 border-brand-500/20 border text-xs">
                          {t("common.active")}
                        </Badge>
                      </div>
                      <Link to={`/services/${id}/template`}>
                        <Button variant="outline" className="w-full bg-input-background">
                          <Code className="w-4 h-4 mr-2" /> {t("services.editTemplate")}
                          <ExternalLink className="w-3 h-3 ml-auto" />
                        </Button>
                      </Link>
                    </div>
                  ) : (
                    <div className="space-y-4">
                      <div className="text-center py-4">
                        <Code className="w-8 h-8 text-muted-foreground mx-auto mb-2 opacity-50" />
                        <p style={{ fontSize: "0.8125rem", color: "#94A3B8" }}>
                          {t("services.noTemplateConfigured")}
                        </p>
                      </div>
                      <Link to={`/services/${id}/template`}>
                        <Button className="w-full bg-brand-500 hover:bg-brand-600 text-white">
                          <Plus className="w-4 h-4 mr-2" /> {t("services.createTemplate")}
                        </Button>
                      </Link>
                    </div>
                  )}

                  <div className="mt-4 border-t border-border pt-4">
                    <label
                      htmlFor="wh-template"
                      className="mb-2 block text-xs font-semibold text-muted-foreground"
                    >
                      {t("services.useExistingTemplate")}
                    </label>
                    <Select
                      value={webhookSettings?.templateId ?? "__none__"}
                      disabled={setTemplateMutation.isPending}
                      onValueChange={(value) =>
                        setTemplateMutation.mutate({
                          serviceId: id!,
                          templateId: value === "__none__" ? null : value,
                        })
                      }
                    >
                      <SelectTrigger id="wh-template" aria-label={t("services.templatePickerAria")} className="bg-input-background">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="__none__">{t("services.templateNone")}</SelectItem>
                        {(allTemplates ?? []).map((template) => (
                          <SelectItem key={template.id} value={template.id}>
                            {template.name}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  </div>
                </Card>

                <Card className="p-6 bg-card/80 backdrop-blur-sm border-border lg:col-span-2">
                  <div className="flex items-center gap-2 mb-4">
                    <Activity className="w-5 h-5 text-brand-500" />
                    <h3 style={{ fontSize: "1.125rem", fontWeight: 600 }}>{t("services.webhookStatistics")}</h3>
                  </div>
                  <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
                    <div className="p-4 rounded-lg bg-surface-light/20 border border-border text-center">
                      <p style={{ fontSize: "1.5rem", fontWeight: 700 }}>{webhookSettings?.webhooksReceivedCount ?? 0}</p>
                      <p style={{ fontSize: "0.75rem", color: "#94A3B8" }}>{t("services.totalReceived")}</p>
                    </div>
                    <div className="p-4 rounded-lg bg-surface-light/20 border border-border text-center">
                      <p style={{ fontSize: "1.5rem", fontWeight: 700 }}>{webhookSettings?.capturedCount ?? 0}</p>
                      <p style={{ fontSize: "0.75rem", color: "#94A3B8" }}>{t("services.captured")}</p>
                    </div>
                    <div className="p-4 rounded-lg bg-surface-light/20 border border-border text-center">
                      <p style={{ fontSize: "1.5rem", fontWeight: 700 }}>{incidentCount}</p>
                      <p style={{ fontSize: "0.75rem", color: "#94A3B8" }}>{t("services.incidents")}</p>
                    </div>
                    <div className="p-4 rounded-lg bg-surface-light/20 border border-border text-center">
                      <p style={{ fontSize: "0.875rem", fontWeight: 600 }}>{formatRelativeTime(webhookSettings?.lastWebhookReceivedAt)}</p>
                      <p style={{ fontSize: "0.75rem", color: "#94A3B8" }}>{t("services.lastReceived")}</p>
                    </div>
                  </div>
                  {webhookSettings && webhookSettings.capturedCount > 0 && (
                    <div className="mt-4">
                      <Link to={`/services/${id}/captures`}>
                        <Button variant="outline" className="w-full bg-input-background">
                          <Radio className="w-4 h-4 mr-2" /> {t("services.viewAllCaptures")}
                          <ExternalLink className="w-3 h-3 ml-auto" />
                        </Button>
                      </Link>
                    </div>
                  )}
                </Card>
              </div>
            )}
            <DeleteConfirmDialog
              open={isDisabling}
              onOpenChange={setIsDisabling}
              title={t("services.disableWebhookTitle")}
              message={t("services.disableWebhookMsg")}
              warning={t("services.disableWebhookWarn")}
              confirmLabel={t("services.disableWebhook")}
              isLoading={disableWebhookMutation.isPending}
              onConfirm={() =>
                disableWebhookMutation.mutate(id!, { onSettled: () => setIsDisabling(false) })
              }
            />

          </TabsContent>
  );
}
