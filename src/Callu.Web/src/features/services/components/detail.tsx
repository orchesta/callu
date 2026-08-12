import { useState, useEffect, useRef, useId } from "react";
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
  Loader2,
  Plus,
  Link2,
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
import { useWebhookSettings } from "../hooks/use-webhook-settings";
import type { ServiceDependencyDto } from "../types/service.types";
import { getStatusBadge } from "../utils/service-badges";
import { parseAckHeaders, serializeAckHeaders } from "../utils/ack-headers";
import { WebhooksTab } from "./detail/webhooks-tab";
import { DependenciesTab } from "./detail/dependencies-tab";
import { AckSettingsTab } from "./detail/ack-settings-tab";

export function ServiceDetail() {
  const { id } = useParams();
  const navigate = useNavigate();
  const formId = useId();

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

  const [isDeleteModalOpen, setIsDeleteModalOpen] = useState(false);
  const [isDepModalOpen, setIsDepModalOpen] = useState(false);

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
  const [ackEvents, setAckEvents] = useState<number | null>(null);
  const [ackSecret, setAckSecret] = useState("");
  const [ackSecretCleared, setAckSecretCleared] = useState(false);
  const [ackSignatureHeader, setAckSignatureHeader] = useState("");

  const [depServiceId, setDepServiceId] = useState("");
  const [depType, setDepType] = useState("Upstream");
  const [depCriticality, setDepCriticality] = useState("High");
  const [depDescription, setDepDescription] = useState("");

  // Hydrate only when a different service arrives; a same-id background refetch
  // must not wipe in-progress edits.
  const hydratedForId = useRef<string | null>(null);
  useEffect(() => {
    if (service && hydratedForId.current !== service.id) {
      hydratedForId.current = service.id;
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
      setAckHeaders(parseAckHeaders(service.ackHeaders));
      setAckEvents(service.ackEvents ?? null);
      setAckSecret("");
      setAckSecretCleared(false);
      setAckSignatureHeader(service.ackSignatureHeader ?? "");
    }
  }, [service]);

  const handleSave = () => {
    if (!id) return;
    const headersJson = serializeAckHeaders(ackHeaders);

    updateServiceMutation.mutate({
      id,
      data: {
        name: serviceName,
        description,
        type: serviceType,
        environment,
        status,
        teamId: selectedTeamId || undefined,
        ackEnabled,
        ackUrl,
        ackHttpMethod,
        ackContentType,
        // Text fields travel as-is: the API keeps on omitted and clears on empty string.
        ackHeaders: headersJson ?? "",
        ackPayloadTemplate,
        // Untouched checkboxes stay null so the stored value is preserved.
        ackEvents: ackEvents ?? undefined,
        // Secret is tri-state: omitted keeps it, a typed value replaces it, "" clears it.
        ackSecret: ackSecretCleared ? "" : ackSecret || undefined,
        ackSignatureHeader,
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
              {t("serviceActions.tabTitle")}
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
                        <label htmlFor={`${formId}-svc-type`} style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>{t("services.type")}</label>
                        <Select value={serviceType} onValueChange={setServiceType}>
                          <SelectTrigger id={`${formId}-svc-type`} className="bg-input-background"><SelectValue /></SelectTrigger>
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
                        <label htmlFor={`${formId}-svc-environment`} style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>{t("services.environment")}</label>
                        <Select value={environment} onValueChange={setEnvironment}>
                          <SelectTrigger id={`${formId}-svc-environment`} className="bg-input-background"><SelectValue /></SelectTrigger>
                          <SelectContent>
                            <SelectItem value="production">Production</SelectItem>
                            <SelectItem value="staging">Staging</SelectItem>
                            <SelectItem value="development">Development</SelectItem>
                          </SelectContent>
                        </Select>
                      </div>
                      <div>
                        <label htmlFor={`${formId}-svc-status`} style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>{t("services.status")}</label>
                        <Select value={status} onValueChange={setStatus}>
                          <SelectTrigger id={`${formId}-svc-status`} className="bg-input-background"><SelectValue /></SelectTrigger>
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
                      <label htmlFor={`${formId}-svc-team`} style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                        {t("services.team")}
                      </label>
                      <Select value={selectedTeamId || "__none__"} onValueChange={(v) => setSelectedTeamId(v === "__none__" ? "" : v)}>
                        <SelectTrigger id={`${formId}-svc-team`} className="bg-input-background">
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
                        <span style={{ fontSize: "0.875rem", fontWeight: 600 }}>
                          {service.uptime === null ? "—" : `${service.uptime.toFixed(1)}%`}
                        </span>
                      </div>
                      <div className="w-full h-2 bg-muted/20 rounded-full overflow-hidden">
                        <div
                          className="h-full bg-success-500 rounded-full transition-all"
                          style={{ width: `${Math.min(service.uptime ?? 0, 100)}%` }}
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

          <WebhooksTab
            serviceId={id!}
            webhookSettings={webhookSettings}
            isWebhookLoading={isWebhookLoading}
            incidentCount={service.incidentCount}
          />

          <DependenciesTab
            dependencies={dependencies}
            onAddClick={() => setIsDepModalOpen(true)}
            onRemove={handleRemoveDependency}
          />

          <AckSettingsTab
            formId={formId}
            serviceId={id!}
            ackEnabled={ackEnabled}
            setAckEnabled={setAckEnabled}
            ackUrl={ackUrl}
            setAckUrl={setAckUrl}
            ackHttpMethod={ackHttpMethod}
            setAckHttpMethod={setAckHttpMethod}
            ackContentType={ackContentType}
            setAckContentType={setAckContentType}
            ackHeaders={ackHeaders}
            setAckHeaders={setAckHeaders}
            ackPayloadTemplate={ackPayloadTemplate}
            setAckPayloadTemplate={setAckPayloadTemplate}
            ackEvents={ackEvents}
            setAckEvents={setAckEvents}
            ackSecret={ackSecret}
            setAckSecret={setAckSecret}
            setAckSecretCleared={setAckSecretCleared}
            ackSignatureHeader={ackSignatureHeader}
            setAckSignatureHeader={setAckSignatureHeader}
            hasAckSecret={service.hasAckSecret ?? false}
          />
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
              <label htmlFor={`${formId}-dep-service`} style={{ fontSize: "0.875rem", fontWeight: 600, display: "block" }}>
                {t("services.dependsOn")} <span className="text-error-500">*</span>
              </label>
              <Select value={depServiceId} onValueChange={setDepServiceId}>
                <SelectTrigger id={`${formId}-dep-service`} className="bg-input-background">
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
                <label htmlFor={`${formId}-dep-type`} style={{ fontSize: "0.875rem", fontWeight: 600, display: "block" }}>{t("services.type")}</label>
                <Select value={depType} onValueChange={setDepType}>
                  <SelectTrigger id={`${formId}-dep-type`} className="bg-input-background"><SelectValue /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value="Upstream">Upstream</SelectItem>
                    <SelectItem value="Downstream">Downstream</SelectItem>
                    <SelectItem value="Bidirectional">Bidirectional</SelectItem>
                  </SelectContent>
                </Select>
              </div>
              <div className="space-y-2">
                <label htmlFor={`${formId}-dep-criticality`} style={{ fontSize: "0.875rem", fontWeight: 600, display: "block" }}>{t("services.criticality")}</label>
                <Select value={depCriticality} onValueChange={setDepCriticality}>
                  <SelectTrigger id={`${formId}-dep-criticality`} className="bg-input-background"><SelectValue /></SelectTrigger>
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
