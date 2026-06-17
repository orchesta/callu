/**
 * Audit Log Types — mirrors the BE AuditLog entity (Callu.Domain.Entities.AuditLog),
 * serialized camelCase. The /audit-logs endpoint returns a flat, newest-first array.
 */

/** AuditAction values from the BE enum, serialized as strings. */
export type AuditActionValue =
  | 'Created'
  | 'Updated'
  | 'Deleted'
  | 'Viewed'
  | 'Login'
  | 'Logout'
  | 'PasswordChanged'
  | 'RoleAssigned'
  | 'RoleRemoved'
  | 'SettingsChanged';

export interface AuditLogEntry {
  id: string;
  userId?: string;
  userName?: string;
  action: AuditActionValue | string;
  entityType: string;
  entityId?: string;
  description?: string;
  oldValues?: string;
  newValues?: string;
  ipAddress?: string;
  userAgent?: string;
  requestPath?: string;
  createdAt: string;
}

export interface AuditLogFilters {
  entityName?: string;
  entityId?: string;
  count?: number;
}
