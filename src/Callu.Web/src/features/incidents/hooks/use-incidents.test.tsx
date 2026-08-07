import type { ReactNode } from "react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

/** Create has two successful outcomes that arrive as one success, so these pin that a suppressed
 * incident — nothing written, nobody paged — is never reported to the reporter as created. */

const create = vi.fn();
const reassign = vi.fn();
const addNote = vi.fn();
const acknowledge = vi.fn();
const resolve = vi.fn();
const update = vi.fn();

vi.mock("../api/incident.api", () => ({
  incidentApi: {
    create: (data: unknown) => create(data),
    reassign: (id: string, targetUserId: string) => reassign(id, targetUserId),
    addNote: (incidentId: string, data: unknown) => addNote(incidentId, data),
    acknowledge: (id: string) => acknowledge(id),
    resolve: (id: string) => resolve(id),
    update: (id: string, data: unknown) => update(id, data),
  },
}));

const toast = { success: vi.fn(), error: vi.fn(), warning: vi.fn(), info: vi.fn() };
vi.mock("@/shared/utils", () => ({ toast }));
vi.mock("@/shared/utils/toast", () => ({ toast }));

const {
  useCreateIncident,
  useReassignIncident,
  useAddNote,
  useAcknowledgeIncident,
  useInvestigateIncident,
  useMitigateIncident,
  useResolveIncident,
  incidentKeys,
} = await import("./use-incidents");
const { dashboardKeys } = await import("@/features/dashboard/hooks/use-dashboard");

const ok = <T,>(data: T) => Promise.resolve({ success: true, data });

let client: QueryClient;
let invalidateSpy: ReturnType<typeof vi.spyOn>;

function wrapper({ children }: { children: ReactNode }) {
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}

/** The query keys a mutation asked to be refetched, in call order. */
function invalidatedKeys(): unknown[] {
  const calls = invalidateSpy.mock.calls as Array<[{ queryKey?: unknown }?]>;
  return calls.map((call) => call[0]?.queryKey);
}

beforeEach(() => {
  vi.clearAllMocks();
  client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  invalidateSpy = vi.spyOn(client, "invalidateQueries");
});

describe("useCreateIncident — a suppressed incident is not a created one", () => {
  it("reports a created incident and refreshes the list", async () => {
    create.mockImplementation(() =>
      ok({ outcome: "Created", incident: { id: "inc-1", title: "DB down" }, reason: undefined }),
    );

    const { result } = renderHook(() => useCreateIncident(), { wrapper });
    await result.current.mutateAsync({ title: "DB down", severity: "High" });

    expect(toast.success).toHaveBeenCalledWith("Incident created successfully");
    expect(toast.info).not.toHaveBeenCalled();
    // A row exists now, so the list on screen is stale — and so are the counters beside it.
    expect(invalidatedKeys()).toContainEqual(incidentKeys.lists());
    expect(invalidatedKeys()).toContainEqual(dashboardKeys.incidentCounts());
  });

  it("never says 'created' when a maintenance window suppressed it, and names the window", async () => {
    // 202 Accepted: the API considered it and deliberately wrote nothing. Nobody was paged.
    create.mockImplementation(() =>
      ok({
        outcome: "Suppressed",
        incident: null,
        reason: "DB migration 02:00-04:00",
      }),
    );

    const { result } = renderHook(() => useCreateIncident(), { wrapper });
    await result.current.mutateAsync({ title: "DB down", severity: "High" });

    // The sentence that must never appear: it would have the reporter waiting on a page that
    // was never sent.
    expect(toast.success).not.toHaveBeenCalled();
    expect(toast.info).toHaveBeenCalledWith(
      "Suppressed by maintenance window: DB migration 02:00-04:00",
    );
    // Nothing was written, so there is nothing for the list to refetch.
    expect(invalidateSpy).not.toHaveBeenCalled();
  });

  it("still says it was suppressed when the API gives no window name", async () => {
    create.mockImplementation(() => ok({ outcome: "Suppressed", incident: null }));

    const { result } = renderHook(() => useCreateIncident(), { wrapper });
    await result.current.mutateAsync({ title: "DB down", severity: "High" });

    expect(toast.success).not.toHaveBeenCalled();
    expect(toast.info).toHaveBeenCalledWith("Suppressed by an active maintenance window");
    expect(invalidateSpy).not.toHaveBeenCalled();
  });

  it("surfaces a rejected create as an error and refreshes nothing", async () => {
    create.mockImplementation(() =>
      Promise.resolve({ success: false, message: "Service not found" }),
    );

    const { result } = renderHook(() => useCreateIncident(), { wrapper });
    await expect(
      result.current.mutateAsync({ title: "DB down", severity: "High" }),
    ).rejects.toThrow("Service not found");

    await waitFor(() => expect(toast.error).toHaveBeenCalled());
    expect(toast.success).not.toHaveBeenCalled();
    expect(toast.info).not.toHaveBeenCalled();
    expect(invalidateSpy).not.toHaveBeenCalled();
  });
});

