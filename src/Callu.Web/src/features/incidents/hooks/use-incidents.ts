/** Incident query and mutation hooks. */

import { useQuery, useQueryClient } from '@tanstack/react-query';
import type { QueryClient } from '@tanstack/react-query';
import { apiQueryOptions, useApiMutation } from '@/shared/api';
import { toast } from '@/shared/utils';
import { incidentApi } from '../api/incident.api';
import type {
  IncidentFilter,
  CreateIncidentRequest,
  CreateIncidentNoteRequest,
  UpdateIncidentNoteRequest,
} from '../types/incident.types';
import { dashboardKeys } from '@/features/dashboard/hooks/use-dashboard';

export const incidentKeys = {
  all: ['incidents'] as const,
  lists: () => [...incidentKeys.all, 'list'] as const,
  list: (filters?: IncidentFilter) => [...incidentKeys.lists(), filters] as const,
  details: () => [...incidentKeys.all, 'detail'] as const,
  detail: (id: string) => [...incidentKeys.details(), id] as const,
  notes: (id: string) => [...incidentKeys.detail(id), 'notes'] as const,
  timeline: (id: string) => [...incidentKeys.detail(id), 'timeline'] as const,
  conference: (id: string) => [...incidentKeys.detail(id), 'conference'] as const,
  escalation: (id: string) => [...incidentKeys.detail(id), 'escalation'] as const,
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

/** Outbound webhook ACK delivery history; refetched on the retry job's cadence so status transitions appear without a manual refresh. */
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

/** The escalation run for an incident. */
// Polled only while it is advancing: the server sweep moves a step roughly every ten seconds, and a
// stopped or exhausted run cannot change on its own. The countdown itself ticks in the component,
// so this cadence is about the step pointer, not the clock.
export function useIncidentEscalation(incidentId: string) {
  return useQuery({
    queryKey: incidentKeys.escalation(incidentId),
    queryFn: async () => {
      const resp = await incidentApi.getEscalation(incidentId);
      return resp.data ?? null;
    },
    enabled: !!incidentId && incidentId !== 'new',
    refetchInterval: (query) => (query.state.data?.runState === 'Running' ? 15_000 : false),
    staleTime: 5_000,
  });
}

/** Invalidates everything an incident's status is counted in, not just the rows themselves. */
// The dashboard counters sit beside the list rows, so a status change that refreshes only the
// rows leaves a visibly wrong number on the screen the operator just acted on.
function invalidateIncidentViews(qc: QueryClient, scope: readonly unknown[] = incidentKeys.all) {
  qc.invalidateQueries({ queryKey: scope });
  qc.invalidateQueries({ queryKey: dashboardKeys.incidentCounts() });
  qc.invalidateQueries({ queryKey: dashboardKeys.summaries() });
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
        invalidateIncidentViews(qc, incidentKeys.lists());
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
        invalidateIncidentViews(qc);
      },
    },
  );
}

export function useInvestigateIncident() {
  const qc = useQueryClient();
  return useApiMutation(
    (id: string) => incidentApi.update(id, { status: 'Investigating' }),
    {
      successMessage: 'Incident marked as investigating',
      onSuccess: () => {
        invalidateIncidentViews(qc);
      },
    },
  );
}

export function useMitigateIncident() {
  const qc = useQueryClient();
  return useApiMutation(
    (id: string) => incidentApi.update(id, { status: 'Mitigated' }),
    {
      successMessage: 'Incident marked as mitigated',
      onSuccess: () => {
        invalidateIncidentViews(qc);
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
        invalidateIncidentViews(qc);
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
        invalidateIncidentViews(qc);
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
        invalidateIncidentViews(qc);
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
        invalidateIncidentViews(qc);
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
        invalidateIncidentViews(qc);
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
        invalidateIncidentViews(qc);
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
