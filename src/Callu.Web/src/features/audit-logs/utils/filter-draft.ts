import type { AuditActionValue } from "../types/audit-log.types";

export const ANY_ACTION = "__any__";

/** The from/to/action filter both audit screens offer. */
export interface AuditFilterDraft {
  from: string;
  to: string;
  action: string;
}

export const EMPTY_AUDIT_FILTER: AuditFilterDraft = { from: "", to: "", action: ANY_ACTION };

// A date input gives a local calendar day; the trail is stored in UTC.
export function dayStart(value: string): string | undefined {
  return value ? new Date(value + "T00:00:00").toISOString() : undefined;
}

export function dayEnd(value: string): string | undefined {
  return value ? new Date(value + "T23:59:59.999").toISOString() : undefined;
}

export function chosenAction(action: string): AuditActionValue | undefined {
  return action === ANY_ACTION ? undefined : (action as AuditActionValue);
}
