/**
 * Centralized incident severity and status style helpers.
 *
 * These were previously duplicated across:
 *   - features/incidents/components/list.tsx
 *   - features/incidents/components/detail.tsx
 *   - features/dashboard/components/dashboard-presentation.tsx
 */

export interface SeverityConfig {
  /** Left border + badge classes for list items */
  border: string;
  /** Badge background/text/border classes */
  badge: string;
  /** Hover shadow glow */
  glow: string;
  /** Hex color for inline use (charts, dots) */
  hex: string;
  /** Tailwind background class (safe, static) */
  bg: string;
}

const SEVERITY_CONFIG: Record<string, SeverityConfig> = {
  Critical: {
    border: 'border-l-4 border-l-error-500',
    badge: 'bg-error-500/10 text-error-500 border-error-500/20',
    glow: 'hover:shadow-error-500/20',
    hex: '#FF4D4D',
    bg: 'bg-error-500/10',
  },
  High: {
    border: 'border-l-4 border-l-warning-500',
    badge: 'bg-warning-500/10 text-warning-500 border-warning-500/20',
    glow: 'hover:shadow-warning-500/20',
    hex: '#FB923C',
    bg: 'bg-warning-500/10',
  },
  Medium: {
    border: 'border-l-4 border-l-blue-400',
    badge: 'bg-blue-400/10 text-blue-400 border-blue-400/20',
    glow: 'hover:shadow-blue-400/20',
    hex: '#3E7BFA',
    bg: 'bg-blue-400/10',
  },
  Low: {
    border: 'border-l-4 border-l-muted',
    badge: 'bg-muted/10 text-muted-foreground border-muted/20',
    glow: '',
    hex: '#94A3B8',
    bg: 'bg-muted/10',
  },
};

const DEFAULT_SEVERITY: SeverityConfig = {
  border: 'border-l-4 border-l-muted',
  badge: 'bg-muted/10 text-muted-foreground border-muted/20',
  glow: '',
  hex: '#94A3B8',
  bg: 'bg-muted/10',
};

export function getSeverityConfig(severity: string): SeverityConfig {
  return SEVERITY_CONFIG[severity] ?? DEFAULT_SEVERITY;
}

/** Shorthand: just the badge class string */
export function getSeverityBadge(severity: string): string {
  return `${getSeverityConfig(severity).badge} border`;
}

const STATUS_CONFIG: Record<string, string> = {
  Open: 'bg-error-500/10 text-error-500 border-error-500/20',
  Triggered: 'bg-error-500/10 text-error-500 border-error-500/20',
  Acknowledged: 'bg-warning-500/10 text-warning-500 border-warning-500/20',
  Investigating: 'bg-blue-400/10 text-blue-400 border-blue-400/20',
  Mitigated: 'bg-cyan-400/10 text-cyan-400 border-cyan-400/20',
  Resolved: 'bg-success-500/10 text-success-500 border-success-500/20',
  Closed: 'bg-muted/10 text-muted-foreground border-muted/20',
};

const DEFAULT_STATUS = 'bg-muted/10 text-muted-foreground border-muted/20';

export function getStatusBadge(status: string): string {
  const key = Object.keys(STATUS_CONFIG).find(
    (k) => k.toLowerCase() === status?.toLowerCase(),
  );
  return `${key ? STATUS_CONFIG[key] : DEFAULT_STATUS} border`;
}

/** Used in the incident create modal for the visual severity picker.
 *  These MUST remain as static strings so Tailwind can detect them at build time. */
export const SEVERITY_PICKER_OPTIONS = [
  {
    value: 'critical' as const,
    label: 'Critical',
    badgeHex: '#FF4D4D',
    dotClass: 'bg-error-500',
    activeClasses: 'border-error-500 bg-error-500/10',
    activeTextClass: 'text-error-500',
  },
  {
    value: 'high' as const,
    label: 'High',
    badgeHex: '#FB923C',
    dotClass: 'bg-warning-500',
    activeClasses: 'border-warning-500 bg-warning-500/10',
    activeTextClass: 'text-warning-500',
  },
  {
    value: 'medium' as const,
    label: 'Medium',
    badgeHex: '#3E7BFA',
    dotClass: 'bg-brand-500',
    activeClasses: 'border-brand-500 bg-brand-500/10',
    activeTextClass: 'text-brand-500',
  },
  {
    value: 'low' as const,
    label: 'Low',
    badgeHex: '#94A3B8',
    dotClass: 'bg-slate-400',
    activeClasses: 'border-slate-400 bg-slate-400/10',
    activeTextClass: 'text-slate-400',
  },
] as const;
