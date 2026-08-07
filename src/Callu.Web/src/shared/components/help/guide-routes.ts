// Route → help-guide key, first match wins, so specific paths come before their parents. Guide copy
// lives in the locale files under `help.<key>.*`.
const GUIDE_ROUTES: { test: RegExp; key: string }[] = [
  { test: /^\/dashboard/, key: "dashboard" },
  { test: /^\/incidents\/[^/]+$/, key: "incidentDetail" },
  { test: /^\/incidents/, key: "incidents" },
  { test: /^\/conferences/, key: "conferences" },
  { test: /^\/services/, key: "services" },
  { test: /^\/escalations/, key: "escalations" },
  { test: /^\/schedules/, key: "schedules" },
  { test: /^\/teams/, key: "teams" },
  { test: /^\/reports/, key: "reports" },
  { test: /^\/postmortems/, key: "postmortems" },
  { test: /^\/runbooks/, key: "runbooks" },
  { test: /^\/call-logs/, key: "callLogs" },
  { test: /^\/maintenance/, key: "maintenance" },
  { test: /^\/audit-logs/, key: "auditLogs" },
  { test: /^\/diagnostics/, key: "diagnostics" },
  { test: /^\/users/, key: "users" },
  { test: /^\/settings\/communications/, key: "communications" },
  { test: /^\/settings\/email-templates/, key: "emailTemplates" },
  { test: /^\/settings\/status-page/, key: "statusPage" },
  { test: /^\/settings/, key: "settings" },
  { test: /^\/notifications/, key: "notifications" },
  { test: /^\/profile/, key: "profile" },
];

export function resolveGuideKey(pathname: string): string | null {
  return GUIDE_ROUTES.find((g) => g.test.test(pathname))?.key ?? null;
}

export interface RelatedPage {
  to: string;
  labelKey: string;
}

// Paths and labels rather than translated strings: a route is not language-specific, and the
// sidebar already names every page in both languages.
const PAGE: Record<string, RelatedPage> = {
  auditLogs: { to: "/audit-logs", labelKey: "nav.auditLog" },
  callLogs: { to: "/call-logs", labelKey: "nav.callLogs" },
  communications: { to: "/settings/communications", labelKey: "nav.communications" },
  conferences: { to: "/conferences", labelKey: "nav.conferences" },
  dashboard: { to: "/dashboard", labelKey: "nav.dashboard" },
  diagnostics: { to: "/diagnostics", labelKey: "nav.diagnostics" },
  emailTemplates: { to: "/settings/email-templates", labelKey: "nav.emailTemplates" },
  escalations: { to: "/escalations", labelKey: "nav.escalations" },
  incidents: { to: "/incidents", labelKey: "nav.incidents" },
  maintenance: { to: "/maintenance", labelKey: "nav.maintenance" },
  notifications: { to: "/notifications", labelKey: "nav.notifications" },
  postmortems: { to: "/postmortems", labelKey: "nav.postmortems" },
  profile: { to: "/profile", labelKey: "nav.profile" },
  reports: { to: "/reports", labelKey: "nav.reports" },
  runbooks: { to: "/runbooks", labelKey: "nav.runbooks" },
  schedules: { to: "/schedules", labelKey: "nav.schedules" },
  services: { to: "/services", labelKey: "nav.services" },
  settings: { to: "/settings", labelKey: "nav.settings" },
  statusPage: { to: "/settings/status-page", labelKey: "nav.statusPage" },
  teams: { to: "/teams", labelKey: "nav.teams" },
  users: { to: "/users", labelKey: "nav.users" },
};

/** The pages an operator has to visit next for the one on screen to actually do anything. */
// Paging only works when services, escalation policies, schedules and providers all line up, and
// the page an operator is stuck on is rarely the one that is misconfigured.
const RELATED: Record<string, string[]> = {
  dashboard: ["incidents", "reports", "services"],
  incidents: ["services", "escalations", "maintenance"],
  incidentDetail: ["escalations", "postmortems", "runbooks"],
  conferences: ["incidents", "communications"],
  services: ["incidents", "escalations", "statusPage"],
  escalations: ["schedules", "teams", "communications"],
  schedules: ["teams", "escalations", "users"],
  teams: ["users", "schedules", "escalations"],
  reports: ["incidents", "postmortems"],
  postmortems: ["incidents", "runbooks"],
  runbooks: ["incidents", "postmortems"],
  callLogs: ["communications", "profile"],
  maintenance: ["services", "incidents"],
  auditLogs: ["users", "settings"],
  diagnostics: ["settings", "auditLogs"],
  users: ["teams", "profile"],
  communications: ["profile", "escalations", "callLogs"],
  emailTemplates: ["settings", "notifications"],
  statusPage: ["services", "incidents"],
  settings: ["communications", "users", "auditLogs"],
  notifications: ["profile", "communications"],
  profile: ["notifications", "communications"],
};

export function relatedPages(guideKey: string): RelatedPage[] {
  return (RELATED[guideKey] ?? []).map((key) => PAGE[key]).filter(Boolean);
}
