import { useState, useMemo, useCallback } from "react";
import React from "react";
import { t } from "@/shared/locales/i18n";
import { Link } from "react-router";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Textarea } from "@/shared/components/ui/textarea";
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
  Server,
  Plus,
  ExternalLink,
  Edit,
  Trash2,
  Info,
} from "lucide-react";
import { LoadingState } from "@/shared/components/loading-state";
import { ErrorState } from "@/shared/components/error-state";
import { EmptyState } from "@/shared/components/empty-state";
import { StatCard } from "@/shared/components/stat-card";
import { PageHeader } from "@/shared/components/page-header";
import { SearchInput } from "@/shared/components/search-input";
import { DeleteConfirmDialog } from "@/shared/components/delete-confirm-dialog";
import {
  useServices,
  useCreateService,
  useUpdateService,
  useDeleteService,
} from "../hooks/use-services";
import type { ServiceListDto } from "../types/service.types";

function getStatusStyles(status: string) {
  const s = String(status).toLowerCase();
  if (s === "operational")
    return {
      badge: "bg-success-500/10 text-success-500 border-success-500/20",
      glow: "shadow-success-500/20",
      dot: "bg-success-500",
      label: t("services.statusOperational"),
    };
  if (s.includes("degraded"))
    return {
      badge: "bg-warning-500/10 text-warning-500 border-warning-500/20",
      glow: "shadow-warning-500/20",
      dot: "bg-warning-500 animate-pulse",
      label: t("services.statusDegraded"),
    };
  if (s.includes("partial"))
    return {
      badge: "bg-warning-500/10 text-warning-500 border-warning-500/20",
      glow: "shadow-warning-500/20",
      dot: "bg-warning-500 animate-pulse",
      label: t("services.statusPartialOutage"),
    };
  if (s.includes("major"))
    return {
      badge: "bg-error-500/10 text-error-500 border-error-500/20",
      glow: "shadow-error-500/20",
      dot: "bg-error-500 animate-pulse",
      label: t("services.statusMajorOutage"),
    };
  if (s.includes("maintenance"))
    return {
      badge: "bg-blue-400/10 text-blue-400 border-blue-400/20",
      glow: "",
      dot: "bg-blue-400",
      label: t("services.statusMaintenance"),
    };
  return {
    badge: "bg-muted/10 text-muted-foreground border-muted/20",
    glow: "",
    dot: "bg-muted",
    label: status,
  };
}

function getEnvironmentBadge(env?: string) {
  if (!env) return "bg-muted/10 text-muted-foreground border-muted/20";
  const e = env.toLowerCase();
  if (e === "production") return "bg-brand-500/10 text-brand-500 border-brand-500/20";
  if (e === "staging") return "bg-warning-500/10 text-warning-500 border-warning-500/20";
  return "bg-muted/10 text-muted-foreground border-muted/20";
}

interface ServiceCardProps {
  service: ServiceListDto;
  onEdit: (service: ServiceListDto) => void;
  onDelete: (service: ServiceListDto) => void;
}

