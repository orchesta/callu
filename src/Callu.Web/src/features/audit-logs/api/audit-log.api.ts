/**
 * Audit Log API module — connects to AuditLogsController.
 *   GET /api/v1/audit-logs?entityName=&entityId=&count=  (policy: CanViewAuditLog)
 */

import { apiClient } from '@/shared/api';
import type { AuditLogEntry, AuditLogFilters } from '../types/audit-log.types';

const BASE = '/api/v1/audit-logs';

export const auditLogApi = {
  /** GET /audit-logs — recent entries (newest first), optionally filtered by entity. */
  getAll: (filters: AuditLogFilters = {}) =>
    apiClient.get<AuditLogEntry[]>(BASE, {
      params: {
        entityName: filters.entityName,
        entityId: filters.entityId,
        count: filters.count,
      },
    }),
};
