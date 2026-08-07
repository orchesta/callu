import { t } from "@/shared/locales/i18n";

export interface StatusBadge {
  class: string;
  label: string;
}

/** Substring matching rather than an enum lookup, because the API's status strings have variants; an
 * unrecognised one falls through to a neutral badge showing the raw value instead of vanishing. */
export function getStatusBadge(status: string): StatusBadge {
  const s = String(status).toLowerCase();
  if (s === "operational") return { class: "bg-success-500/10 text-success-500 border-success-500/20", label: t("services.statusOperational") };
  if (s.includes("degraded")) return { class: "bg-warning-500/10 text-warning-500 border-warning-500/20", label: t("services.statusDegraded") };
  if (s.includes("partial")) return { class: "bg-warning-500/10 text-warning-500 border-warning-500/20", label: t("services.statusPartialOutage") };
  if (s.includes("major")) return { class: "bg-error-500/10 text-error-500 border-error-500/20", label: t("services.statusMajorOutage") };
  if (s.includes("maintenance")) return { class: "bg-blue-400/10 text-blue-400 border-blue-400/20", label: t("services.statusMaintenance") };
  return { class: "bg-muted/10 text-muted-foreground border-muted/20", label: status };
}

export function getCriticalityBadge(crit: string): string {
  const c = crit.toLowerCase();
  if (c === "critical") return "bg-error-500/10 text-error-500 border-error-500/20";
  if (c === "high") return "bg-warning-500/10 text-warning-500 border-warning-500/20";
  if (c === "medium") return "bg-blue-400/10 text-blue-400 border-blue-400/20";
  return "bg-muted/10 text-muted-foreground border-muted/20";
}

/** Coarse "last seen" for webhook traffic. Reads the clock, so it is not pure. */
export function formatRelativeTime(dateStr?: string): string {
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
