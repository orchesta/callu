
import { useQuery } from '@tanstack/react-query';
import { apiQueryOptions } from '@/shared/api';
import { diagnosticsApi } from '../api/diagnostics.api';
import { tracingApi } from '../api/tracing.api';
import type { TraceSearchFilters } from '../types/tracing.types';

export const tracingKeys = {
  all: ['diagnostics', 'tracing'] as const,
  status: () => [...tracingKeys.all, 'status'] as const,
  list: (filters: TraceSearchFilters) => [...tracingKeys.all, 'list', filters] as const,
  overview: (service: string, lookbackMinutes: number) =>
    [...tracingKeys.all, 'overview', service, lookbackMinutes] as const,
  detail: (traceId: string) => [...tracingKeys.all, 'detail', traceId] as const,
};

export const diagnosticsKeys = {
  all: ['diagnostics'] as const,
  build: () => [...diagnosticsKeys.all, 'build'] as const,
};

export const tracingQueries = {
  status: () =>
    apiQueryOptions(tracingKeys.status(), () => tracingApi.getStatus(), { staleTime: 60_000 }),

  list: (filters: TraceSearchFilters) =>
    apiQueryOptions(tracingKeys.list(filters), () => tracingApi.search(filters), { staleTime: 10_000 }),

  overview: (service: string, lookbackMinutes: number) =>
    apiQueryOptions(
      tracingKeys.overview(service, lookbackMinutes),
      () => tracingApi.getOverview(service, lookbackMinutes),
      { staleTime: 10_000 },
    ),

  detail: (traceId: string) =>
    apiQueryOptions(tracingKeys.detail(traceId), () => tracingApi.getTrace(traceId), { staleTime: Infinity }),
};

export const diagnosticsQueries = {
  build: () =>
    apiQueryOptions(diagnosticsKeys.build(), () => diagnosticsApi.getBuild(), {
      staleTime: Infinity,
    }),
};

export function useBuildIdentity() {
  return useQuery(diagnosticsQueries.build());
}

export function useTracingOverview(service: string, lookbackMinutes: number) {
  return useQuery({
    ...tracingQueries.overview(service, lookbackMinutes),
    enabled: Boolean(service),
  });
}

export function useTracingStatus() {
  return useQuery(tracingQueries.status());
}

export function useTraces(filters: TraceSearchFilters) {
  return useQuery({
    ...tracingQueries.list(filters),
    enabled: Boolean(filters.service),
  });
}

export function useTrace(traceId: string | null) {
  return useQuery({
    ...tracingQueries.detail(traceId ?? ''),
    enabled: Boolean(traceId),
  });
}
