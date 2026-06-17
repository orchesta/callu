import { useState } from "react";
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
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from "@/shared/components/ui/dialog";
import { ScrollText, Info, Search } from "lucide-react";
import { LoadingState } from "@/shared/components/loading-state";
import { ErrorState } from "@/shared/components/error-state";
import { EmptyState } from "@/shared/components/empty-state";
import { PageHeader } from "@/shared/components/page-header";
import { useAuditLogs } from "../hooks/use-audit-logs";
import type { AuditLogEntry } from "../types/audit-log.types";

function actionBadgeClass(action: string) {
  switch (action) {
    case "Created":
      return "bg-success-500/10 text-success-500 border-success-500/20";
    case "Updated":
    case "SettingsChanged":
      return "bg-brand-500/10 text-brand-500 border-brand-500/20";
    case "Deleted":
    case "RoleRemoved":
      return "bg-error-500/10 text-error-500 border-error-500/20";
    case "Login":
    case "Logout":
    case "Viewed":
      return "bg-muted/10 text-muted-foreground border-muted/20";
    default:
      return "bg-warning-500/10 text-warning-500 border-warning-500/20";
  }
}

function formatTimestamp(s: string) {
  return new Date(s).toLocaleString("en-US", {
    month: "short",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}

export function AuditLogList() {
  const [entity, setEntity] = useState("");
  const [applied, setApplied] = useState<string | undefined>(undefined);
  const [count, setCount] = useState("100");
  const [selected, setSelected] = useState<AuditLogEntry | null>(null);

  const { data, isLoading, error } = useAuditLogs({
    entityName: applied,
    count: Number(count),
  });
  const rows = data ?? [];

  const apply = () => setApplied(entity.trim() || undefined);

  if (isLoading) {
    return <LoadingState message={t("auditLog.loading")} />;
  }

  if (error) {
    return (
      <ErrorState
        title={t("auditLog.loadFailed")}
        message={error instanceof Error ? error.message : t("common.errorOccurred")}
      />
    );
  }

  return (
    <>
      <div className="p-6 space-y-6">
        <PageHeader title={t("auditLog.title")} subtitle={t("auditLog.subtitle")} />

        <Card className="p-4 bg-card/80 backdrop-blur-sm border-border">
          <div className="flex flex-wrap items-end gap-4">
            <div className="flex-1 min-w-[200px]">
              <label className="mb-2 block text-xs font-semibold tracking-wide text-muted-foreground">
                {t("auditLog.entityFilter")}
              </label>
              <Input
                placeholder={t("auditLog.entityPlaceholder")}
                value={entity}
                onChange={(e) => setEntity(e.target.value)}
                onKeyDown={(e) => { if (e.key === "Enter") apply(); }}
                className="bg-input-background"
              />
            </div>
            <div className="min-w-[120px]">
              <label className="mb-2 block text-xs font-semibold tracking-wide text-muted-foreground">
                {t("auditLog.rows")}
              </label>
              <Select value={count} onValueChange={setCount}>
                <SelectTrigger className="bg-input-background">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="50">50</SelectItem>
                  <SelectItem value="100">100</SelectItem>
                  <SelectItem value="250">250</SelectItem>
                  <SelectItem value="500">500</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <Button type="button" variant="secondary" onClick={apply}>
              <Search className="w-4 h-4 mr-2" />
              {t("auditLog.filter")}
            </Button>
          </div>
        </Card>

        <Card className="overflow-hidden bg-card/80 backdrop-blur-sm border-border">
          <div className="overflow-x-auto">
            <table className="w-full">
              <thead>
                <tr className="border-b border-border bg-surface-light/20 text-left text-xs font-semibold tracking-wide text-muted-foreground">
                  <th className="p-4">{t("auditLog.colTime")}</th>
                  <th className="p-4">{t("auditLog.colUser")}</th>
                  <th className="p-4">{t("auditLog.colAction")}</th>
                  <th className="p-4">{t("auditLog.colEntity")}</th>
                  <th className="p-4">{t("auditLog.colDescription")}</th>
                  <th className="p-4"></th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => (
                  <tr
                    key={row.id}
                    className="border-b border-border hover:bg-surface-light/20 transition-colors cursor-pointer"
                    tabIndex={0}
                    role="button"
                    onClick={() => setSelected(row)}
                    onKeyDown={(e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); setSelected(row); } }}
                  >
                    <td className="p-4 text-sm text-muted-foreground whitespace-nowrap">{formatTimestamp(row.createdAt)}</td>
                    <td className="p-4 text-sm">{row.userName || row.userId || t("auditLog.system")}</td>
                    <td className="p-4">
                      <Badge className={`${actionBadgeClass(row.action)} border text-xs`}>{row.action}</Badge>
                    </td>
                    <td className="p-4 text-sm">
                      <span className="font-medium">{row.entityType}</span>
                      {row.entityId && (
                        <span className="ml-1 font-mono text-xs text-muted-foreground">{row.entityId.slice(0, 8)}</span>
                      )}
                    </td>
                    <td className="p-4 text-sm text-muted-foreground max-w-[280px] truncate">{row.description || "—"}</td>
                    <td className="p-4">
                      <Button size="sm" variant="ghost" onClick={(e) => { e.stopPropagation(); setSelected(row); }}>
                        <Info className="w-4 h-4" />
                      </Button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {rows.length === 0 && (
            <EmptyState
              icon={ScrollText}
              title={t("auditLog.noEntries")}
              description={t("auditLog.noEntriesDesc")}
            />
          )}
        </Card>
      </div>

      <Dialog open={!!selected} onOpenChange={(o) => { if (!o) setSelected(null); }}>
        <DialogContent className="bg-card border-border sm:max-w-[680px]">
          <DialogHeader>
            <DialogTitle style={{ fontSize: "1.25rem", fontWeight: 600 }}>{t("auditLog.details")}</DialogTitle>
          </DialogHeader>

          {selected && (
            <div className="space-y-4 py-2">
              <div className="grid grid-cols-2 gap-3">
                <Field label={t("auditLog.colTime")} value={new Date(selected.createdAt).toLocaleString()} />
                <Field label={t("auditLog.colUser")} value={selected.userName || selected.userId || t("auditLog.system")} />
                <Field label={t("auditLog.colAction")} value={selected.action} />
                <Field label={t("auditLog.colEntity")} value={`${selected.entityType}${selected.entityId ? ` · ${selected.entityId}` : ""}`} mono />
                {selected.ipAddress && <Field label={t("auditLog.ipAddress")} value={selected.ipAddress} mono />}
                {selected.requestPath && <Field label={t("auditLog.requestPath")} value={selected.requestPath} mono />}
              </div>
              {selected.description && <Field label={t("auditLog.colDescription")} value={selected.description} />}
              {selected.oldValues && <JsonField label={t("auditLog.oldValues")} json={selected.oldValues} />}
              {selected.newValues && <JsonField label={t("auditLog.newValues")} json={selected.newValues} />}
              {selected.userAgent && <Field label={t("auditLog.userAgent")} value={selected.userAgent} />}
            </div>
          )}

          <DialogFooter>
            <Button onClick={() => setSelected(null)} className="bg-brand-500 hover:bg-brand-600 text-white">
              {t("common.close")}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}

function Field({ label, value, mono }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="rounded-lg bg-surface-light/20 p-3">
      <p className="mb-1 text-xs text-muted-foreground">{label}</p>
      <p className={`text-sm font-medium ${mono ? "font-mono break-all" : ""}`}>{value}</p>
    </div>
  );
}

function JsonField({ label, json }: { label: string; json: string }) {
  let pretty = json;
  try {
    pretty = JSON.stringify(JSON.parse(json), null, 2);
  } catch {
    /* leave raw if not valid JSON */
  }
  return (
    <div className="rounded-lg bg-surface-light/20 p-3">
      <p className="mb-1 text-xs text-muted-foreground">{label}</p>
      <pre className="overflow-x-auto whitespace-pre-wrap break-all font-mono text-xs text-muted-foreground">{pretty}</pre>
    </div>
  );
}
