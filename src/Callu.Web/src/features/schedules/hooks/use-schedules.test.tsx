import type { ReactNode } from "react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { SaveSchedulePlanRequest } from "../types/schedule.types";

/** These pin that a rota save is one request: several per-rotation calls each rematerialize, which
 * publishes a half-moved rota and pages the wrong person in between. */

const savePlan = vi.fn();
const updateRotation = vi.fn();
const addRotation = vi.fn();
const deleteRotation = vi.fn();
const update = vi.fn();

vi.mock("../api/schedule.api", () => ({
  scheduleApi: {
    savePlan: (id: string, data: unknown) => savePlan(id, data),
    update: (id: string, data: unknown) => update(id, data),
    updateRotation: (rotationId: string, data: unknown) => updateRotation(rotationId, data),
    addRotation: (scheduleId: string, data: unknown) => addRotation(scheduleId, data),
    deleteRotation: (rotationId: string) => deleteRotation(rotationId),
  },
}));

vi.mock("@/shared/utils/toast", () => ({
  toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() },
}));

const { useSaveSchedulePlan } = await import("./use-schedules");
const { toast } = await import("@/shared/utils/toast");
const { ApiError, createNetworkError, createTimeoutError } = await import("@/shared/api");

const ok = <T,>(data: T) => Promise.resolve({ success: true, data });
const fail = (message: string) => Promise.resolve({ success: false, message });

/** How a failure that reached the server, ran, and blew up AFTER committing the plan arrives here. */
const throws = (error: Error) => () => Promise.reject(error);

function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}

const plan: SaveSchedulePlanRequest = {
  name: "Primary on-call",
  timezone: "Europe/Istanbul",
  teamId: "team-1",
  rotations: [
    {
      id: "rot-b",
      userId: "bob",
      handoverStartLocal: "2026-07-06T00:00:00",
      shiftLengthMinutes: 10080,
      isPrimary: true,
      order: 1,
      recurrenceType: "Biweekly",
      recurrenceIntervalDays: 14,
    },
    {
      id: "rot-a",
      userId: "alice",
      handoverStartLocal: "2026-07-13T00:00:00",
      shiftLengthMinutes: 10080,
      isPrimary: false,
      order: 2,
      recurrenceType: "Biweekly",
      recurrenceIntervalDays: 14,
    },
  ],
};

function savePlanHook(input: SaveSchedulePlanRequest = plan) {
  const { result } = renderHook(() => useSaveSchedulePlan(), { wrapper });
  return { result, run: () => result.current.mutateAsync({ id: "sched-1", ...input }) };
}

describe("useSaveSchedulePlan", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    savePlan.mockImplementation(() => ok(undefined));
  });

  it("sends the whole plan as one request, so no intermediate rota is ever live", async () => {
    const { run } = savePlanHook();
    await run();

    expect(savePlan).toHaveBeenCalledTimes(1);
    expect(savePlan).toHaveBeenCalledWith("sched-1", plan);
  });

  it("never falls back to the per-rotation endpoints", async () => {
    // Each of those rematerializes the schedule on the way out; a sequence of them publishes
    // every intermediate state.
    const { run } = savePlanHook();
    await run();

    expect(update).not.toHaveBeenCalled();
    expect(updateRotation).not.toHaveBeenCalled();
    expect(addRotation).not.toHaveBeenCalled();
    expect(deleteRotation).not.toHaveBeenCalled();
  });

  it("omits rotations entirely when the plan does not touch them", async () => {
    // An edit to the name alone must not carry a rota with it: the form's timing fields are a
    // lossy rendering of rotation #1 and would overwrite the real handovers.
    const { run } = savePlanHook({ name: "Renamed" });
    await run();

    expect(savePlan).toHaveBeenCalledWith("sched-1", { name: "Renamed" });
    expect(savePlan.mock.calls[0][1]).not.toHaveProperty("rotations");
  });

  it("surfaces the reason a rejected save gave", async () => {
    savePlan.mockImplementation(() => fail("2 rotation member(s) are not in that team"));

    const { result, run } = savePlanHook();
    await expect(run()).rejects.toThrow("2 rotation member(s) are not in that team");

    await waitFor(() =>
      expect(result.current.error?.message).toBe("2 rotation member(s) are not in that team"),
    );

    // The server considered it and said no. Nothing changed, and saying so is the truth.
    expect(toast.error).toHaveBeenCalledWith(
      "Schedule not saved",
      "2 rotation member(s) are not in that team",
    );
    expect(toast.warning).not.toHaveBeenCalled();
  });

  /** The plan can commit while the rematerialize that follows it fails, so a flat error would tell
   * the operator nothing changed when the stored plan is live and the occurrences are stale. */
  it("does not claim nothing changed when the save may have landed", async () => {
    // A 500 is what the rematerialize-after-commit failure actually looks like from here.
    savePlan.mockImplementation(throws(new ApiError(500, "Internal Server Error")));

    const { run } = savePlanHook();
    await expect(run()).rejects.toThrow();

    await waitFor(() => expect(toast.warning).toHaveBeenCalledTimes(1));

    const [title, detail] = vi.mocked(toast.warning).mock.calls[0];
    expect(title).toBe("Schedule save could not be confirmed");
    expect(detail).toContain("may have been saved");
    expect(detail).toContain("who is on call");

    // And it must NOT be reported as a rejection — that is the sentence that was false.
    expect(toast.error).not.toHaveBeenCalled();
  });

  it("does not claim nothing changed when the answer never came back", async () => {
    // The request may have reached the server, committed, and had its reply lost on the way home.
    savePlan.mockImplementation(throws(createNetworkError(new Error("socket hang up"))));

    const { run } = savePlanHook();
    await expect(run()).rejects.toThrow();

    await waitFor(() => expect(toast.warning).toHaveBeenCalledTimes(1));
    expect(toast.error).not.toHaveBeenCalled();
  });

  it("does not claim nothing changed after a timeout", async () => {
    savePlan.mockImplementation(throws(createTimeoutError(30_000)));

    const { run } = savePlanHook();
    await expect(run()).rejects.toThrow();

    await waitFor(() => expect(toast.warning).toHaveBeenCalledTimes(1));
    expect(toast.error).not.toHaveBeenCalled();
  });
});
