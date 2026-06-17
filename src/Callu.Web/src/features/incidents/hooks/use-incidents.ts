/**
 * Incident React Query hooks.
 *
 * Provides type-safe queries and mutations for all incident operations.
 * Uses incidentQueries + useQuery / useApiMutation (ApiResponse unwrap).
 */

import { useQuery, useQueryClient } from '@tanstack/react-query';
import { apiQueryOptions, useApiMutation } from '@/shared/api';
import { toast } from '@/shared/utils';
import { incidentApi } from '../api/incident.api';
import type {
  IncidentFilter,
  CreateIncidentRequest,
  UpdateIncidentRequest,
  CreateIncidentNoteRequest,
  UpdateIncidentNoteRequest,
} from '../types/incident.types';

export const incidentKeys = {
  all: ['incidents'] as const,
  lists: () => [...incidentKeys.all, 'list'] as const,
  list: (filters?: IncidentFilter) => [...incidentKeys.lists(), filters] as const,
  details: () => [...incidentKeys.all, 'detail'] as const,
  detail: (id: string) => [...incidentKeys.details(), id] as const,
  notes: (id: string) => [...incidentKeys.detail(id), 'notes'] as const,
  timeline: (id: string) => [...incidentKeys.detail(id), 'timeline'] as const,
  conference: (id: string) => [...incidentKeys.detail(id), 'conference'] as const,
};

export const incidentQueries = {
  list: (filters?: IncidentFilter) =>
    apiQueryOptions(incidentKeys.list(filters), () => incidentApi.getAll(filters), { staleTime: 30_000 }),

  detail: (id: string) =>
    apiQueryOptions(incidentKeys.detail(id), () => incidentApi.getById(id), {
      enabled: !!id && id !== 'new',
    }),

  notes: (incidentId: string) =>
    apiQueryOptions(incidentKeys.notes(incidentId), () => incidentApi.getNotes(incidentId), {
      enabled: !!incidentId,
    }),

  timeline: (incidentId: string) =>
    apiQueryOptions(incidentKeys.timeline(incidentId), () => incidentApi.getTimeline(incidentId), {
      enabled: !!incidentId,
    }),

  conference: (incidentId: string) =>
    apiQueryOptions(incidentKeys.conference(incidentId), () => incidentApi.getActiveConference(incidentId), {
      enabled: !!incidentId,
      refetchInterval: 15_000,
    }),
};

/** Paginated incident list with filters */
export function useIncidents(filters?: IncidentFilter) {
  return useQuery(incidentQueries.list(filters));
}

/** Single incident detail */
export function useIncident(id: string) {
  return useQuery(incidentQueries.detail(id));
}

/** Notes for an incident */
export function useIncidentNotes(incidentId: string) {
  return useQuery(incidentQueries.notes(incidentId));
}

/** Timeline for an incident */
export function useIncidentTimeline(incidentId: string) {
  return useQuery(incidentQueries.timeline(incidentId));
}

/** Active video conference for an incident */
export function useIncidentConference(incidentId: string) {
  return useQuery(incidentQueries.conference(incidentId));
}

/**
 * Outbound webhook ACK delivery history for an incident. 30-second refetch
 * matches the retry job cadence so the UI surfaces "Retrying → Succeeded /
 * Failed" transitions without manual refresh. Fix 10.P1-7.
 */
export function useWebhookDeliveries(incidentId: string, limit = 20) {
  return useQuery({
    queryKey: [...incidentKeys.detail(incidentId), 'webhook-deliveries'],
    queryFn: async () => {
      const resp = await incidentApi.getWebhookDeliveries(incidentId, limit);
      return resp.data ?? [];
    },
    enabled: !!incidentId,
    refetchInterval: 30_000,
    staleTime: 15_000,
  });
}

export function useCreateIncident() {
  const qc = useQueryClient();
  return useApiMutation(
    (data: CreateIncidentRequest) => incidentApi.create(data),
    {
      successMessage: false,
      onSuccess: (result) => {
        if (result?.outcome === 'Suppressed') {
          toast.info(
            result.reason
              ? `Suppressed by maintenance window: ${result.reason}`
              : 'Suppressed by an active maintenance window',
          );
          return;
        }
        toast.success('Incident created successfully');
        qc.invalidateQueries({ queryKey: incidentKeys.lists() });
      },
    },
  );
}

export function useUpdateIncident() {
  const qc = useQueryClient();
  return useApiMutation(
    ({ id, ...data }: { id: string } & UpdateIncidentRequest) =>
      incidentApi.update(id, data),
    {
      successMessage: 'Incident updated successfully',
      onSuccess: () => {
        qc.invalidateQueries({ queryKey: incidentKeys.all });
      },
    },
  );
}

