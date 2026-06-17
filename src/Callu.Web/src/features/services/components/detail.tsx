import { useState, useEffect, useRef } from "react";
import { t } from "@/shared/locales/i18n";
import { Link, useParams, useNavigate } from "react-router";
import { Button } from "@/shared/components/ui/button";
import { Badge } from "@/shared/components/ui/badge";
import { Card } from "@/shared/components/ui/card";
import { Input } from "@/shared/components/ui/input";
import { Textarea } from "@/shared/components/ui/textarea";
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
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from "@/shared/components/ui/dialog";
import { Tabs, TabsList, TabsTrigger, TabsContent } from "@/shared/components/ui/tabs";
import { Switch } from "@/shared/components/ui/switch";
import {
  Server,
  ChevronRight,
  Home,
  Save,
  Trash2,
  Activity,
  Webhook,
  AlertCircle,
  Code,
  Radio,
  ExternalLink,
  Loader2,
  Plus,
  Link2,
  X,
  Copy,
  Check,
  Key,
  RefreshCw,
  Globe,
  Eye,
  Settings,
  Send,
} from "lucide-react";
import {
  useService,
  useUpdateService,
  useDeleteService,
  useServiceDependencies,
  useAddDependency,
  useRemoveDependency,
  useServices,
} from "../hooks/use-services";
import { useTeams } from "@/features/teams/hooks/use-teams";
import {
  useWebhookSettings,
  useRegenerateToken,
  useRegenerateApiKey,
  useToggleListeningMode,
  useSetSignature,
  useClearSignature,
} from "../hooks/use-webhook-settings";
import type { ServiceDependencyDto } from "../types/service.types";

function getStatusBadge(status: string) {
  const s = String(status).toLowerCase();
  if (s === "operational") return { class: "bg-success-500/10 text-success-500 border-success-500/20", label: t("services.statusOperational") };
  if (s.includes("degraded")) return { class: "bg-warning-500/10 text-warning-500 border-warning-500/20", label: t("services.statusDegraded") };
  if (s.includes("partial")) return { class: "bg-warning-500/10 text-warning-500 border-warning-500/20", label: t("services.statusPartialOutage") };
  if (s.includes("major")) return { class: "bg-error-500/10 text-error-500 border-error-500/20", label: t("services.statusMajorOutage") };
  if (s.includes("maintenance")) return { class: "bg-blue-400/10 text-blue-400 border-blue-400/20", label: t("services.statusMaintenance") };
  return { class: "bg-muted/10 text-muted-foreground border-muted/20", label: status };
}

function getCriticalityBadge(crit: string) {
  const c = crit.toLowerCase();
  if (c === "critical") return "bg-error-500/10 text-error-500 border-error-500/20";
  if (c === "high") return "bg-warning-500/10 text-warning-500 border-warning-500/20";
  if (c === "medium") return "bg-blue-400/10 text-blue-400 border-blue-400/20";
  return "bg-muted/10 text-muted-foreground border-muted/20";
}

function formatRelativeTime(dateStr?: string) {
  if (!dateStr) return t("services.never");
  const diff = Date.now() - new Date(dateStr).getTime();
  const mins = Math.floor(diff / 60000);
  if (mins < 1) return t("services.justNow");
  if (mins < 60) return `${mins}m ago`;
  const hours = Math.floor(mins / 60);
  if (hours < 24) return `${hours}h ago`;
  const days = Math.floor(hours / 24);
  return `${days}d ago`;
}