const ServiceCard = React.memo(function ServiceCard({ service, onEdit, onDelete }: ServiceCardProps) {
  const statusStyles = getStatusStyles(service.status);
  const envBadge = getEnvironmentBadge(service.environment);

  return (
    <Card className={`p-6 bg-card/80 backdrop-blur-sm border-border hover:border-border-light transition-all hover:shadow-lg ${statusStyles.glow}`}>
      <div className="flex items-start justify-between gap-3 mb-4">
        <div className="flex items-start gap-3 flex-1 min-w-0">
          <div className="w-10 h-10 rounded-lg bg-brand-500/10 flex items-center justify-center flex-shrink-0">
            <Server className="w-5 h-5 text-brand-500" />
          </div>
          <div className="flex-1 min-w-0">
            <div className="flex items-center gap-2 mb-1">
              <Link
                to={`/services/${service.id}`}
                className="font-semibold hover:text-brand-500 transition-colors truncate"
                style={{ fontSize: "1.0625rem" }}
              >
                {service.name}
              </Link>
              <div className={`w-2 h-2 rounded-full ${statusStyles.dot}`} />
            </div>
            <p style={{ fontSize: "0.875rem", color: "#94A3B8" }} className="line-clamp-2">
              {service.description || t("services.noDescription")}
            </p>
          </div>
        </div>
      </div>

      <div className="flex flex-wrap gap-2 mb-4">
        <Badge className={`${statusStyles.badge} border text-xs`}>{statusStyles.label}</Badge>
        {service.environment && <Badge className={`${envBadge} border text-xs`}>{service.environment}</Badge>}
        {service.type && <Badge className="bg-muted/10 text-muted-foreground border-muted/20 border text-xs">{service.type}</Badge>}
      </div>

      <div className="grid grid-cols-2 gap-4 mb-4 p-3 rounded-lg bg-surface-light/20">
        <div>
          <p style={{ fontSize: "0.75rem", color: "#94A3B8", marginBottom: "0.25rem" }}>{t("services.uptime30d")}</p>
          <p style={{ fontSize: "0.9375rem", fontWeight: 600 }}>{service.uptime.toFixed(1)}%</p>
        </div>
        <div>
          <p style={{ fontSize: "0.75rem", color: "#94A3B8", marginBottom: "0.25rem" }}>{t("services.activeIncidents")}</p>
          <p style={{ fontSize: "0.9375rem", fontWeight: 600 }} className={service.incidentCount > 0 ? "text-error-500" : ""}>
            {service.incidentCount}
          </p>
        </div>
      </div>

      {service.teamName && (
        <div className="mb-4 p-3 rounded-lg bg-brand-500/5 border border-brand-500/20">
          <p style={{ fontSize: "0.75rem", color: "#94A3B8", marginBottom: "0.25rem" }}>{t("services.team")}</p>
          <p style={{ fontSize: "0.875rem", fontWeight: 500 }}>{service.teamName}</p>
        </div>
      )}

      <div className="flex gap-2 pt-4 border-t border-border">
        <Link to={`/services/${service.id}`} className="flex-1">
          <Button variant="outline" className="w-full bg-input-background">
            <ExternalLink className="w-4 h-4 mr-2" />
            {t("services.viewDetails")}
          </Button>
        </Link>
        <Button variant="outline" size="sm" className="bg-input-background" onClick={() => onEdit(service)} aria-label={`Edit ${service.name}`}>
          <Edit className="w-4 h-4" />
        </Button>
        <Button variant="outline" size="sm" className="bg-input-background hover:bg-error-500/10 hover:text-error-500" onClick={() => onDelete(service)} aria-label={`Delete ${service.name}`}>
          <Trash2 className="w-4 h-4" />
        </Button>
      </div>
    </Card>
  );
});

