// Role → permission mapping mirrored from the backend role seeder. It drives UI gating only; the
// API enforces the same claims through policy-based authorization.

export const ROLES = ['Admin', 'TeamLead', 'Member', 'Viewer', 'Auditor'] as const;

export type Role = (typeof ROLES)[number];

export const PERMISSIONS = {
  ManageSettings: 'CanManageSettings',
  ManageUsers: 'CanManageUsers',
  ManageBilling: 'CanManageBilling',
  ManageIntegrations: 'CanManageIntegrations',
  ViewAuditLog: 'CanViewAuditLog',
  ManageServices: 'CanManageServices',
  ViewServices: 'CanViewServices',
  ManageWebhooks: 'CanManageWebhooks',
  ManageTeams: 'CanManageTeams',
  ViewTeams: 'CanViewTeams',
  ManageIncidents: 'CanManageIncidents',
  ViewIncidents: 'CanViewIncidents',
  AcknowledgeIncidents: 'CanAcknowledgeIncidents',
  ResolveIncidents: 'CanResolveIncidents',
  ExecuteServiceActions: 'CanExecuteServiceActions',
  ViewCallLogs: 'CanViewCallLogs',
  ManageEscalations: 'CanManageEscalations',
  ViewEscalations: 'CanViewEscalations',
  ManageSchedules: 'CanManageSchedules',
  ViewSchedules: 'CanViewSchedules',
  ManageRunbooks: 'CanManageRunbooks',
  ViewRunbooks: 'CanViewRunbooks',
  ManagePostmortems: 'CanManagePostmortems',
  ViewPostmortems: 'CanViewPostmortems',
  ViewReports: 'CanViewReports',
} as const;

export type Permission = (typeof PERMISSIONS)[keyof typeof PERMISSIONS];

export const ROLE_PERMISSIONS: Record<Role, readonly Permission[]> = {
  Admin: [
    PERMISSIONS.ManageSettings,
    PERMISSIONS.ManageUsers,
    PERMISSIONS.ManageBilling,
    PERMISSIONS.ManageIntegrations,
    PERMISSIONS.ViewAuditLog,
    PERMISSIONS.ManageServices,
    PERMISSIONS.ViewServices,
    PERMISSIONS.ManageWebhooks,
    PERMISSIONS.ManageTeams,
    PERMISSIONS.ViewTeams,
    PERMISSIONS.ManageIncidents,
    PERMISSIONS.ViewIncidents,
    PERMISSIONS.AcknowledgeIncidents,
    PERMISSIONS.ResolveIncidents,
    PERMISSIONS.ExecuteServiceActions,
    PERMISSIONS.ViewCallLogs,
    PERMISSIONS.ManageEscalations,
    PERMISSIONS.ViewEscalations,
    PERMISSIONS.ManageSchedules,
    PERMISSIONS.ViewSchedules,
    PERMISSIONS.ManageRunbooks,
    PERMISSIONS.ViewRunbooks,
    PERMISSIONS.ManagePostmortems,
    PERMISSIONS.ViewPostmortems,
    PERMISSIONS.ViewReports,
  ],
  TeamLead: [
    PERMISSIONS.ManageServices,
    PERMISSIONS.ViewServices,
    PERMISSIONS.ManageWebhooks,
    PERMISSIONS.ManageTeams,
    PERMISSIONS.ViewTeams,
    PERMISSIONS.ManageIncidents,
    PERMISSIONS.ViewIncidents,
    PERMISSIONS.AcknowledgeIncidents,
    PERMISSIONS.ResolveIncidents,
    PERMISSIONS.ExecuteServiceActions,
    PERMISSIONS.ViewCallLogs,
    PERMISSIONS.ManageEscalations,
    PERMISSIONS.ViewEscalations,
    PERMISSIONS.ManageSchedules,
    PERMISSIONS.ViewSchedules,
    PERMISSIONS.ManageRunbooks,
    PERMISSIONS.ViewRunbooks,
    PERMISSIONS.ManagePostmortems,
    PERMISSIONS.ViewPostmortems,
    PERMISSIONS.ViewReports,
  ],
  Member: [
    PERMISSIONS.ViewServices,
    PERMISSIONS.ViewTeams,
    PERMISSIONS.ViewIncidents,
    PERMISSIONS.ViewEscalations,
    PERMISSIONS.ViewSchedules,
    PERMISSIONS.ViewCallLogs,
    PERMISSIONS.AcknowledgeIncidents,
    PERMISSIONS.ResolveIncidents,
    PERMISSIONS.ExecuteServiceActions,
    PERMISSIONS.ViewRunbooks,
    PERMISSIONS.ViewPostmortems,
    PERMISSIONS.ViewReports,
  ],
  Viewer: [
    PERMISSIONS.ViewServices,
    PERMISSIONS.ViewTeams,
    PERMISSIONS.ViewIncidents,
    PERMISSIONS.ViewEscalations,
    PERMISSIONS.ViewSchedules,
    PERMISSIONS.ViewCallLogs,
    PERMISSIONS.ViewRunbooks,
    PERMISSIONS.ViewPostmortems,
    PERMISSIONS.ViewReports,
  ],
  Auditor: [
    PERMISSIONS.ViewAuditLog,
    PERMISSIONS.ViewServices,
    PERMISSIONS.ViewTeams,
    PERMISSIONS.ViewIncidents,
    PERMISSIONS.ViewEscalations,
    PERMISSIONS.ViewSchedules,
    PERMISSIONS.ViewCallLogs,
    PERMISSIONS.ViewRunbooks,
    PERMISSIONS.ViewPostmortems,
    PERMISSIONS.ViewReports,
  ],
};