/** The counters sit beside the rows on the same screen, so a status change that refreshes only
 * the rows leaves a number the operator can see is wrong. */
describe("status changes refresh the counters, not only the rows", () => {
  async function expectCountInvalidation(hook: () => { mutateAsync: (id: string) => Promise<unknown> }) {
    const { result } = renderHook(hook, { wrapper });
    await result.current.mutateAsync("inc-1");

    expect(invalidatedKeys()).toContainEqual(dashboardKeys.incidentCounts());
    expect(invalidatedKeys()).toContainEqual(dashboardKeys.summaries());
  }

  it("acknowledge invalidates the incident counts and the dashboard summary", async () => {
    acknowledge.mockImplementation(() => ok(undefined));
    await expectCountInvalidation(() => useAcknowledgeIncident());
  });

  it("resolve invalidates the incident counts and the dashboard summary", async () => {
    resolve.mockImplementation(() => ok(undefined));
    await expectCountInvalidation(() => useResolveIncident());
  });

  it("investigate invalidates the incident counts and the dashboard summary", async () => {
    update.mockImplementation(() => ok(undefined));
    await expectCountInvalidation(() => useInvestigateIncident());
  });

  it("mitigate invalidates the incident counts and the dashboard summary", async () => {
    update.mockImplementation(() => ok(undefined));
    await expectCountInvalidation(() => useMitigateIncident());
  });

  it("investigate posts Investigating via the generic update path", async () => {
    update.mockImplementation(() => ok(undefined));

    const { result } = renderHook(() => useInvestigateIncident(), { wrapper });
    await result.current.mutateAsync("inc-1");

    expect(update).toHaveBeenCalledWith("inc-1", { status: "Investigating" });
  });

  it("mitigate posts Mitigated via the generic update path", async () => {
    update.mockImplementation(() => ok(undefined));

    const { result } = renderHook(() => useMitigateIncident(), { wrapper });
    await result.current.mutateAsync("inc-1");

    expect(update).toHaveBeenCalledWith("inc-1", { status: "Mitigated" });
  });
});
describe("useReassignIncident", () => {
  it("refreshes the reassigned incident and the list, so the new owner shows on both", async () => {
    reassign.mockImplementation(() => ok(undefined));

    const { result } = renderHook(() => useReassignIncident(), { wrapper });
    await result.current.mutateAsync({ id: "inc-1", targetUserId: "u2" });

    expect(reassign).toHaveBeenCalledWith("inc-1", "u2");
    const keys = invalidatedKeys();
    expect(keys).toContainEqual(incidentKeys.detail("inc-1"));
    expect(keys).toContainEqual(incidentKeys.lists());
  });
});

describe("useAddNote", () => {
  it("refreshes the notes and the timeline the note also lands on", async () => {
    addNote.mockImplementation(() => ok({ id: "note-1" }));

    const { result } = renderHook(() => useAddNote(), { wrapper });
    await result.current.mutateAsync({ incidentId: "inc-1", content: "Rolled back the deploy" });

    expect(addNote).toHaveBeenCalledWith("inc-1", { content: "Rolled back the deploy" });
    const keys = invalidatedKeys();
    expect(keys).toContainEqual(incidentKeys.notes("inc-1"));
    // A note is also a timeline entry; refreshing only the notes leaves the timeline stale.
    expect(keys).toContainEqual(incidentKeys.timeline("inc-1"));
  });
});