export function ServiceDetail() {
  const { id } = useParams();
  const navigate = useNavigate();

  const [activeTab, setActiveTab] = useState("overview");
  const [webhookListeningActive, setWebhookListeningActive] = useState(false);
  const prevCaptureCountRef = useRef<number>(0);

  const { data: service, isLoading, error } = useService(id!);
  const { data: dependencies } = useServiceDependencies(id!);
  const { data: allServices } = useServices();
  const { data: teams } = useTeams();
  const { data: webhookSettings, isLoading: isWebhookLoading } = useWebhookSettings(id!, webhookListeningActive);
  const updateServiceMutation = useUpdateService();
  const deleteServiceMutation = useDeleteService();
  const addDependencyMutation = useAddDependency();
  const removeDependencyMutation = useRemoveDependency();
  const regenerateTokenMutation = useRegenerateToken();
  const regenerateApiKeyMutation = useRegenerateApiKey();
  const toggleListeningModeMutation = useToggleListeningMode();
  const setSignatureMutation = useSetSignature();
  const clearSignatureMutation = useClearSignature();

  const [signatureForm, setSignatureForm] = useState({ secret: "", headerName: "X-Callu-Signature" });
  const [signaturePlaintext, setSignaturePlaintext] = useState<string | null>(null);

  const [apiKeyPlaintext, setApiKeyPlaintext] = useState<string | null>(null);
  const [showHowToCall, setShowHowToCall] = useState(false);

  const [isDeleteModalOpen, setIsDeleteModalOpen] = useState(false);
  const [isDepModalOpen, setIsDepModalOpen] = useState(false);
  const [copiedField, setCopiedField] = useState<string | null>(null);

  useEffect(() => {
    if (webhookSettings?.listeningMode !== undefined) {
      setWebhookListeningActive(webhookSettings.listeningMode);
    }
  }, [webhookSettings?.listeningMode]);

  useEffect(() => {
    if (webhookSettings?.capturedCount !== undefined) {
      prevCaptureCountRef.current = webhookSettings.capturedCount;
    }
  }, [webhookSettings?.capturedCount]);

  const [serviceName, setServiceName] = useState("");
  const [description, setDescription] = useState("");
  const [serviceType, setServiceType] = useState("Api");
  const [environment, setEnvironment] = useState("production");
  const [status, setStatus] = useState("Operational");
  const [selectedTeamId, setSelectedTeamId] = useState<string>("");

  const [ackEnabled, setAckEnabled] = useState(false);
  const [ackUrl, setAckUrl] = useState("");
  const [ackHttpMethod, setAckHttpMethod] = useState("POST");
  const [ackContentType, setAckContentType] = useState("application/json");
  const [ackHeaders, setAckHeaders] = useState<{ key: string; value: string }[]>([]);
  const [ackPayloadTemplate, setAckPayloadTemplate] = useState("");

  const [depServiceId, setDepServiceId] = useState("");
  const [depType, setDepType] = useState("Upstream");
  const [depCriticality, setDepCriticality] = useState("High");
  const [depDescription, setDepDescription] = useState("");

  useEffect(() => {
    if (service) {
      setServiceName(service.name);
      setDescription(service.description ?? "");
      setServiceType(service.type || "Api");
      setEnvironment(service.environment || "production");
      setStatus(String(service.status) || "Operational");
      setSelectedTeamId(service.teamId ?? "");
      setAckEnabled(service.ackEnabled ?? false);
      setAckUrl(service.ackUrl ?? "");
      setAckHttpMethod(service.ackHttpMethod || "POST");
      setAckContentType(service.ackContentType || "application/json");
      setAckPayloadTemplate(service.ackPayloadTemplate ?? "");
      try {
        const parsed = service.ackHeaders ? JSON.parse(service.ackHeaders) : {};
        setAckHeaders(Object.entries(parsed).map(([k, v]) => ({ key: k, value: String(v) })));
      } catch {
        setAckHeaders([]);
      }
    }
  }, [service]);

  const handleSave = () => {
    if (!id) return;
    const headersObj: Record<string, string> = {};
    ackHeaders.forEach(h => { if (h.key.trim()) headersObj[h.key.trim()] = h.value; });
    const headersJson = Object.keys(headersObj).length > 0 ? JSON.stringify(headersObj) : undefined;

    updateServiceMutation.mutate({
      id,
      data: {
        name: serviceName,
        description: description || undefined,
        type: serviceType,
        environment,
        status,
        teamId: selectedTeamId || undefined,
        ackEnabled,
        ackUrl: ackUrl || undefined,
        ackHttpMethod,
        ackContentType,
        ackHeaders: headersJson,
        ackPayloadTemplate: ackPayloadTemplate || undefined,
      },
    });
  };

  const handleDelete = () => {
    if (!id) return;
    deleteServiceMutation.mutate(id, {
      onSuccess: () => {
        setIsDeleteModalOpen(false);
        navigate("/services");
      },
    });
  };

  const handleAddDependency = () => {
    if (!id || !depServiceId) return;
    addDependencyMutation.mutate(
      {
        serviceId: id,
        dependsOnServiceId: depServiceId,
        type: depType,
        criticality: depCriticality,
        description: depDescription || undefined,
      },
      {
        onSuccess: () => {
          setIsDepModalOpen(false);
          setDepServiceId("");
          setDepDescription("");
        },
      },
    );
  };

  const handleRemoveDependency = (depId: string) => {
    removeDependencyMutation.mutate(depId);
  };

  const copyToClipboard = (text: string, field: string) => {
    navigator.clipboard.writeText(text);
    setCopiedField(field);
    setTimeout(() => setCopiedField(null), 2000);
  };

  const availableForDep = (allServices ?? []).filter(
    (s) => s.id !== id && !(dependencies ?? []).some((d: ServiceDependencyDto) => d.dependsOnServiceId === s.id),
  );

  if (isLoading) {
    return (
      <div className="p-6 flex items-center justify-center min-h-[60vh]">
        <div className="text-center">
          <Loader2 className="w-8 h-8 animate-spin text-brand-500 mx-auto mb-3" />
          <p style={{ fontSize: "0.875rem", color: "#94A3B8" }}>{t("services.loadingService")}</p>
        </div>
      </div>
    );
  }

  if (error || !service) {
    return (
      <div className="p-6 flex items-center justify-center min-h-[60vh]">
        <div className="text-center">
          <AlertCircle className="w-8 h-8 text-error-500 mx-auto mb-3" />
          <p style={{ fontSize: "1.125rem", fontWeight: 600, marginBottom: "0.5rem" }}>{t("services.failedToLoadService")}</p>
          <p style={{ fontSize: "0.875rem", color: "#94A3B8" }}>
            {error instanceof Error ? error.message : t("services.serviceNotFound")}
          </p>
          <Button variant="outline" onClick={() => navigate("/services")} className="mt-4">
            {t("services.backToServices")}
          </Button>
        </div>
      </div>
    );
  }

  const statusInfo = getStatusBadge(service.status);

  return (
    <>
      <div className="p-6 space-y-6">
        <nav className="flex items-center gap-2 text-sm">
          <Link to="/dashboard" className="text-muted-foreground hover:text-foreground transition-colors">
            <Home className="w-4 h-4" />
          </Link>
          <ChevronRight className="w-4 h-4 text-muted-foreground" />
          <Link to="/services" className="text-muted-foreground hover:text-foreground transition-colors">
            {t("common.services")}
          </Link>
          <ChevronRight className="w-4 h-4 text-muted-foreground" />
          <span className="text-foreground font-medium">{serviceName}</span>
        </nav>

        <div className="flex flex-col sm:flex-row sm:items-start sm:justify-between gap-4">
          <div className="flex items-start gap-3">
            <div className="w-12 h-12 rounded-lg bg-brand-500/10 flex items-center justify-center flex-shrink-0">
              <Server className="w-6 h-6 text-brand-500" />
            </div>
            <div>
              <div className="flex items-center gap-2 mb-1">
                <h1 style={{ fontSize: "1.875rem", fontWeight: 600 }}>{serviceName}</h1>
                <Badge className={`${statusInfo.class} border`}>{statusInfo.label}</Badge>
              </div>
              <p style={{ fontSize: "0.875rem", color: "#94A3B8" }}>
                {description || t("services.noDescription")}
              </p>
            </div>
          </div>
          <div className="flex gap-2">
            <Button
              onClick={handleSave}
              disabled={updateServiceMutation.isPending || !serviceName.trim()}
              className="bg-brand-500 hover:bg-brand-600 text-white"
            >
              {updateServiceMutation.isPending ? (
                <>
                  <div className="w-4 h-4 border-2 border-white/30 border-t-white rounded-full animate-spin mr-2" />
                  {t("common.saving")}
                </>
              ) : (
                <>
                  <Save className="w-4 h-4 mr-2" />
                  {t("services.saveChanges")}
                </>
              )}
            </Button>
            <Button
              variant="outline"
              onClick={() => setIsDeleteModalOpen(true)}
              className="bg-input-background hover:bg-error-500/10 hover:text-error-500"
            >
              <Trash2 className="w-4 h-4 mr-2" />
              {t("common.delete")}
            </Button>
          </div>
        </div>

        <Tabs value={activeTab} onValueChange={setActiveTab}>
          <TabsList className="bg-card/80 backdrop-blur-sm border border-border">
            <TabsTrigger value="overview">
              <Activity className="w-4 h-4 mr-2" />
              {t("services.tabOverview")}
            </TabsTrigger>
            <TabsTrigger value="webhooks">
              <Webhook className="w-4 h-4 mr-2" />
              {t("services.tabWebhooks")}
            </TabsTrigger>
            <TabsTrigger value="dependencies">
              <Link2 className="w-4 h-4 mr-2" />
              {t("services.tabDependencies")}
            </TabsTrigger>
            <TabsTrigger value="ack-settings">
              <Send className="w-4 h-4 mr-2" />
              {t("services.tabAckSettings")}
            </TabsTrigger>
          </TabsList>

          <TabsContent value="overview" className="space-y-6">
            <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
              <div className="lg:col-span-2 space-y-6">
                <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
                  <h3 style={{ fontSize: "1.125rem", fontWeight: 600, marginBottom: "1rem" }}>
                    {t("services.serviceConfiguration")}
                  </h3>
                  <div className="space-y-4">
                    <div>
                      <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                        {t("services.serviceName")}
                      </label>
                      <Input value={serviceName} onChange={(e) => setServiceName(e.target.value)} className="bg-input-background" />
                    </div>
                    <div>
                      <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                        {t("common.description")}
                      </label>
                      <Textarea value={description} onChange={(e) => setDescription(e.target.value)} rows={3} className="bg-input-background resize-none" />
                    </div>
                    <div className="grid grid-cols-3 gap-4">
                      <div>
                        <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>{t("services.type")}</label>
                        <Select value={serviceType} onValueChange={setServiceType}>
                          <SelectTrigger className="bg-input-background"><SelectValue /></SelectTrigger>
                          <SelectContent>
                            <SelectItem value="Api">API</SelectItem>
                            <SelectItem value="Website">Website</SelectItem>
                            <SelectItem value="Database">Database</SelectItem>
                            <SelectItem value="Server">Server</SelectItem>
                            <SelectItem value="Queue">Queue</SelectItem>
                            <SelectItem value="Cache">Cache</SelectItem>
                            <SelectItem value="Cdn">CDN</SelectItem>
                            <SelectItem value="Storage">Storage</SelectItem>
                            <SelectItem value="Email">Email</SelectItem>
                            <SelectItem value="ThirdParty">Third Party</SelectItem>
                            <SelectItem value="Other">Other</SelectItem>
                          </SelectContent>
                        </Select>
                      </div>
                      <div>
                        <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>{t("services.environment")}</label>
                        <Select value={environment} onValueChange={setEnvironment}>
                          <SelectTrigger className="bg-input-background"><SelectValue /></SelectTrigger>
                          <SelectContent>
                            <SelectItem value="production">Production</SelectItem>
                            <SelectItem value="staging">Staging</SelectItem>
                            <SelectItem value="development">Development</SelectItem>
                          </SelectContent>
                        </Select>
                      </div>
                      <div>
                        <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>{t("services.status")}</label>
                        <Select value={status} onValueChange={setStatus}>
                          <SelectTrigger className="bg-input-background"><SelectValue /></SelectTrigger>
                          <SelectContent>
                            <SelectItem value="Operational">Operational</SelectItem>
                            <SelectItem value="DegradedPerformance">Degraded</SelectItem>
                            <SelectItem value="PartialOutage">Partial Outage</SelectItem>
                            <SelectItem value="MajorOutage">Major Outage</SelectItem>
                            <SelectItem value="UnderMaintenance">Maintenance</SelectItem>
                          </SelectContent>
                        </Select>
                      </div>
                    </div>
                    <div>
                      <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                        {t("services.team")}
                      </label>
                      <Select value={selectedTeamId || "__none__"} onValueChange={(v) => setSelectedTeamId(v === "__none__" ? "" : v)}>
                        <SelectTrigger className="bg-input-background">
                          <SelectValue placeholder={t("services.selectTeamDropdownPlaceholder")} />
                        </SelectTrigger>
                        <SelectContent>
                          <SelectItem value="__none__">No team</SelectItem>
                          {(teams ?? []).filter(team => team.id).map((team) => (
                            <SelectItem key={team.id} value={team.id}>{team.name}</SelectItem>
                          ))}
                        </SelectContent>
                      </Select>
                      <p style={{ fontSize: "0.75rem", color: "#94A3B8", marginTop: "0.25rem" }}>
                        Assign a team to enable automatic escalation when incidents are created via webhooks.
                      </p>
                    </div>
                  </div>
                </Card>
              </div>

              <div className="space-y-6">
                <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
                  <h3 style={{ fontSize: "1.125rem", fontWeight: 600, marginBottom: "1rem" }}>{t("services.serviceHealth")}</h3>
                  <div className="space-y-4">
                    <div>
                      <div className="flex items-center justify-between mb-2">
                        <span style={{ fontSize: "0.875rem", color: "#94A3B8" }}>{t("services.uptime30d")}</span>
                        <span style={{ fontSize: "0.875rem", fontWeight: 600 }}>{service.uptime.toFixed(1)}%</span>
                      </div>
                      <div className="w-full h-2 bg-muted/20 rounded-full overflow-hidden">
                        <div
                          className="h-full bg-success-500 rounded-full transition-all"
                          style={{ width: `${Math.min(service.uptime, 100)}%` }}
                        />
                      </div>
                    </div>
                    <div className="flex items-center justify-between">
                      <span style={{ fontSize: "0.875rem", color: "#94A3B8" }}>{t("services.activeIncidents")}</span>
                      <span
                        style={{ fontSize: "0.875rem", fontWeight: 600 }}
                        className={service.incidentCount > 0 ? "text-error-500" : ""}
                      >
                        {service.incidentCount}
                      </span>
                    </div>
                    {service.teamName && (
                      <div className="flex items-center justify-between">
                        <span style={{ fontSize: "0.875rem", color: "#94A3B8" }}>{t("services.team")}</span>
                        <span style={{ fontSize: "0.875rem", fontWeight: 600 }}>{service.teamName}</span>
                      </div>
                    )}
                    <div className="flex items-center justify-between">
                      <span style={{ fontSize: "0.875rem", color: "#94A3B8" }}>{t("services.tabDependencies")}</span>
                      <span style={{ fontSize: "0.875rem", fontWeight: 600 }}>
                        {(dependencies ?? []).length}
                      </span>
                    </div>
                    {webhookSettings && (
                      <div className="flex items-center justify-between">
                        <span style={{ fontSize: "0.875rem", color: "#94A3B8" }}>{t("services.webhooksReceived")}</span>
                        <span style={{ fontSize: "0.875rem", fontWeight: 600 }}>
                          {webhookSettings.webhooksReceivedCount}
                        </span>
                      </div>
                    )}
                  </div>
                </Card>

                <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
                  <h3 style={{ fontSize: "1.125rem", fontWeight: 600, marginBottom: "1rem" }}>{t("services.quickActions")}</h3>
                  <div className="flex flex-col gap-2">
                    <Button
                      variant="outline"
                      className="w-full justify-start bg-input-background"
                      onClick={() => setActiveTab("webhooks")}
                    >
                      <Settings className="w-4 h-4 mr-2" />
                      {t("services.configureWebhooks")}
                    </Button>
                    <Link to={`/services/${id}/template`} className="block">
                      <Button variant="outline" className="w-full justify-start bg-input-background">
                        <Code className="w-4 h-4 mr-2" />
                        {t("services.editWebhookTemplate")}
                      </Button>
                    </Link>
                    <Link to={`/services/${id}/captures`} className="block">
                      <Button variant="outline" className="w-full justify-start bg-input-background">
                        <Radio className="w-4 h-4 mr-2" />
                        {t("services.viewCaptures")}
                        {webhookSettings && webhookSettings.capturedCount > 0 && (
                          <Badge className="ml-auto bg-brand-500/10 text-brand-500 border-brand-500/20 border">
                            {webhookSettings.capturedCount}
                          </Badge>
                        )}
                      </Button>
                    </Link>
                  </div>
                </Card>
              </div>
            </div>
          </TabsContent>

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
                    <Badge className={webhookSettings?.webhookEnabled
                      ? "bg-success-500/10 text-success-500 border-success-500/20 border"
                      : "bg-muted/10 text-muted-foreground border-muted/20 border"
                    }>
                      {webhookSettings?.webhookEnabled ? t("common.active") : t("common.inactive")}
                    </Badge>
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
                            {apiKeyPlaintext && (
                              <span className="text-warning-400">?apiKey={apiKeyPlaintext}</span>
                            )}
                          </div>
                          <Button
                            variant="outline"
                            size="icon"
                            className="flex-shrink-0 bg-input-background"
                            onClick={() => {
                              const full = `${window.location.origin}${webhookSettings.webhookUrl!}` +
                                (apiKeyPlaintext ? `?apiKey=${apiKeyPlaintext}` : "");
                              copyToClipboard(full, "url");
                            }}
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
{`curl -X POST '${window.location.origin}${webhookSettings.webhookUrl}${webhookSettings.hasApiKey || apiKeyPlaintext ? `?apiKey=${apiKeyPlaintext ?? "<API_KEY>"}` : ""}' \\
  -H 'Content-Type: application/json' \\${webhookSettings.hasSignatureSecret ? `
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
                      <p style={{ fontSize: "1.5rem", fontWeight: 700 }}>{service.incidentCount}</p>
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
          </TabsContent>

          <TabsContent value="dependencies" className="space-y-6">
            <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
              <div className="flex items-center justify-between mb-4">
                <h3 style={{ fontSize: "1.125rem", fontWeight: 600 }}>{t("services.serviceDependencies")}</h3>
                <Button onClick={() => setIsDepModalOpen(true)} className="bg-brand-500 hover:bg-brand-600 text-white">
                  <Plus className="w-4 h-4 mr-2" />
                  {t("services.addDependency")}
                </Button>
              </div>

              {(dependencies ?? []).length > 0 ? (
                <div className="space-y-3">
                  {(dependencies ?? []).map((dep: ServiceDependencyDto) => (
                    <div
                      key={dep.id}
                      className="flex items-center justify-between p-4 rounded-lg bg-surface-light/20 border border-border hover:border-border-light transition-colors"
                    >
                      <div className="flex items-center gap-3 flex-1 min-w-0">
                        <div className="w-10 h-10 rounded-lg bg-brand-500/10 flex items-center justify-center flex-shrink-0">
                          <Link2 className="w-5 h-5 text-brand-500" />
                        </div>
                        <div className="flex-1 min-w-0">
                          <p style={{ fontSize: "0.9375rem", fontWeight: 600 }}>
                            {dep.dependsOnServiceName}
                          </p>
                          {dep.description && (
                            <p style={{ fontSize: "0.8125rem", color: "#94A3B8" }} className="truncate">
                              {dep.description}
                            </p>
                          )}
                        </div>
                      </div>
                      <div className="flex items-center gap-2 flex-shrink-0">
                        <Badge className="bg-muted/10 text-muted-foreground border-muted/20 border text-xs">
                          {dep.type}
                        </Badge>
                        <Badge className={`${getCriticalityBadge(dep.criticality)} border text-xs`}>
                          {dep.criticality}
                        </Badge>
                        <Button
                          size="sm"
                          variant="ghost"
                          onClick={() => handleRemoveDependency(dep.id)}
                          className="text-error-500 hover:bg-error-500/10"
                        >
                          <X className="w-4 h-4" />
                        </Button>
                      </div>
                    </div>
                  ))}
                </div>
              ) : (
                <div className="text-center py-12">
                  <Link2 className="w-12 h-12 text-muted-foreground mx-auto mb-3 opacity-50" />
                  <p style={{ fontSize: "0.9375rem", fontWeight: 600, marginBottom: "0.5rem" }}>
                    {t("services.noDependencies")}
                  </p>
                  <p style={{ fontSize: "0.8125rem", color: "#94A3B8" }}>
                    {t("services.noDependenciesHint")}
                  </p>
                </div>
              )}
            </Card>
          </TabsContent>

          <TabsContent value="ack-settings" className="space-y-6">
            <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
              <div className="lg:col-span-2 space-y-6">
                <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
                  <div className="flex items-center justify-between mb-6">
                    <div>
                      <h3 style={{ fontSize: "1.125rem", fontWeight: 600 }}>{t("services.ackTitle")}</h3>
                      <p style={{ fontSize: "0.8125rem", color: "#94A3B8", marginTop: "0.25rem" }}>
                        {t("services.ackSubtitle")}
                      </p>
                    </div>
                    <Switch checked={ackEnabled} onCheckedChange={setAckEnabled} />
                  </div>

                  {ackEnabled && (
                    <div className="space-y-4">
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
                          <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                            HTTP Method
                          </label>
                          <Select value={ackHttpMethod} onValueChange={setAckHttpMethod}>
                            <SelectTrigger className="bg-input-background"><SelectValue /></SelectTrigger>
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
          </TabsContent>
        </Tabs>
      </div>

      <Dialog open={isDeleteModalOpen} onOpenChange={setIsDeleteModalOpen}>
        <DialogContent className="bg-card border-border sm:max-w-[500px]">
          <DialogHeader>
            <DialogTitle style={{ fontSize: "1.5rem", fontWeight: 600 }}>{t("services.deleteService")}</DialogTitle>
          </DialogHeader>
          <div className="py-4">
            <div className="flex gap-3 mb-4">
              <div className="w-10 h-10 rounded-full bg-error-500/10 flex items-center justify-center flex-shrink-0">
                <AlertCircle className="w-5 h-5 text-error-500" />
              </div>
              <div>
                <p style={{ fontSize: "0.875rem", marginBottom: "0.5rem" }}>
                  {t("services.deleteConfirmation", { name: serviceName })}
                </p>
                <p style={{ fontSize: "0.8125rem", color: "#94A3B8" }}>
                  {t("services.deleteWarning")}
                </p>
              </div>
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setIsDeleteModalOpen(false)} className="bg-input-background">
              {t("common.cancel")}
            </Button>
            <Button onClick={handleDelete} disabled={deleteServiceMutation.isPending} className="bg-error-500 hover:bg-error-600 text-white">
              <Trash2 className="w-4 h-4 mr-2" />
              {deleteServiceMutation.isPending ? t("common.deleting") : t("services.deleteServiceBtn")}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog open={isDepModalOpen} onOpenChange={setIsDepModalOpen}>
        <DialogContent className="bg-card border-border sm:max-w-[500px]">
          <DialogHeader>
            <DialogTitle style={{ fontSize: "1.5rem", fontWeight: 600 }}>{t("services.addDependency")}</DialogTitle>
          </DialogHeader>
          <div className="space-y-4 py-4">
            <div className="space-y-2">
              <label style={{ fontSize: "0.875rem", fontWeight: 600, display: "block" }}>
                {t("services.dependsOn")} <span className="text-error-500">*</span>
              </label>
              <Select value={depServiceId} onValueChange={setDepServiceId}>
                <SelectTrigger className="bg-input-background">
                  <SelectValue placeholder={t("services.selectService")} />
                </SelectTrigger>
                <SelectContent>
                  {availableForDep.map((s) => (
                    <SelectItem key={s.id} value={s.id}>
                      <div className="flex items-center gap-2">
                        <Server className="w-4 h-4" />
                        <span>{s.name}</span>
                      </div>
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>

            <div className="grid grid-cols-2 gap-4">
              <div className="space-y-2">
                <label style={{ fontSize: "0.875rem", fontWeight: 600, display: "block" }}>{t("services.type")}</label>
                <Select value={depType} onValueChange={setDepType}>
                  <SelectTrigger className="bg-input-background"><SelectValue /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value="Upstream">Upstream</SelectItem>
                    <SelectItem value="Downstream">Downstream</SelectItem>
                    <SelectItem value="Bidirectional">Bidirectional</SelectItem>
                  </SelectContent>
                </Select>
              </div>
              <div className="space-y-2">
                <label style={{ fontSize: "0.875rem", fontWeight: 600, display: "block" }}>{t("services.criticality")}</label>
                <Select value={depCriticality} onValueChange={setDepCriticality}>
                  <SelectTrigger className="bg-input-background"><SelectValue /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value="Critical">Critical</SelectItem>
                    <SelectItem value="High">High</SelectItem>
                    <SelectItem value="Medium">Medium</SelectItem>
                    <SelectItem value="Low">Low</SelectItem>
                    <SelectItem value="Optional">Optional</SelectItem>
                  </SelectContent>
                </Select>
              </div>
            </div>

            <div className="space-y-2">
              <label style={{ fontSize: "0.875rem", fontWeight: 600, display: "block" }}>{t("common.description")}</label>
              <Input
                placeholder={t("services.dependencyDescriptionPlaceholder")}
                value={depDescription}
                onChange={(e) => setDepDescription(e.target.value)}
                className="bg-input-background"
              />
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setIsDepModalOpen(false)} className="bg-input-background">
              {t("common.cancel")}
            </Button>
            <Button
              onClick={handleAddDependency}
              disabled={!depServiceId || addDependencyMutation.isPending}
              className="bg-brand-500 hover:bg-brand-600 text-white"
            >
              {addDependencyMutation.isPending ? (
                <>
                  <div className="w-4 h-4 border-2 border-white/30 border-t-white rounded-full animate-spin mr-2" />
                  {t("services.adding")}
                </>
              ) : (
                <>
                  <Plus className="w-4 h-4 mr-2" />
                  {t("services.addDependency")}
                </>
              )}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}
