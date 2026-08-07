// A notification's actionUrl comes from the server, so anything outside the SPA route table or off
// this origin falls back to the notifications list instead of a 404.

const KNOWN_PREFIXES = [
  "/dashboard",
  "/notifications",
  "/incidents",
  "/conferences",
  "/conference/",
  "/profile",
  "/users",
  "/services",
  "/escalations",
  "/schedules",
  "/teams",
  "/call-logs",
  "/audit-logs",
  "/reports",
  "/postmortems",
  "/runbooks",
  "/maintenance",
  "/settings",
  "/status",
];

function toAppPath(actionUrl: string): string | null {
  const raw = actionUrl.trim();
  if (!raw) return null;

  // Absolute URLs (e.g. mis-set org BaseUrl → http://localhost:3000/incidents/…) keep only the path.
  if (/^https?:\/\//i.test(raw)) {
    try {
      const parsed = new URL(raw);
      return `${parsed.pathname}${parsed.search}`;
    } catch {
      return null;
    }
  }

  if (raw.startsWith("//") || !raw.startsWith("/")) return null;
  return raw;
}

export function resolveActionUrl(actionUrl: string): string {
  const url = toAppPath(actionUrl);
  if (!url) return "/notifications";
  const matches = KNOWN_PREFIXES.some(
    (p) => url === p || url.startsWith(`${p}/`) || url.startsWith(`${p}?`) || (p.endsWith("/") && url.startsWith(p)),
  );
  return matches ? url : "/notifications";
}
