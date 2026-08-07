/**
 * Audit Log React Query hooks.
 */

import { useQuery } from '@tanstack/react-query';
import { apiQueryOptions, useApiMutation } from '@/shared/api';
import { auditLogApi } from '../api/audit-log.api';
import type { AuditLogFilters, AuditLogSearchFilter } from '../types/audit-log.types';

export const auditLogKeys = {
  all: ['audit-logs'] as const,
  list: (filters: AuditLogFilters) => [...auditLogKeys.all, 'list', filters] as const,
  search: (filter: AuditLogSearchFilter) => [...auditLogKeys.all, 'search', filter] as const,
};

export const auditLogQueries = {
  list: (filters: AuditLogFilters) =>
    apiQueryOptions(auditLogKeys.list(filters), () => auditLogApi.getAll(filters), { staleTime: 30_000 }),
  search: (filter: AuditLogSearchFilter) =>
    apiQueryOptions(auditLogKeys.search(filter), () => auditLogApi.search(filter), { staleTime: 30_000 }),
};

/** The filtered, paged trail. Every filter belongs in the key or two views share one cache entry. */
export function useAuditLogSearch(filter: AuditLogSearchFilter) {
  return useQuery(auditLogQueries.search(filter));
}

/** Replay the chain on demand. Not a query: it is an action an auditor chooses to run. */
export function useVerifyAuditChain() {
  return useApiMutation(() => auditLogApi.verify(), { successMessage: false });
}
