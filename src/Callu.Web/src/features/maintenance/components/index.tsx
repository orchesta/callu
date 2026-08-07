import { useState, useId, useEffect } from "react";
import {
  useMaintenanceWindows,
  useCreateMaintenanceWindow,
  useUpdateMaintenanceWindow,
  useCancelMaintenanceWindow,
  useDeleteMaintenanceWindow,
} from "../hooks/use-maintenance";
import { useServices } from "@/features/services/hooks/use-services";
import { PageHeader } from "@/shared/components/page-header";
import { Button } from "@/shared/components/ui/button";
import { Badge } from "@/shared/components/ui/badge";
import { Card } from "@/shared/components/ui/card";
import { Input } from "@/shared/components/ui/input";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from "@/shared/components/ui/dialog";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";
import { LoadingState } from "@/shared/components/loading-state";
import { ErrorState } from "@/shared/components/error-state";
import { EmptyState } from "@/shared/components/empty-state";
import { Wrench, Plus, XCircle, Trash2, Clock, Check, Server, Pencil } from "lucide-react";
import { formatDateTime } from "@/shared/utils/time";
import type {
  CreateMaintenanceWindowRequest,
  MaintenanceWindowDto,
} from "../types/maintenance.types";
import { t } from "@/shared/locales/i18n";
import { DeleteConfirmDialog } from "@/shared/components/delete-confirm-dialog";

