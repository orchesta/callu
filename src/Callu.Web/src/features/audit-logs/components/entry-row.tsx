import type { ReactNode } from "react";
import { Info } from "lucide-react";
import { Badge } from "@/shared/components/ui/badge";
import { Button } from "@/shared/components/ui/button";
import { t } from "@/shared/locales/i18n";
import { actionBadgeClass, formatTimestamp, outcomeBadgeClass, rowSummary } from "../utils/entry-format";
import type { AuditLogEntry } from "../types/audit-log.types";

interface AuditEntryRowProps {
  entry: AuditLogEntry;
  onSelect: (entry: AuditLogEntry) => void;
  /** Cell placed after the action badge; the flat list puts the entity there, the trail omits it. */
  entityCell?: ReactNode;
}

export function AuditEntryRow({ entry, onSelect, entityCell }: AuditEntryRowProps) {
  return (
    <tr
      className="border-b border-border hover:bg-surface-light/20 transition-colors cursor-pointer"
      tabIndex={0}
      role="button"
      onClick={() => onSelect(entry)}
      onKeyDown={(e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); onSelect(entry); } }}
    >
      <td className="p-4 text-sm text-muted-foreground whitespace-nowrap">{formatTimestamp(entry.createdAt)}</td>
      <td className="p-4 text-sm">{entry.actorDisplayName || entry.actorId || t("auditLog.system")}</td>
      <td className="p-4">
        <Badge className={`${actionBadgeClass(entry.action)} border text-xs`}>{entry.action}</Badge>
        {entry.outcome && entry.outcome !== "Success" && (
          <Badge className={`${outcomeBadgeClass(entry.outcome)} border text-xs ml-1`}>{entry.outcome}</Badge>
        )}
      </td>
      {entityCell}
      <td className="p-4 text-sm text-muted-foreground max-w-[280px] truncate" title={rowSummary(entry)}>
        {rowSummary(entry)}
      </td>
      <td className="p-4">
        <Button
          size="sm"
          variant="ghost"
          title={t("auditLog.viewDetails")}
          onClick={(e) => { e.stopPropagation(); onSelect(entry); }}
        >
          <Info className="w-4 h-4" />
        </Button>
      </td>
    </tr>
  );
}
