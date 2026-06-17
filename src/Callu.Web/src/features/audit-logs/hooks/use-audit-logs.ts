/**
 * Audit Log React Query hooks.
 */

import { useQuery } from '@tanstack/react-query';
import { apiQueryOptions } from '@/shared/api';
import { auditLogApi } from '../api/audit-log.api';
import type { AuditLogFilters } from '../types/audit-log.types';

export const auditLogKeys = {
  all: ['audit-logs'] as const,
  list: (filters: AuditLogFilters) => [...auditLogKeys.all, 'list', filters] as const,
};

export const auditLogQueries = {
  list: (filters: AuditLogFilters) =>
    apiQueryOptions(auditLogKeys.list(filters), () => auditLogApi.getAll(filters), { staleTime: 30_000 }),
};

/** Recent audit-log entries, optionally filtered by entity type. */
export function useAuditLogs(filters: AuditLogFilters = {}) {
  return useQuery(auditLogQueries.list(filters));
}
