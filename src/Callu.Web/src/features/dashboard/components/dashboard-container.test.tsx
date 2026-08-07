import "@testing-library/jest-dom/vitest";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";

/** What the dashboard asks the server for on behalf of the on-call card, and under which cache key. */

const summary = vi.fn();
const schedules = vi.fn();
const scheduleDetail = vi.fn();
const occurrences = vi.fn();

vi.mock("../hooks/use-dashboard", () => ({
  useDashboardSummary: (recentCount?: number, timeRangeDays?: number) =>
    summary(recentCount, timeRangeDays),
}));

vi.mock("@/features/schedules/hooks/use-schedules", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/features/schedules/hooks/use-schedules")>()),
  useSchedules: () => schedules(),
  useSchedule: (id: string) => scheduleDetail(id),
  useScheduleOccurrences: (id: string, days: number) => occurrences(id, days),
}));

const { Dashboard } = await import("./dashboard-container");

function schedule(id: string, name: string) {
  return {
    id,
    name,
    teamId: "team-1",
    timezone: "UTC",
    rotationCount: 1,
    createdAt: "2026-06-01T00:00:00Z",
  };
}

function renderDashboard() {
  return render(
    <MemoryRouter>
      <Dashboard />
    </MemoryRouter>,
  );
}

function lastOccurrencesCall(): unknown[] {
  const { calls } = occurrences.mock;
  return calls[calls.length - 1];
}

beforeEach(() => {
  vi.clearAllMocks();
  summary.mockReturnValue({ data: undefined, isLoading: false });
  schedules.mockReturnValue({ data: [schedule("sch-1", "Primary on-call")], isLoading: false });
  scheduleDetail.mockReturnValue({ data: { overrides: [] } });
  occurrences.mockReturnValue({ data: [], isLoading: false });
});

describe("what the dashboard fetches for the on-call card", () => {
  it("asks for one schedule's occurrences, over the window the card draws", () => {
    renderDashboard();

    expect(lastOccurrencesCall()).toEqual(["sch-1", 2]);
    expect(scheduleDetail).toHaveBeenCalledWith("sch-1");
  });

  it("asks for the schedule the operator picked instead", async () => {
    const user = userEvent.setup();
    schedules.mockReturnValue({
      data: [schedule("sch-1", "Primary on-call"), schedule("sch-2", "Database")],
      isLoading: false,
    });
    renderDashboard();

    await user.click(screen.getByRole("button", { name: "Database" }));

    await waitFor(() => expect(lastOccurrencesCall()).toEqual(["sch-2", 2]));
  });

  // A pick can outlive the schedule it named; asking for a deleted id leaves the card blank.
  it("falls back to the first schedule when the picked one is gone", async () => {
    const user = userEvent.setup();
    schedules.mockReturnValue({
      data: [schedule("sch-1", "Primary on-call"), schedule("sch-2", "Database")],
      isLoading: false,
    });
    const { rerender } = renderDashboard();

    await user.click(screen.getByRole("button", { name: "Database" }));
    await waitFor(() => expect(lastOccurrencesCall()).toEqual(["sch-2", 2]));

    schedules.mockReturnValue({ data: [schedule("sch-1", "Primary on-call")], isLoading: false });
    rerender(
      <MemoryRouter>
        <Dashboard />
      </MemoryRouter>,
    );

    await waitFor(() => expect(lastOccurrencesCall()).toEqual(["sch-1", 2]));
  });

  it("carries a failed read through rather than handing the card an empty rota", () => {
    occurrences.mockReturnValue({ data: undefined, isLoading: false, error: new Error("HTTP 500") });
    renderDashboard();

    expect(screen.getByText(/could not load who is on call/i)).toBeInTheDocument();
  });

  it("asks for nothing while no schedule exists", () => {
    schedules.mockReturnValue({ data: [], isLoading: false });
    renderDashboard();

    // Both reads are keyed off an empty id, which is what disables them.
    expect(lastOccurrencesCall()).toEqual(["", 2]);
    expect(scheduleDetail).toHaveBeenCalledWith("");
  });
});

/** The rota and the overrides are two separate reads. Whoever the card names has to be read off
 * both of them, and only once both have landed. */
describe("the two reads the on-call card is assembled from", () => {
  const HOUR = 3_600_000;

  function runningShift() {
    return [
      {
        id: "occ-1",
        scheduleId: "sch-1",
        userId: "ada",
        userName: "Ada Lovelace",
        isPrimary: true,
        order: 0,
        shiftLengthMinutes: 480,
        startUtc: new Date(Date.now() - 2 * HOUR).toISOString(),
        endUtc: new Date(Date.now() + 6 * HOUR).toISOString(),
      },
    ];
  }

  function runningOverride() {
    return [
      {
        id: "ovr-1",
        scheduleId: "sch-1",
        scheduleName: "Primary on-call",
        overrideUserId: "efe",
        overrideUserName: "Efe Kaya",
        startUtc: new Date(Date.now() - HOUR).toISOString(),
        endUtc: new Date(Date.now() + HOUR).toISOString(),
        isActive: true,
      },
    ];
  }

  it("carries the overrides from the schedule detail into the card", () => {
    occurrences.mockReturnValue({ data: runningShift(), isLoading: false });
    scheduleDetail.mockReturnValue({ data: { overrides: runningOverride() } });
    renderDashboard();

    expect(screen.getByText("Efe Kaya, Ada Lovelace")).toBeInTheDocument();
  });

  // The occurrences usually land first. Naming the rotation holder off them alone puts the wrong
  // person on the screen whose only job is naming who gets paged, then swaps them out.
  it("waits for the overrides before naming anyone", () => {
    occurrences.mockReturnValue({ data: runningShift(), isLoading: false });
    scheduleDetail.mockReturnValue({ data: undefined, isLoading: true });
    renderDashboard();

    expect(screen.getByRole("status")).toBeInTheDocument();
    expect(screen.queryByText(/Ada Lovelace/)).not.toBeInTheDocument();
  });
});