export function useDeleteIncident() {
  const qc = useQueryClient();
  return useApiMutation(
    (id: string) => incidentApi.delete(id),
    {
      successMessage: 'Incident deleted successfully',
      onSuccess: () => {
        qc.invalidateQueries({ queryKey: incidentKeys.lists() });
      },
    },
  );
}

export function useAcknowledgeIncident() {
  const qc = useQueryClient();
  return useApiMutation(
    (id: string) => incidentApi.acknowledge(id),
    {
      successMessage: 'Incident acknowledged',
      onSuccess: () => {
        qc.invalidateQueries({ queryKey: incidentKeys.all });
      },
    },
  );
}

export function useResolveIncident() {
  const qc = useQueryClient();
  return useApiMutation(
    (id: string) => incidentApi.resolve(id),
    {
      successMessage: 'Incident resolved',
      onSuccess: () => {
        qc.invalidateQueries({ queryKey: incidentKeys.all });
      },
    },
  );
}

export function useCloseIncident() {
  const qc = useQueryClient();
  return useApiMutation(
    (id: string) => incidentApi.close(id),
    {
      successMessage: 'Incident closed',
      onSuccess: () => {
        qc.invalidateQueries({ queryKey: incidentKeys.all });
      },
    },
  );
}

export function useReopenIncident() {
  const qc = useQueryClient();
  return useApiMutation(
    (id: string) => incidentApi.reopen(id),
    {
      successMessage: 'Incident reopened',
      onSuccess: () => {
        qc.invalidateQueries({ queryKey: incidentKeys.all });
      },
    },
  );
}

export function useEscalateIncident() {
  const qc = useQueryClient();
  return useApiMutation(
    ({ id, reason }: { id: string; reason?: string }) =>
      incidentApi.escalate(id, reason),
    {
      successMessage: 'Incident escalated',
      onSuccess: () => {
        qc.invalidateQueries({ queryKey: incidentKeys.all });
      },
    },
  );
}

export function useReassignIncident() {
  const qc = useQueryClient();
  return useApiMutation(
    ({ id, targetUserId }: { id: string; targetUserId: string }) =>
      incidentApi.reassign(id, targetUserId),
    {
      successMessage: 'Incident reassigned',
      onSuccess: (_, { id }) => {
        qc.invalidateQueries({ queryKey: incidentKeys.detail(id) });
        qc.invalidateQueries({ queryKey: incidentKeys.lists() });
      },
    },
  );
}

export function useBulkAcknowledge() {
  const qc = useQueryClient();
  return useApiMutation(
    (incidentIds: string[]) => incidentApi.bulkAcknowledge(incidentIds),
    {
      successMessage: 'Incidents acknowledged',
      onSuccess: () => {
        qc.invalidateQueries({ queryKey: incidentKeys.all });
      },
    },
  );
}

export function useBulkResolve() {
  const qc = useQueryClient();
  return useApiMutation(
    (incidentIds: string[]) => incidentApi.bulkResolve(incidentIds),
    {
      successMessage: 'Incidents resolved',
      onSuccess: () => {
        qc.invalidateQueries({ queryKey: incidentKeys.all });
      },
    },
  );
}

export function useAddNote() {
  const qc = useQueryClient();
  return useApiMutation(
    ({ incidentId, ...data }: { incidentId: string } & CreateIncidentNoteRequest) =>
      incidentApi.addNote(incidentId, data),
    {
      successMessage: 'Note added',
      onSuccess: (_, { incidentId }) => {
        qc.invalidateQueries({ queryKey: incidentKeys.notes(incidentId) });
        qc.invalidateQueries({ queryKey: incidentKeys.timeline(incidentId) });
      },
    },
  );
}

export function useUpdateNote() {
  const qc = useQueryClient();
  return useApiMutation(
    ({ noteId, ...data }: { noteId: string; incidentId: string } & UpdateIncidentNoteRequest) =>
      incidentApi.updateNote(noteId, data),
    {
      successMessage: 'Note updated',
      onSuccess: (_, { incidentId }) => {
        qc.invalidateQueries({ queryKey: incidentKeys.notes(incidentId) });
      },
    },
  );
}

export function useDeleteNote() {
  const qc = useQueryClient();
  return useApiMutation(
    ({ noteId }: { noteId: string; incidentId: string }) =>
      incidentApi.deleteNote(noteId),
    {
      successMessage: 'Note deleted',
      onSuccess: (_, { incidentId }) => {
        qc.invalidateQueries({ queryKey: incidentKeys.notes(incidentId) });
        qc.invalidateQueries({ queryKey: incidentKeys.timeline(incidentId) });
      },
    },
  );
}