export function ServicesList() {
  const { data: services, isLoading, error } = useServices();
  const createServiceMutation = useCreateService();
  const updateServiceMutation = useUpdateService();
  const deleteServiceMutation = useDeleteService();

  const [searchQuery, setSearchQuery] = useState("");
  const [isCreateModalOpen, setIsCreateModalOpen] = useState(false);
  const [isEditModalOpen, setIsEditModalOpen] = useState(false);
  const [isDeleteModalOpen, setIsDeleteModalOpen] = useState(false);
  const [selectedService, setSelectedService] = useState<ServiceListDto | null>(null);
  const [formData, setFormData] = useState({
    name: "",
    description: "",
    type: "Api",
    environment: "production",
  });

  const filteredServices = useMemo(() => {
    if (!services) return [];
    return services.filter(
      (s) =>
        s.name.toLowerCase().includes(searchQuery.toLowerCase()) ||
        (s.description ?? "").toLowerCase().includes(searchQuery.toLowerCase()),
    );
  }, [services, searchQuery]);

  const stats = useMemo(
    () => ({
      total: filteredServices.length,
      operational: filteredServices.filter((s) => String(s.status).toLowerCase() === "operational").length,
      degraded: filteredServices.filter((s) => String(s.status).toLowerCase().includes("degraded") || String(s.status).toLowerCase().includes("partial")).length,
      down: filteredServices.filter((s) => String(s.status).toLowerCase().includes("major")).length,
    }),
    [filteredServices],
  );

  const resetForm = () =>
    setFormData({ name: "", description: "", type: "Api", environment: "production" });

  const handleEditService = useCallback((service: ServiceListDto) => {
    setSelectedService(service);
    setFormData({
      name: service.name,
      description: service.description || "",
      type: service.type || "Api",
      environment: service.environment || "production",
    });
    setIsEditModalOpen(true);
  }, []);

  const handleDeleteService = useCallback((service: ServiceListDto) => {
    setSelectedService(service);
    setIsDeleteModalOpen(true);
  }, []);

  const handleCreate = () => {
    createServiceMutation.mutate(
      { name: formData.name, description: formData.description || undefined, type: formData.type, environment: formData.environment },
      {
        onSuccess: () => {
          setIsCreateModalOpen(false);
          resetForm();
        },
      },
    );
  };

  const handleEdit = () => {
    if (!selectedService) return;
    updateServiceMutation.mutate(
      { id: selectedService.id, data: { name: formData.name, description: formData.description || undefined, type: formData.type, environment: formData.environment } },
      {
        onSuccess: () => {
          setIsEditModalOpen(false);
          resetForm();
        },
      },
    );
  };

  const handleDelete = () => {
    if (!selectedService) return;
    deleteServiceMutation.mutate(selectedService.id, {
      onSuccess: () => {
        setIsDeleteModalOpen(false);
        setSelectedService(null);
      },
    });
  };

  if (isLoading) {
    return <LoadingState message={t("services.loading")} />;
  }

  if (error) {
    return (
      <ErrorState
        title={t("services.failedToLoad")}
        message={error instanceof Error ? error.message : t("common.error")}
      />
    );
  }

  return (
    <>
      <div className="p-6 space-y-6">
        <PageHeader
          title={t("services.catalogTitle")}
          subtitle={t("services.catalogSubtitle")}
          action={
            <Button
              onClick={() => { resetForm(); setIsCreateModalOpen(true); }}
              className="bg-brand-500 hover:bg-brand-600 text-white shadow-lg shadow-brand-500/20"
            >
              <Plus className="w-4 h-4 mr-2" />
              {t("services.createService")}
            </Button>
          }
        />

        <SearchInput
          placeholder={t("services.searchPlaceholder")}
          value={searchQuery}
          onChange={setSearchQuery}
        />

        <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
          <StatCard label={t("services.totalServices").toUpperCase()} value={stats.total} />
          <StatCard label={t("services.statusOperational").toUpperCase()} value={stats.operational} color="#22C55E" borderColor="border-success-500/20" />
          <StatCard label={t("services.statusDegraded").toUpperCase()} value={stats.degraded} color="#FB923C" borderColor="border-warning-500/20" />
          <StatCard label={t("services.statusDown").toUpperCase()} value={stats.down} color="#FF4D4D" borderColor="border-error-500/20" />
        </div>

        <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
          {filteredServices.map((service) => (
            <ServiceCard
              key={service.id}
              service={service}
              onEdit={handleEditService}
              onDelete={handleDeleteService}
            />
          ))}
        </div>

        {filteredServices.length === 0 && (
          <EmptyState
            icon={Server}
            title={t("services.noServicesFound")}
            description={t("services.noServicesHint")}
            action={
              <Button onClick={() => { resetForm(); setIsCreateModalOpen(true); }} className="bg-brand-500 hover:bg-brand-600">
                <Plus className="w-4 h-4 mr-2" />
                {t("services.createService")}
              </Button>
            }
          />
        )}
      </div>

      <Dialog open={isCreateModalOpen} onOpenChange={setIsCreateModalOpen}>
        <DialogContent className="bg-card border-border sm:max-w-[600px]">
          <DialogHeader>
            <DialogTitle style={{ fontSize: "1.5rem", fontWeight: 600 }}>{t("services.createNewService")}</DialogTitle>
            <DialogDescription style={{ fontSize: "0.875rem", color: "#94A3B8" }}>
              {t("services.createDescription")}
            </DialogDescription>
          </DialogHeader>

          <div className="space-y-5 py-4">
            <div className="p-4 rounded-lg bg-brand-500/5 border border-brand-500/20">
              <div className="flex gap-3">
                <Info className="w-5 h-5 text-brand-500 flex-shrink-0 mt-0.5" />
                <div>
                  <p style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.25rem" }}>
                    {t("services.integrationKeyTitle")}
                  </p>
                  <p style={{ fontSize: "0.8125rem", color: "#94A3B8" }}>
                    {t("services.integrationKeyDescription")}
                  </p>
                </div>
              </div>
            </div>

            <div className="space-y-2">
              <label style={{ fontSize: "0.875rem", fontWeight: 600, display: "block" }}>
                {t("services.serviceName")} <span className="text-error-500">*</span>
              </label>
              <Input
                placeholder={t("services.serviceNamePlaceholder")}
                value={formData.name}
                onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                className="bg-input-background"
              />
            </div>

            <div className="space-y-2">
              <label style={{ fontSize: "0.875rem", fontWeight: 600, display: "block" }}>{t("common.description")}</label>
              <Textarea
                placeholder={t("services.descriptionPlaceholder")}
                value={formData.description}
                onChange={(e) => setFormData({ ...formData, description: e.target.value })}
                rows={3}
                className="bg-input-background resize-none"
              />
            </div>

            <div className="grid grid-cols-2 gap-4">
              <div className="space-y-2">
                <label style={{ fontSize: "0.875rem", fontWeight: 600, display: "block" }}>
                  {t("services.type")} <span className="text-error-500">*</span>
                </label>
                <Select value={formData.type} onValueChange={(v) => setFormData({ ...formData, type: v })}>
                  <SelectTrigger className="bg-input-background"><SelectValue /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value="Api">API</SelectItem>
                    <SelectItem value="Website">Website</SelectItem>
                    <SelectItem value="Database">Database</SelectItem>
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

              <div className="space-y-2">
                <label style={{ fontSize: "0.875rem", fontWeight: 600, display: "block" }}>
                  {t("services.environment")} <span className="text-error-500">*</span>
                </label>
                <Select value={formData.environment} onValueChange={(v) => setFormData({ ...formData, environment: v })}>
                  <SelectTrigger className="bg-input-background"><SelectValue /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value="production">
                      <div className="flex items-center gap-2">
                        <div className="w-2 h-2 rounded-full bg-brand-500" />
                        <span>Production</span>
                      </div>
                    </SelectItem>
                    <SelectItem value="staging">
                      <div className="flex items-center gap-2">
                        <div className="w-2 h-2 rounded-full bg-warning-500" />
                        <span>Staging</span>
                      </div>
                    </SelectItem>
                    <SelectItem value="development">
                      <div className="flex items-center gap-2">
                        <div className="w-2 h-2 rounded-full bg-muted" />
                        <span>Development</span>
                      </div>
                    </SelectItem>
                  </SelectContent>
                </Select>
              </div>
            </div>
          </div>

          <DialogFooter>
            <Button variant="outline" onClick={() => setIsCreateModalOpen(false)} disabled={createServiceMutation.isPending} className="bg-input-background">
              {t("common.cancel")}
            </Button>
            <Button onClick={handleCreate} disabled={!formData.name || createServiceMutation.isPending} className="bg-brand-500 hover:bg-brand-600 text-white">
              {createServiceMutation.isPending ? (
                <>
                  <div className="w-4 h-4 border-2 border-white/30 border-t-white rounded-full animate-spin mr-2" />
                  {t("common.creating")}
                </>
              ) : (
                <>
                  <Plus className="w-4 h-4 mr-2" />
                  {t("services.createService")}
                </>
              )}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog open={isEditModalOpen} onOpenChange={setIsEditModalOpen}>
        <DialogContent className="bg-card border-border sm:max-w-[600px]">
          <DialogHeader>
            <DialogTitle style={{ fontSize: "1.5rem", fontWeight: 600 }}>{t("services.editService")}</DialogTitle>
            <DialogDescription style={{ fontSize: "0.875rem", color: "#94A3B8" }}>
              {t("services.editDescription")}
            </DialogDescription>
          </DialogHeader>

          <div className="space-y-5 py-4">
            <div className="space-y-2">
              <label style={{ fontSize: "0.875rem", fontWeight: 600, display: "block" }}>
                {t("services.serviceName")} <span className="text-error-500">*</span>
              </label>
              <Input
                placeholder={t("services.serviceNamePlaceholderShort")}
                value={formData.name}
                onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                className="bg-input-background"
              />
            </div>

            <div className="space-y-2">
              <label style={{ fontSize: "0.875rem", fontWeight: 600, display: "block" }}>{t("common.description")}</label>
              <Textarea
                placeholder={t("services.descriptionPlaceholderShort")}
                value={formData.description}
                onChange={(e) => setFormData({ ...formData, description: e.target.value })}
                rows={3}
                className="bg-input-background resize-none"
              />
            </div>

            <div className="grid grid-cols-2 gap-4">
              <div className="space-y-2">
                <label style={{ fontSize: "0.875rem", fontWeight: 600, display: "block" }}>{t("services.type")}</label>
                <Select value={formData.type} onValueChange={(v) => setFormData({ ...formData, type: v })}>
                  <SelectTrigger className="bg-input-background"><SelectValue /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value="Api">API</SelectItem>
                    <SelectItem value="Website">Website</SelectItem>
                    <SelectItem value="Database">Database</SelectItem>
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

              <div className="space-y-2">
                <label style={{ fontSize: "0.875rem", fontWeight: 600, display: "block" }}>{t("services.environment")}</label>
                <Select value={formData.environment} onValueChange={(v) => setFormData({ ...formData, environment: v })}>
                  <SelectTrigger className="bg-input-background"><SelectValue /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value="production">Production</SelectItem>
                    <SelectItem value="staging">Staging</SelectItem>
                    <SelectItem value="development">Development</SelectItem>
                  </SelectContent>
                </Select>
              </div>
            </div>
          </div>

          <DialogFooter>
            <Button variant="outline" onClick={() => setIsEditModalOpen(false)} className="bg-input-background">
              {t("common.cancel")}
            </Button>
            <Button onClick={handleEdit} disabled={!formData.name || updateServiceMutation.isPending} className="bg-brand-500 hover:bg-brand-600 text-white">
              {updateServiceMutation.isPending ? t("common.saving") : t("services.saveChanges")}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <DeleteConfirmDialog
        open={isDeleteModalOpen}
        onOpenChange={setIsDeleteModalOpen}
        title={t("services.deleteService")}
        message={t("services.deleteConfirmation", { name: selectedService?.name ?? "" })}
        warning={t("services.deleteWarning")}
        onConfirm={handleDelete}
        isLoading={deleteServiceMutation.isPending}
        confirmLabel={t("services.deleteServiceBtn")}
        cancelLabel={t("common.cancel")}
      />
    </>
  );
}