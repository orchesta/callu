export { AuthProvider, useAuth } from './auth.context';
export { authService, AuthService, RefreshUnavailableError } from './auth.service';
export type { AuthUser, TokenPayload } from './auth.service';

export {
    ROLES,
    PERMISSIONS,
    ROLE_PERMISSIONS,
    ROUTE_PERMISSIONS,
    isRole,
    hasPermission,
    canAccessRoute,
    canManageIncidents,
    canAcknowledgeIncidents,
    canResolveIncidents,
    canManageEscalations,
    canManageSchedules,
    canManageServices,
    canManageTeams,
} from './roles';
export type { Role, Permission } from './roles';
