import "@testing-library/jest-dom/vitest";
import type { ReactElement } from "react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Routes, Route } from "react-router";

/** The on-call editor decides who a page reaches, so these pin the load error, that an edit to only
 * name/team/timezone omits `rotations`, that a re-order is effective, and that a rejected save shows. */

const navigate = vi.fn();
vi.mock("react-router", async (importOriginal) => ({
  ...(await importOriginal<typeof import("react-router")>()),
  useNavigate: () => navigate,
}));

const useSchedule = vi.fn();
const useScheduleOccurrences = vi.fn();
const useScheduleCoverage = vi.fn();
const createSchedule = vi.fn();
const deleteScheduleMutate = vi.fn();
/** The create path rolls a half-built schedule back with `mutateAsync`, not `mutate`. */
const deleteScheduleAsync = vi.fn();
const saveSchedulePlan = vi.fn();

vi.mock("../hooks/use-schedules", () => ({
  useSchedule: (id: string) => useSchedule(id),
  useScheduleOccurrences: (id: string, days: number) => useScheduleOccurrences(id, days),
  useScheduleCoverage: (id: string, days: number) => useScheduleCoverage(id, days),
  useCreateSchedule: () => ({ mutateAsync: createSchedule, isPending: false }),
  useDeleteSchedule: () => ({
    mutate: deleteScheduleMutate,
    mutateAsync: deleteScheduleAsync,
    isPending: false,
  }),
  useSaveSchedulePlan: () => ({ mutateAsync: saveSchedulePlan, isPending: false }),
  useDeleteOverride: () => ({ mutate: vi.fn(), isPending: false }),
}));

vi.mock("@/features/users/hooks/use-users", () => ({
  useUsers: () => ({
    data: [
      { id: "u1", displayName: "Alice", email: "alice@x.io", phoneNumber: "+111", initials: "AL" },
      { id: "u2", displayName: "Bob", email: "bob@x.io", phoneNumber: "+222", initials: "BO" },
    ],
  }),
}));
vi.mock("@/features/teams/hooks/use-teams", () => ({
  useTeams: () => ({ data: [{ id: "team-1", name: "DB team", color: "#3E7BFA" }] }),
  useTeam: () => ({ data: undefined }),
}));
const useOrganizationSettings = vi.fn();
vi.mock("@/features/settings/hooks/use-settings", () => ({
  useOrganizationSettings: () => useOrganizationSettings(),
}));

const { ScheduleDetail } = await import("./detail");

const SCHEDULE_ID = "sch-1";

/** A valid, weekly 24/7, two-member schedule. */
function weeklySchedule(overrides: Record<string, unknown> = {}) {
  return {
    id: SCHEDULE_ID,
    name: "Primary on-call",
    description: "",
    teamId: "team-1",
    teamName: "DB team",
    timezone: "Europe/Istanbul",
    rotationCount: 2,
    createdAt: "2026-06-01T00:00:00Z",
    overrides: [],
    rotations: [
      {
        id: "r1",
        scheduleId: SCHEDULE_ID,
        userId: "u1",
        order: 1,
        isPrimary: true,
        handoverStartLocal: "2026-07-06T00:00:00",
        shiftLengthMinutes: 10080,
        recurrenceType: "Weekly",
        recurrenceIntervalDays: 14,
      },
      {
        id: "r2",
        scheduleId: SCHEDULE_ID,
        userId: "u2",
        order: 2,
        isPrimary: false,
        handoverStartLocal: "2026-07-13T00:00:00",
        shiftLengthMinutes: 10080,
        recurrenceType: "Weekly",
        recurrenceIntervalDays: 14,
      },
    ],
    ...overrides,
  };
}

function renderAt(path = `/schedules/${SCHEDULE_ID}`, ui: ReactElement = <ScheduleDetail />) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/schedules/:id" element={ui} />
      </Routes>
    </MemoryRouter>,
  );
}

/** The selected-member row (Alice/Bob) that owns the up/down/remove controls. */
function memberRow(name: string): HTMLElement {
  let el: HTMLElement | null = screen.getByText(name);
  while (el && el.querySelectorAll("button").length < 3) el = el.parentElement;
  if (!el) throw new Error(`row for ${name} not found`);
  return el;
}

