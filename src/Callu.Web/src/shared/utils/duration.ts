/** Formats a minute count the way the dashboard's server-side formatter does. */
// Sub-minute has to keep its seconds: rounding a 26-second acknowledgement to "0m" reads as
// "never acknowledged", which is the opposite of what happened.
export function formatMinutes(minutes: number): string {
  if (!Number.isFinite(minutes) || minutes < 0) return "—";
  if (minutes < 1) return `${Math.round(minutes * 60)}s`;
  if (minutes < 60) return `${Math.trunc(minutes)}m`;
  if (minutes < 1440) return `${Math.trunc(minutes / 60)}h ${Math.trunc(minutes % 60)}m`;
  return `${Math.trunc(minutes / 1440)}d`;
}
