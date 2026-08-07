import "@testing-library/jest-dom/vitest";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Routes, Route } from "react-router";

/** An override silently reroutes every page for its whole duration — up to a week. It could be
 * created from the schedule list and then never seen, edited or ended from anywhere in the UI. */

const deleteOverrideMutate = vi.fn();

const OVERRIDE = {
  id: "ovr-1",
  scheduleId: "sch-1",
  scheduleName: "Kurumsal Uygulamalar Takvimi",
  overrideUserId: "u-2",
  overrideUserName: "Weekend Cover",
  originalUserId: "u-1",
  originalUserName: "Ali Gören",
  startUtc: "2026-07-28T10:50:00Z",
  endUtc: "2026-07-28T12:50:00Z",
  reason: "Swap shift",
  isActive: true,
};

const SCHEDULE = {
  id: "sch-1",
  name: "Kurumsal Uygulamalar Takvimi",
  teamId: "team-1",
  teamName: "Test",
  timezone: "Europe/Istanbul",
  description: "",
  rotationCount: 2,
  createdAt: "2026-07-27T00:00:00Z",
  rotations: [],
  overrides: [OVERRIDE],
};

vi.mock("../hooks/use-schedules", () => ({
  useSchedule: () => ({ data: SCHEDULE, isLoading: false, error: null }),
  useScheduleOccurrences: () => ({ data: [] }),
  useCreateSchedule: () => ({ mutate: vi.fn(), isPending: false }),
  useDeleteSchedule: () => ({ mutate: vi.fn(), isPending: false }),
  useSaveSchedulePlan: () => ({ mutate: vi.fn(), isPending: false }),
  useDeleteOverride: () => ({ mutate: deleteOverrideMutate, isPending: false }),
  useScheduleCoverage: () => ({ data: null, isLoading: false }),
}));

vi.mock("@/features/users/hooks/use-users", () => ({ useUsers: () => ({ data: [] }) }));
vi.mock("@/features/teams/hooks/use-teams", () => ({
  useTeams: () => ({ data: [] }),
  useTeam: () => ({ data: null }),
}));
vi.mock("@/features/settings/hooks/use-settings", () => ({
  useOrganizationSettings: () => ({ data: null }),
  useTimezones: () => ({ data: [], isLoading: false }),
}));

const { ScheduleDetail } = await import("./detail");

function renderDetail() {
  render(
    <MemoryRouter initialEntries={["/schedules/sch-1"]}>
      <Routes>
        <Route path="/schedules/:id" element={<ScheduleDetail />} />
      </Routes>
    </MemoryRouter>,
  );
}

async function openOverridesTab(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole("tab", { name: /overrides/i }));
}

beforeEach(() => {
  vi.clearAllMocks();
  // Restored per test: the last case empties it, and order must not decide the others.
  SCHEDULE.overrides = [OVERRIDE];
});

describe("schedule overrides", () => {
  it("shows who is covering, for whom, and until when", async () => {
    const user = userEvent.setup();
    renderDetail();
    await openOverridesTab(user);

    expect(await screen.findByText("Weekend Cover")).toBeInTheDocument();
    expect(screen.getByText(/instead of Ali Gören/i)).toBeInTheDocument();
    expect(screen.getByText(/active now/i)).toBeInTheDocument();
    expect(screen.getByText(/swap shift/i)).toBeInTheDocument();
  });

  it("offers a way to end it, which is what the UI had no answer for", async () => {
    const user = userEvent.setup();
    renderDetail();
    await openOverridesTab(user);

    expect(await screen.findByRole("button", { name: /cancel the override for Weekend Cover/i })).toBeInTheDocument();
  });

  it("asks before ending it, because on-call moves the moment it does", async () => {
    const user = userEvent.setup();
    renderDetail();
    await openOverridesTab(user);

    await user.click(await screen.findByRole("button", { name: /cancel the override for/i }));

    expect(deleteOverrideMutate).not.toHaveBeenCalled();
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: /^cancel override$/i }));

    await waitFor(() => expect(deleteOverrideMutate).toHaveBeenCalledWith("ovr-1", expect.anything()));
  });

  it("says so plainly when the rotation is deciding on its own", async () => {
    SCHEDULE.overrides = [];
    const user = userEvent.setup();
    renderDetail();
    await openOverridesTab(user);

    expect(await screen.findByText(/no overrides/i)).toBeInTheDocument();
  });
});
