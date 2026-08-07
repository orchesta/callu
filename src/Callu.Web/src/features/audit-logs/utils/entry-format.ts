import type { AuditLogEntry } from "../types/audit-log.types";
import { dateLocale } from "@/shared/utils/datetime";

export function actionBadgeClass(action: string) {
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

export function outcomeBadgeClass(outcome: string) {
  switch (outcome) {
    case "Failure":
      return "bg-error-500/10 text-error-500 border-error-500/20";
    case "Partial":
      return "bg-warning-500/10 text-warning-500 border-warning-500/20";
    default:
      return "bg-muted/10 text-muted-foreground border-muted/20";
  }
}

export function formatTimestamp(s: string) {
  return new Date(s).toLocaleString(dateLocale(), {
    month: "short",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}

/** What the row says it is, falling back to the values when nothing wrote a summary. */
// Escalation-outcome rows carry their whole content in changeAfter and left the column empty, so
// the list showed a dash for exactly the rows that say nobody was reached.
export function rowSummary(row: AuditLogEntry): string {
  if (row.summary?.trim()) return row.summary;

  const before = row.changeBefore?.trim();
  const after = row.changeAfter?.trim();
  if (before && after) return `${before} → ${after}`;
  return after || before || "—";
}
