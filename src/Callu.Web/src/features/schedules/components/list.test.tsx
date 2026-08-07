import "@testing-library/jest-dom/vitest";
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { MemoryRouter } from "react-router";

vi.mock("react-router", async (importOriginal) => ({
  ...(await importOriginal<typeof import("react-router")>()),
  useNavigate: () => vi.fn(),
}));

const useSchedules = vi.fn();
const createOverrideMutate = vi.fn();

vi.mock("../hooks/use-schedules", () => ({
  useSchedules: () => useSchedules(),
  useDeleteSchedule: () => ({ mutate: vi.fn(), isPending: false }),
  useCreateOverride: () => ({ mutate: createOverrideMutate, isPending: false }),
}));

vi.mock("@/features/users/hooks/use-users", () => ({
  useUsers: () => ({
    data: [{ id: "u1", displayName: "Alice", email: "alice@x.io", phoneNumber: "+111", initials: "AL" }],
  }),
}));

const { SchedulesList } = await import("./list");

function schedule(timezone: string) {
  return {
    id: "sch-1",
    name: "Primary on-call",
    teamId: "team-1",
    teamName: "DB team",
    timezone,
    rotationCount: 1,
    createdAt: "2026-06-01T00:00:00Z",
  };
}

function openOverrideDialog(timezone: string, nowIso: string) {
  vi.useFakeTimers({ shouldAdvanceTime: true, now: new Date(nowIso) });
  useSchedules.mockReturnValue({ data: [schedule(timezone)], isLoading: false, error: null });
  render(
    <MemoryRouter>
      <SchedulesList />
    </MemoryRouter>,
  );
  fireEvent.click(screen.getByRole("button", { name: /Override/i }));
  return document.querySelector('input[type="date"]') as HTMLInputElement;
}

beforeEach(() => {
  vi.clearAllMocks();
});

afterEach(() => {
  vi.useRealTimers();
});

describe("SchedulesList — override start-date bound", () => {
  it("lets an operator west of UTC still pick tonight", () => {
    // 18:00 on the 15th in Los Angeles; UTC has already rolled over to the 16th.
    const input = openOverrideDialog("America/Los_Angeles", "2026-07-16T01:00:00Z");
    expect(input).toHaveAttribute("min", "2026-07-15");
  });

  it("does not offer a day that has not started yet east of UTC", () => {
    // 07:00 on the 16th in Tokyo; UTC is still on the 15th.
    const input = openOverrideDialog("Asia/Tokyo", "2026-07-15T22:00:00Z");
    expect(input).toHaveAttribute("min", "2026-07-16");
  });
});