function toLocalInputValue(iso: string) {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "";
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

function StatusBadge({ isActive, isCancelled }: { isActive: boolean; isCancelled: boolean }) {
  if (isCancelled)
    return (
      <Badge variant="outline" className="bg-muted/10 text-muted-foreground border-muted/30">
        {t("maintenancePage.badgeCancelled")}
      </Badge>
    );
  if (isActive)
    return (
      <Badge variant="outline" className="bg-warning-500/10 text-warning-500 border-warning-500/30">
        {t("common.active")}
      </Badge>
    );
  return (
    <Badge variant="outline" className="bg-muted/10 text-muted-foreground border-muted/30">
      {t("maintenancePage.badgeScheduled")}
    </Badge>
  );
}

interface ServicePickerProps {
  selectedIds: string[];
  onChange: (ids: string[]) => void;
}

function ServicePicker({ selectedIds, onChange }: ServicePickerProps) {
  const { data: services = [] } = useServices();

  const toggle = (id: string) => {
    onChange(
      selectedIds.includes(id)
        ? selectedIds.filter((s) => s !== id)
        : [...selectedIds, id],
    );
  };

  if (services.length === 0) {
    return (
      <p style={{ fontSize: "0.8125rem", color: "#64748B", padding: "8px 0" }}>
        {t("maintenancePage.noServicesHint")}
      </p>
    );
  }

  return (
    <div
      style={{
        border: "1px solid rgba(148,163,184,0.15)",
        borderRadius: "8px",
        maxHeight: "180px",
        overflowY: "auto",
      }}
    >
      {services.map((svc) => {
        const selected = selectedIds.includes(svc.id);
        return (
          <div
            key={svc.id}
            onClick={() => toggle(svc.id)}
            style={{
              display: "flex",
              alignItems: "center",
              gap: "10px",
              padding: "8px 12px",
              cursor: "pointer",
              borderBottom: "1px solid rgba(148,163,184,0.08)",
              background: selected ? "rgba(62,123,250,0.06)" : "transparent",
              transition: "background 0.15s",
            }}
          >
            <div
              style={{
                width: "16px",
                height: "16px",
                borderRadius: "4px",
                border: selected ? "none" : "1.5px solid rgba(148,163,184,0.4)",
                background: selected ? "#3E7BFA" : "transparent",
                display: "flex",
                alignItems: "center",
                justifyContent: "center",
                flexShrink: 0,
              }}
            >
              {selected && <Check className="w-3 h-3 text-white" />}
            </div>
            <Server className="w-3.5 h-3.5 text-muted-foreground" />
            <span style={{ fontSize: "0.875rem", fontWeight: selected ? 600 : 400 }}>
              {svc.name}
            </span>
          </div>
        );
      })}
    </div>
  );
}

function AffectedServicesPills({ serviceIds }: { serviceIds: string[] }) {
  const { data: services = [] } = useServices();
  if (!serviceIds || serviceIds.length === 0) {
    return (
      <span style={{ fontSize: "0.75rem", color: "#64748B" }}>
        {t("maintenancePage.allServicesLabel")}
      </span>
    );
  }
  const names = serviceIds
    .map((id) => services.find((s) => s.id === id)?.name ?? id.slice(0, 8))
    .filter(Boolean);

  return (
    <div style={{ display: "flex", flexWrap: "wrap", gap: "4px" }}>
      {names.map((name) => (
        <span
          key={name}
          style={{
            fontSize: "0.6875rem",
            padding: "2px 6px",
            borderRadius: "4px",
            background: "rgba(62,123,250,0.1)",
            color: "#3E7BFA",
            border: "1px solid rgba(62,123,250,0.2)",
          }}
        >
          {name}
        </span>
      ))}
    </div>
  );
}

function MaintenanceWindowModal({
  open,
  onClose,
  editing,
}: {
  open: boolean;
  onClose: () => void;
  editing: MaintenanceWindowDto | null;
}) {
  const formId = useId();
  const createMutation = useCreateMaintenanceWindow();
  const updateMutation = useUpdateMaintenanceWindow();
  const isEdit = !!editing;
  const startLocked = isEdit && new Date(editing.startsAt).getTime() <= Date.now();

  const [form, setForm] = useState<CreateMaintenanceWindowRequest>({
    title: "",
    description: "",
    startsAt: new Date().toISOString().slice(0, 16),
    endsAt: new Date(Date.now() + 3600_000).toISOString().slice(0, 16),
    affectedServiceIds: [],
    appliesToAllServices: false,
    mode: "SuppressAlerts",
  });

  useEffect(() => {
    if (!open) return;
    if (editing) {
      setForm({
        title: editing.title,
        description: editing.description ?? "",
        startsAt: toLocalInputValue(editing.startsAt),
        endsAt: toLocalInputValue(editing.endsAt),
        affectedServiceIds: editing.affectedServiceIds,
        appliesToAllServices: editing.appliesToAllServices,
        mode: editing.mode,
      });
    } else {
      setForm({
        title: "",
        description: "",
        startsAt: new Date().toISOString().slice(0, 16),
        endsAt: new Date(Date.now() + 3600_000).toISOString().slice(0, 16),
        affectedServiceIds: [],
        appliesToAllServices: false,
        mode: "SuppressAlerts",
      });
    }
  }, [open, editing]);

  const hasValidScope =
    form.appliesToAllServices || form.affectedServiceIds.length > 0;
  const pending = createMutation.isPending || updateMutation.isPending;

  const handleSubmit = async () => {
    if (!form.title || !form.startsAt || !form.endsAt || !hasValidScope) return;
    const payload = {
      ...form,
      startsAt: new Date(form.startsAt).toISOString(),
      endsAt: new Date(form.endsAt).toISOString(),
      affectedServiceIds: form.appliesToAllServices ? [] : form.affectedServiceIds,
    };
    if (editing) {
      await updateMutation.mutateAsync({ id: editing.id.toString(), ...payload });
    } else {
      await createMutation.mutateAsync(payload);
    }
    onClose();
  };

  return (
    <Dialog open={open} onOpenChange={(v) => !v && onClose()}>
      <DialogContent className="sm:max-w-lg bg-card border-border">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <Wrench className="w-5 h-5 text-brand-500" />
            {isEdit ? t("maintenancePage.editDialogTitle") : t("maintenancePage.dialogTitle")}
          </DialogTitle>
        </DialogHeader>

        <div className="space-y-4 py-2">
          <div>
            <label className="text-xs font-semibold text-muted-foreground uppercase tracking-wide">
              {t("maintenancePage.labelTitle")}
            </label>
            <Input
              className="mt-1"
              placeholder={t("maintenancePage.titleExamplePlaceholder")}
              value={form.title}
              onChange={(e) => setForm((p) => ({ ...p, title: e.target.value }))}
            />
          </div>

          <div>
            <label className="text-xs font-semibold text-muted-foreground uppercase tracking-wide">
              {t("maintenancePage.labelDescription")}
            </label>
            <Input
              className="mt-1"
              placeholder={t("maintenancePage.notesPlaceholder")}
              value={form.description ?? ""}
              onChange={(e) => setForm((p) => ({ ...p, description: e.target.value }))}
            />
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="text-xs font-semibold text-muted-foreground uppercase tracking-wide">
                {t("maintenancePage.labelStartsAt")}
              </label>
              <Input
                type="datetime-local"
                className="mt-1"
                value={form.startsAt}
                disabled={startLocked}
                onChange={(e) => setForm((p) => ({ ...p, startsAt: e.target.value }))}
              />
              {startLocked && (
                <p style={{ fontSize: "0.75rem", color: "#64748B", marginTop: "4px" }}>
                  {t("maintenancePage.startLockedHint")}
                </p>
              )}
            </div>
            <div>
              <label className="text-xs font-semibold text-muted-foreground uppercase tracking-wide">
                {t("maintenancePage.labelEndsAt")}
              </label>
              <Input
                type="datetime-local"
                className="mt-1"
                value={form.endsAt}
                onChange={(e) => setForm((p) => ({ ...p, endsAt: e.target.value }))}
              />
            </div>
          </div>

          <div>
            <label htmlFor={`${formId}-mode`} className="text-xs font-semibold text-muted-foreground uppercase tracking-wide">
              {t("maintenancePage.labelMode")}
            </label>
            <Select
              value={form.mode}
              onValueChange={(v) => setForm((p) => ({ ...p, mode: v }))}
            >
              <SelectTrigger id={`${formId}-mode`} className="mt-1">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="SuppressAlerts">{t("maintenancePage.modeSuppressFull")}</SelectItem>
                <SelectItem value="AutoAcknowledge">{t("maintenancePage.modeAutoAckFull")}</SelectItem>
              </SelectContent>
            </Select>
          </div>

          <div>
            <label className="flex items-center gap-2 text-sm cursor-pointer">
              <input
                type="checkbox"
                checked={form.appliesToAllServices}
                onChange={(e) =>
                  setForm((p) => ({ ...p, appliesToAllServices: e.target.checked }))
                }
                className="rounded"
              />
              <span>{t("maintenancePage.applyToAllServices")}</span>
            </label>
            <p style={{ fontSize: "0.75rem", color: "#64748B", marginTop: "4px" }}>
              {t("maintenancePage.applyToAllServicesHint")}
            </p>
          </div>

          <div className={form.appliesToAllServices ? "opacity-50 pointer-events-none" : ""}>
            <div className="flex items-center justify-between mb-1">
              <label className="text-xs font-semibold text-muted-foreground uppercase tracking-wide">
                {t("maintenancePage.labelAffectedServices")}
              </label>
              <span style={{ fontSize: "0.75rem", color: "#64748B" }}>
                {form.affectedServiceIds.length === 0
                  ? t("maintenancePage.selectionHintNone")
                  : t("maintenancePage.selectionHintCount", {
                      count: String(form.affectedServiceIds.length),
                    })}
              </span>
            </div>
            <ServicePicker
              selectedIds={form.affectedServiceIds}
              onChange={(ids) => setForm((p) => ({ ...p, affectedServiceIds: ids }))}
            />
          </div>

          {!hasValidScope && (
            <p style={{ fontSize: "0.75rem", color: "#EF4444" }}>
              {t("maintenancePage.scopeRequiredHint")}
            </p>
          )}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            {t("common.cancel")}
          </Button>
          <Button
            onClick={handleSubmit}
            disabled={pending || !form.title || !hasValidScope}
          >
            {pending
              ? t("maintenancePage.savingWindow")
              : isEdit
                ? t("maintenancePage.saveWindow")
                : t("maintenancePage.createWindow")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

export function MaintenancePage() {
  const { data: windows = [], isLoading, error, refetch } = useMaintenanceWindows();
  const cancelMutation = useCancelMaintenanceWindow();
  const deleteMutation = useDeleteMaintenanceWindow();
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<MaintenanceWindowDto | null>(null);
  const [deleting, setDeleting] = useState<MaintenanceWindowDto | null>(null);

  const openCreate = () => {
    setEditing(null);
    setModalOpen(true);
  };

  const openEdit = (w: MaintenanceWindowDto) => {
    setEditing(w);
    setModalOpen(true);
  };

  if (isLoading) return <LoadingState message={t("settings.maintenance.loading")} />;
  if (error)
    return (
      <ErrorState
        title={t("settings.maintenance.loadFailed")}
        message={error instanceof Error ? error.message : t("common.unexpectedError")}
        onRetry={() => refetch()}
      />
    );

  return (
    <div className="p-6 space-y-6">
      <PageHeader
        title={t("settings.maintenance.title")}
        subtitle={t("maintenancePage.pageSubtitle")}
        action={
          <Button onClick={openCreate}>
            <Plus className="w-4 h-4 mr-2" />
            {t("maintenancePage.newWindow")}
          </Button>
        }
      />

      {windows.length === 0 ? (
        <EmptyState
          icon={Wrench}
          title={t("maintenanceWindows.noWindows")}
          description={t("maintenancePage.emptyDescription")}
          action={
            <Button onClick={openCreate}>
              <Plus className="w-4 h-4 mr-2" />
              {t("settings.maintenance.schedule")}
            </Button>
          }
        />
      ) : (
        <Card className="overflow-hidden border-border bg-card">
          <div className="overflow-x-auto">
            <table className="w-full text-sm text-left">
              <thead className="bg-muted/50 text-muted-foreground uppercase outline outline-1 outline-border">
                <tr>
                  <th className="px-4 py-3 font-medium">{t("maintenancePage.colStatus")}</th>
                  <th className="px-4 py-3 font-medium">{t("maintenancePage.colTitle")}</th>
                  <th className="px-4 py-3 font-medium">{t("maintenancePage.colAffectedServices")}</th>
                  <th className="px-4 py-3 font-medium">{t("maintenancePage.colMode")}</th>
                  <th className="px-4 py-3 font-medium">{t("maintenancePage.colWindow")}</th>
                  <th className="px-4 py-3 font-medium text-right">{t("maintenancePage.colActions")}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {windows.map((w) => (
                  <tr key={w.id} className="hover:bg-muted/30 transition-colors">
                    <td className="px-4 py-3">
                      <StatusBadge isActive={w.isActive} isCancelled={w.isCancelled} />
                    </td>
                    <td className="px-4 py-3">
                      <p className="font-medium text-foreground">{w.title}</p>
                      {w.description && (
                        <p className="text-xs text-muted-foreground mt-0.5">{w.description}</p>
                      )}
                    </td>
                    <td className="px-4 py-3">
                      {w.appliesToAllServices ? (
                        <Badge variant="secondary" className="text-xs">
                          {t("maintenancePage.allServicesBadge")}
                        </Badge>
                      ) : (
                        <AffectedServicesPills serviceIds={w.affectedServiceIds} />
                      )}
                    </td>
                    <td className="px-4 py-3">
                      <Badge
                        variant="secondary"
                        className="bg-secondary/50 text-secondary-foreground text-xs"
                      >
                        {w.mode === "SuppressAlerts"
                          ? t("maintenancePage.modeSuppressShort")
                          : t("maintenancePage.modeAutoAckShort")}
                      </Badge>
                    </td>
                    <td className="px-4 py-3">
                      <div className="flex items-center gap-1 text-muted-foreground text-xs">
                        <Clock className="w-3.5 h-3.5" />
                        <span>{formatDateTime(w.startsAt)}</span>
                        <span className="mx-1">→</span>
                        <span>{formatDateTime(w.endsAt)}</span>
                      </div>
                    </td>
                    <td className="px-4 py-3 text-right">
                      <div className="flex items-center justify-end gap-2">
                        {!w.isCancelled && (
                          <>
                            <Button
                              variant="outline"
                              size="sm"
                              onClick={() => openEdit(w)}
                            >
                              <Pencil className="w-4 h-4 mr-1" />
                              {t("common.edit")}
                            </Button>
                            <Button
                              variant="outline"
                              size="sm"
                              className="text-warning-500 border-warning-500/30 hover:bg-warning-500/10"
                              onClick={() => cancelMutation.mutate(w.id.toString())}
                              disabled={cancelMutation.isPending}
                            >
                              <XCircle className="w-4 h-4 mr-1" />
                              {t("maintenancePage.cancelWindow")}
                            </Button>
                          </>
                        )}
                        <Button
                          variant="outline"
                          size="sm"
                          className="text-error-500 border-error-500/30 hover:bg-error-500/10"
                          aria-label={t("maintenance.deleteAria").replace("{title}", w.title)}
                          onClick={() => setDeleting(w)}
                          disabled={deleteMutation.isPending}
                        >
                          <Trash2 className="w-4 h-4 mr-1" />
                          {t("common.delete")}
                        </Button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Card>
      )}

      <MaintenanceWindowModal
        open={modalOpen}
        editing={editing}
        onClose={() => {
          setModalOpen(false);
          setEditing(null);
        }}
      />

      <DeleteConfirmDialog
        open={!!deleting}
        onOpenChange={(open) => !open && setDeleting(null)}
        title={t("settings.maintenance.deleteTitle")}
        message={t("settings.maintenance.deleteMsg").replace("{title}", deleting?.title ?? "")}
        warning={t("settings.maintenance.deleteWarn")}
        isLoading={deleteMutation.isPending}
        onConfirm={() => {
          if (!deleting) return;
          deleteMutation.mutate(deleting.id.toString(), { onSettled: () => setDeleting(null) });
        }}
      />
    </div>
  );
}