beforeEach(() => {
  vi.clearAllMocks();
  useSchedule.mockReturnValue({ data: undefined, isLoading: false, error: null });
  useScheduleOccurrences.mockReturnValue({ data: [] });
  useScheduleCoverage.mockReturnValue({ data: undefined });
  useOrganizationSettings.mockReturnValue({ data: undefined });
  createSchedule.mockResolvedValue({ id: SCHEDULE_ID });
  deleteScheduleMutate.mockImplementation((_id: string, opts?: { onSuccess?: () => void }) =>
    opts?.onSuccess?.(),
  );
  deleteScheduleAsync.mockResolvedValue(undefined);
  saveSchedulePlan.mockResolvedValue(undefined);
});

describe("ScheduleDetail — load states", () => {
  it("renders a readable error with a way back instead of spinning forever", () => {
    useSchedule.mockReturnValue({
      data: undefined,
      isLoading: false,
      error: new Error("Request failed with status 404"),
    });
    const { container } = renderAt();

    expect(screen.getByText("Failed to load schedule")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Back to Schedules" })).toBeInTheDocument();
    expect(container.querySelector(".animate-spin")).toBeNull();
  });

  it("shows a spinner while an existing schedule is loading", () => {
    useSchedule.mockReturnValue({ data: undefined, isLoading: true, error: null });
    const { container } = renderAt();
    expect(container.querySelector(".animate-spin")).toBeTruthy();
  });
});

describe("ScheduleDetail — building a schedule from scratch", () => {
  /** Create is two calls that must both land, since a schedule with no rota pages nobody: the rota
   * is sent, phased in the arranged order, and a rota failure is not left as an empty schedule. */
  async function fillNewSchedule(user: ReturnType<typeof userEvent.setup>, name: string) {
    await user.type(screen.getByPlaceholderText("e.g., Backend On-Call, Frontend 24/7"), name);

    await user.click(screen.getByRole("combobox", { name: /^Team/i }));
    await user.click(await screen.findByRole("option", { name: /DB team/i }));

    // The Members tab owns the roster; Radix Tabs activates on focus.
    const membersTab = screen.getByRole("tab", { name: /Members/i });
    fireEvent.focus(membersTab);
    fireEvent.click(membersTab);
    await screen.findByText("Alice");
  }

  /** The "Add" button on an available-member row. */
  async function addMember(user: ReturnType<typeof userEvent.setup>, name: string) {
    let el: HTMLElement | null = screen.getByText(name);
    while (el && !within(el).queryByRole("button", { name: /^Add$/i })) el = el.parentElement;
    if (!el) throw new Error(`available row for ${name} not found`);
    await user.click(within(el).getByRole("button", { name: /^Add$/i }));
  }

  /** The combobox trigger takes no accessible name from its content, so only the label association
   * makes this pass — on the one field that decides when the rota pages anybody. */
  it("names the timezone picker, so a screen reader hears which zone the rota runs in", () => {
    renderAt("/schedules/new");
    expect(screen.getByRole("combobox", { name: /Timezone/i })).toBeInTheDocument();
  });

  it("creates the schedule, then attaches a rota ordered as the operator arranged it", async () => {
    const user = userEvent.setup();
    // A new schedule seeds its timezone from the org default, so this pins what gets sent.
    useOrganizationSettings.mockReturnValue({ data: { defaultTimezone: "Europe/Istanbul" } });
    renderAt("/schedules/new");

    await fillNewSchedule(user, "EU primary");
    await addMember(user, "Alice");
    await addMember(user, "Bob");

    await user.click(screen.getByRole("button", { name: "Create Schedule" }));

    await waitFor(() => expect(navigate).toHaveBeenCalledWith("/schedules"));

    expect(createSchedule).toHaveBeenCalledWith({
      name: "EU primary",
      description: undefined,
      teamId: "team-1",
      timezone: "Europe/Istanbul",
    });

    // The rota follows the id the server returned, in the order the members were added.
    expect(saveSchedulePlan).toHaveBeenCalledTimes(1);
    const payload = saveSchedulePlan.mock.calls[0][0];
    expect(payload.id).toBe(SCHEDULE_ID);
    expect(payload.rotations).toHaveLength(2);
    expect(payload.rotations[0].userId).toBe("u1");
    expect(payload.rotations[1].userId).toBe("u2");
    // Default block is a weekly 24/7 rota: a full 7-day wall-clock shift per member.
    expect(payload.rotations[0].shiftLengthMinutes).toBe(10080);
    // Two members on a 7-day block: each is back on call every 14 days.
    expect(payload.rotations[0].recurrenceIntervalDays).toBe(14);
    // Never a create *and* an edit-shaped save.
    expect(deleteScheduleAsync).not.toHaveBeenCalled();
  });

  it("takes the schedule back out when its rota fails, rather than leaving one that pages nobody", async () => {
    const user = userEvent.setup();
    saveSchedulePlan.mockRejectedValue(new Error("rota rejected"));
    renderAt("/schedules/new");

    await fillNewSchedule(user, "Doomed schedule");
    await addMember(user, "Alice");

    await user.click(screen.getByRole("button", { name: "Create Schedule" }));

    await waitFor(() => expect(screen.getByText(/rota rejected/i)).toBeInTheDocument());
    // The compensating delete removes the schedule the create call had already committed.
    expect(createSchedule).toHaveBeenCalledTimes(1);
    expect(deleteScheduleAsync).toHaveBeenCalledWith(SCHEDULE_ID);
    // The failure is shown and the operator stays put — no pretending it worked.
    expect(navigate).not.toHaveBeenCalled();
  });

  it("will not save a schedule with no members, since it would page nobody", async () => {
    const user = userEvent.setup();
    renderAt("/schedules/new");

    await fillNewSchedule(user, "Empty schedule");

    // Named and teamed, but no roster: the create button stays disabled.
    expect(screen.getByRole("button", { name: "Create Schedule" })).toBeDisabled();
    expect(createSchedule).not.toHaveBeenCalled();
  });
});

describe("ScheduleDetail — Tur-2: an unrelated edit must not touch the rotations", () => {
  it("omits rotations from the save when only the description changed", async () => {
    useSchedule.mockReturnValue({ data: weeklySchedule(), isLoading: false, error: null });
    renderAt();

    fireEvent.change(screen.getByPlaceholderText("Brief description of this schedule..."), {
      target: { value: "Now covers the EU region" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Update Schedule" }));

    await waitFor(() => expect(saveSchedulePlan).toHaveBeenCalledTimes(1));
    const payload = saveSchedulePlan.mock.calls[0][0];
    // The heart of the guard: the stored handovers are left alone.
    expect(payload.rotations).toBeUndefined();
    expect(payload).toMatchObject({ id: SCHEDULE_ID, description: "Now covers the EU region" });
    // No handover moved, so no confirmation was needed and the save went straight through.
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(navigate).toHaveBeenCalledWith("/schedules");
  });
});

describe("ScheduleDetail — changing the member order is effective", () => {
  it("sends a rota whose new primary is the member moved to the top", async () => {
    useSchedule.mockReturnValue({ data: weeklySchedule(), isLoading: false, error: null });
    renderAt();

    // Move to the Members tab and push Alice (currently primary) below Bob. Radix Tabs uses
    // automatic activation (on focus), so focus the trigger rather than only clicking it.
    const membersTab = screen.getByRole("tab", { name: /Members/i });
    fireEvent.focus(membersTab);
    fireEvent.click(membersTab);
    await screen.findByText("Alice");

    const aliceControls = within(memberRow("Alice")).getAllByRole("button");
    // [up (disabled at top), down, remove] — move Alice down so Bob becomes primary.
    fireEvent.click(aliceControls[1]);

    fireEvent.click(screen.getByRole("button", { name: "Update Schedule" }));

    // Re-phasing the rota moves handovers, so the confirmation gate appears first.
    const dialog = await screen.findByRole("dialog");
    fireEvent.click(within(dialog).getByRole("button", { name: /Save anyway/i }));

    await waitFor(() => expect(saveSchedulePlan).toHaveBeenCalledTimes(1));
    const payload = saveSchedulePlan.mock.calls[0][0];
    expect(Array.isArray(payload.rotations)).toBe(true);
    expect(payload.rotations).toHaveLength(2);
    // Bob is now the primary the save publishes.
    expect(payload.rotations[0].userId).toBe("u2");
    expect(payload.rotations[1].userId).toBe("u1");
    expect(navigate).toHaveBeenCalledWith("/schedules");
  });
});

describe("ScheduleDetail — an unsupported block is flagged and cannot be saved", () => {
  it("warns and disables save for a sub-24h window that would exceed the per-member day limit", () => {
    // 8h shift window (< 24h) with a 40-day-per-member cadence: over the 31-day non-24/7 limit,
    // so the block is unsupported the moment it loads.
    useSchedule.mockReturnValue({
      data: weeklySchedule({
        rotations: [
          {
            id: "r1",
            scheduleId: SCHEDULE_ID,
            userId: "u1",
            order: 1,
            isPrimary: true,
            handoverStartLocal: "2026-07-06T09:00:00",
            shiftLengthMinutes: 480,
            recurrenceIntervalDays: 80,
          },
          {
            id: "r2",
            scheduleId: SCHEDULE_ID,
            userId: "u2",
            order: 2,
            isPrimary: false,
            handoverStartLocal: "2026-08-25T09:00:00",
            shiftLengthMinutes: 480,
            recurrenceIntervalDays: 80,
          },
        ],
      }),
      isLoading: false,
      error: null,
    });
    renderAt();

    expect(screen.getByText(/can cover at most 31 days per member/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Update Schedule" })).toBeDisabled();
  });
});

describe("ScheduleDetail — a rejected save is shown, not swallowed", () => {
  it("surfaces the failure and stays on the page", async () => {
    useSchedule.mockReturnValue({ data: weeklySchedule(), isLoading: false, error: null });
    saveSchedulePlan.mockRejectedValue(new Error("region locked"));
    renderAt();

    fireEvent.change(screen.getByPlaceholderText("Brief description of this schedule..."), {
      target: { value: "trigger a dirty save" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Update Schedule" }));

    await waitFor(() => expect(screen.getByText(/Save failed — nothing was changed/i)).toBeInTheDocument());
    expect(screen.getByText(/region locked/i)).toBeInTheDocument();
    expect(navigate).not.toHaveBeenCalled();
  });
});

describe("ScheduleDetail — delete", () => {
  it("confirmation fires the delete mutation and returns to the list", async () => {
    useSchedule.mockReturnValue({ data: weeklySchedule(), isLoading: false, error: null });
    renderAt();

    fireEvent.click(screen.getByRole("button", { name: /Delete$/i }));
    const dialog = await screen.findByRole("dialog");
    fireEvent.click(within(dialog).getByRole("button", { name: "Delete Schedule" }));

    expect(deleteScheduleMutate).toHaveBeenCalledWith(SCHEDULE_ID, expect.anything());
    await waitFor(() => expect(navigate).toHaveBeenCalledWith("/schedules"));
  });
});

describe("ScheduleDetail — primary coverage gaps", () => {
  it("asks for the 30-day coverage window used by the post-save warning", () => {
    useSchedule.mockReturnValue({ data: weeklySchedule(), isLoading: false, error: null });
    useScheduleCoverage.mockReturnValue({
      data: { hasFullCoverage: true, gapHours: 0, coveragePercent: 100, gaps: [] },
    });
    renderAt();

    expect(useScheduleCoverage).toHaveBeenCalledWith(SCHEDULE_ID, 30);
  });

  it("warns when primary coverage is incomplete and lists the first gaps", () => {
    useSchedule.mockReturnValue({ data: weeklySchedule(), isLoading: false, error: null });
    useScheduleCoverage.mockReturnValue({
      data: {
        hasFullCoverage: false,
        gapHours: 24,
        coveragePercent: 85.7,
        gaps: [
          { start: "2026-08-02T00:00:00Z", end: "2026-08-03T00:00:00Z" },
          { start: "2026-08-10T00:00:00Z", end: "2026-08-11T00:00:00Z" },
        ],
      },
    });
    renderAt();

    expect(screen.getByRole("status")).toHaveTextContent(/85\.7% primary coverage/i);
    expect(screen.getByRole("status")).toHaveTextContent(/24h uncovered/i);
    expect(screen.getByText(/nobody is primary on-call/i)).toBeInTheDocument();
  });

  it("stays quiet when coverage is complete", () => {
    useSchedule.mockReturnValue({ data: weeklySchedule(), isLoading: false, error: null });
    useScheduleCoverage.mockReturnValue({
      data: { hasFullCoverage: true, gapHours: 0, coveragePercent: 100, gaps: [] },
    });
    renderAt();

    expect(screen.queryByText(/primary coverage/i)).not.toBeInTheDocument();
  });
});
