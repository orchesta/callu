
import { apiClient } from '@/shared/api';
import type {
  TraceDetail,
  TraceSearchFilters,
  TraceSummary,
  TracingOverview,
  TracingStatus,
} from '../types/tracing.types';

const BASE = '/api/v1/diagnostics/tracing';

export const tracingApi = {
  getStatus: () => apiClient.get<TracingStatus>(`${BASE}/status`),

  search: (filters: TraceSearchFilters = {}) =>
    apiClient.get<TraceSummary[]>(`${BASE}/traces`, {
      params: {
        service: filters.service,
        operation: filters.operation,
        lookbackMinutes: filters.lookbackMinutes,
        limit: filters.limit,
        onlyErrors: filters.onlyErrors,
        minDurationMs: filters.minDurationMs,
      },
    }),

  getOverview: (service: string, lookbackMinutes: number) =>
    apiClient.get<TracingOverview>(`${BASE}/overview`, {
      params: { service, lookbackMinutes },
    }),

  getTrace: (traceId: string) => apiClient.get<TraceDetail>(`${BASE}/traces/${traceId}`),
};