export function isRole(value: string | null | undefined): value is Role {
  return !!value && (ROLES as readonly string[]).includes(value);
}

const ROLE_BY_KEY = new Map<string, Role>(ROLES.map((role) => [role.toLowerCase(), role]));

/** Roles named by the claim, which arrives comma-joined when a token carries more than one. */
function parseRoles(value: string | null | undefined): Role[] {
  if (!value) return [];
  return value
    .split(',')
    .map((part) => ROLE_BY_KEY.get(part.trim().toLowerCase()))
    .filter((role): role is Role => role !== undefined);
}

export function hasPermission(role: string | null | undefined, permission: Permission): boolean {
  return parseRoles(role).some((r) => ROLE_PERMISSIONS[r].includes(permission));
}

export const canManageIncidents = (role: string | null | undefined): boolean =>
  hasPermission(role, PERMISSIONS.ManageIncidents);

export const canAcknowledgeIncidents = (role: string | null | undefined): boolean =>
  hasPermission(role, PERMISSIONS.AcknowledgeIncidents);

export const canResolveIncidents = (role: string | null | undefined): boolean =>
  hasPermission(role, PERMISSIONS.ResolveIncidents);

export const canManageEscalations = (role: string | null | undefined): boolean =>
  hasPermission(role, PERMISSIONS.ManageEscalations);

export const canManageSchedules = (role: string | null | undefined): boolean =>
  hasPermission(role, PERMISSIONS.ManageSchedules);

export const canManageServices = (role: string | null | undefined): boolean =>
  hasPermission(role, PERMISSIONS.ManageServices);

export const canManageTeams = (role: string | null | undefined): boolean =>
  hasPermission(role, PERMISSIONS.ManageTeams);

// Route prefixes needing more than a signed-in session, each mirroring the policy the API puts on
// the endpoints that page depends on. Anything unlisted is reachable by every authenticated role.
export const ROUTE_PERMISSIONS: ReadonlyArray<{ prefix: string; permission: Permission }> = [
  { prefix: '/users', permission: PERMISSIONS.ManageUsers },
  { prefix: '/audit-logs', permission: PERMISSIONS.ViewAuditLog },
  { prefix: '/settings', permission: PERMISSIONS.ManageSettings },
  { prefix: '/maintenance', permission: PERMISSIONS.ManageSettings },
  { prefix: '/diagnostics', permission: PERMISSIONS.ManageSettings },
  { prefix: '/applications', permission: PERMISSIONS.ManageWebhooks },
];

/** True when the role may open `pathname`. Longest matching prefix wins. */
export function canAccessRoute(role: string | null | undefined, pathname: string): boolean {
  const rule = ROUTE_PERMISSIONS
    .filter(({ prefix }) => pathname === prefix || pathname.startsWith(`${prefix}/`))
    .sort((a, b) => b.prefix.length - a.prefix.length)[0];

  return rule ? hasPermission(role, rule.permission) : true;
}
