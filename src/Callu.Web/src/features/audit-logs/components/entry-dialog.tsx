import { Button } from "@/shared/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from "@/shared/components/ui/dialog";
import { t } from "@/shared/locales/i18n";
import type { AuditLogEntry } from "../types/audit-log.types";
import { dateLocale } from "@/shared/utils/datetime";

interface AuditEntryDialogProps {
  entry: AuditLogEntry | null;
  onClose: () => void;
}

export function AuditEntryDialog({ entry, onClose }: AuditEntryDialogProps) {
  return (
    <Dialog open={!!entry} onOpenChange={(o) => { if (!o) onClose(); }}>
      <DialogContent className="bg-card border-border sm:max-w-[680px]">
        <DialogHeader>
          <DialogTitle style={{ fontSize: "1.25rem", fontWeight: 600 }}>{t("auditLog.details")}</DialogTitle>
        </DialogHeader>

        {entry && (
          <div className="space-y-4 py-2">
            <div className="grid grid-cols-2 gap-3">
              <Field label={t("auditLog.colTime")} value={new Date(entry.createdAt).toLocaleString(dateLocale())} />
              <Field label={t("auditLog.colUser")} value={entry.actorDisplayName || entry.actorId || t("auditLog.system")} />
              <Field label={t("auditLog.colAction")} value={entry.action} />
              <Field label={t("auditLog.colEntity")} value={`${entry.resourceType}${entry.resourceId ? ` · ${entry.resourceId}` : ""}`} mono />
              {entry.requestIpAddress && <Field label={t("auditLog.ipAddress")} value={entry.requestIpAddress} mono />}
              {entry.requestRoute && <Field label={t("auditLog.requestPath")} value={entry.requestRoute} mono />}
            </div>
            {entry.summary && <Field label={t("auditLog.colDescription")} value={entry.summary} />}
            {entry.changeBefore && <JsonField label={t("auditLog.oldValues")} json={entry.changeBefore} />}
            {entry.changeAfter && <JsonField label={t("auditLog.newValues")} json={entry.changeAfter} />}
            {entry.requestUserAgent && <Field label={t("auditLog.userAgent")} value={entry.requestUserAgent} />}
          </div>
        )}

        <DialogFooter>
          <Button onClick={onClose} className="bg-brand-500 hover:bg-brand-600 text-white">
            {t("common.close")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
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
