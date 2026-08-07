import "@testing-library/jest-dom/vitest";
import type { ReactElement } from "react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Routes, Route } from "react-router";

/** The policy editor decides whether anyone gets paged, so these pin that an invalid policy never
 * reaches the server, that add/remove step is effective, and that a rejected save does not navigate. */

const navigate = vi.fn();
vi.mock("react-router", async (importOriginal) => ({
  ...(await importOriginal<typeof import("react-router")>()),
  useNavigate: () => navigate,
}));

const useEscalationPolicy = vi.fn();
const createPolicy = vi.fn();
const updatePolicy = vi.fn();
const deletePolicy = vi.fn();
const addStep = vi.fn();
const updateStep = vi.fn();
const removeStep = vi.fn();
const reorderSteps = vi.fn();

vi.mock("../hooks/use-escalations", () => ({
  useEscalationPolicy: (id: string) => useEscalationPolicy(id),
  useCreatePolicy: () => ({ mutateAsync: createPolicy, isPending: false }),
  useUpdatePolicy: () => ({ mutateAsync: updatePolicy, isPending: false }),
  useDeletePolicy: () => ({ mutate: deletePolicy, isPending: false }),
  useAddStep: () => ({ mutateAsync: addStep, isPending: false }),
  useUpdateStep: () => ({ mutateAsync: updateStep, isPending: false }),
  useRemoveStep: () => ({ mutateAsync: removeStep, isPending: false }),
  useReorderSteps: () => ({ mutateAsync: reorderSteps, isPending: false }),
}));

vi.mock("@/features/users/hooks/use-users", () => ({
  useUsers: () => ({ data: [{ id: "user-1", email: "a@x.io", displayName: "Ada" }] }),
}));
vi.mock("@/features/teams/hooks/use-teams", () => ({
  useTeams: () => ({ data: [{ id: "team-1", name: "DB team" }] }),
}));
vi.mock("@/features/schedules/hooks/use-schedules", () => ({
  useSchedules: () => ({ data: [{ id: "sched-1", name: "Primary on-call" }] }),
}));

const toast = { error: vi.fn(), success: vi.fn(), warning: vi.fn(), info: vi.fn() };
vi.mock("@/shared/utils/toast", () => ({ toast }));

const { EscalationDetail } = await import("./detail");

function editPolicy(overrides: Record<string, unknown> = {}) {
  return {
    id: "pol-1",
    name: "DB on-call",
    description: "",
    teamId: "team-1",
    isActive: true,
    stepCount: 1,
    createdAt: "2026-07-15T10:00:00Z",
    steps: [
      {
        id: "step-1",
        escalationPolicyId: "pol-1",
        level: 1,
        title: "Page primary",
        delayMinutes: 0,
        scheduleId: "sched-1",
        notifyUserIds: [],
        notifyUserNames: [],
      },
    ],
    ...overrides,
  };
}

function renderAt(path: string, ui: ReactElement = <EscalationDetail />) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/escalations/:id" element={ui} />
      </Routes>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  useEscalationPolicy.mockReturnValue({ data: undefined, isLoading: false, error: null });
  createPolicy.mockResolvedValue({ id: "created-pol" });
  updatePolicy.mockResolvedValue(undefined);
  addStep.mockResolvedValue({ id: "srv-step" });
  updateStep.mockResolvedValue(undefined);
  removeStep.mockResolvedValue(undefined);
  reorderSteps.mockResolvedValue(undefined);
});

describe("EscalationDetail — load states", () => {
  it("renders a readable error with a way back instead of spinning forever", () => {
    useEscalationPolicy.mockReturnValue({
      data: undefined,
      isLoading: false,
      error: new Error("Request failed with status 404"),
    });
    const { container } = renderAt("/escalations/pol-1");

    expect(screen.getByText("Failed to load policy")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Back to Policies" })).toBeInTheDocument();
    expect(container.querySelector(".animate-spin")).toBeNull();
  });

  it("shows a spinner while an existing policy is loading", () => {
    useEscalationPolicy.mockReturnValue({ data: undefined, isLoading: true, error: null });
    const { container } = renderAt("/escalations/pol-1");
    expect(container.querySelector(".animate-spin")).toBeTruthy();
  });
});

describe("EscalationDetail — the pickers carry an accessible name", () => {
  /** A combobox trigger takes no accessible name from its contents, so querying by role+name is
   * what proves each picker's visible label is still tied to it. */
  it("names the owner team picker after its visible label, not its placeholder", () => {
    renderAt("/escalations/new");

    const teamPicker = screen.getByRole("combobox", { name: /^Owner Team/i });
    // The label element is what supplies the name, so clicking it focuses the picker.
    expect(teamPicker).toHaveAccessibleName(/Owner Team/i);
    // Every combobox on the screen is named — none are left silent.
    for (const combobox of screen.getAllByRole("combobox")) {
      expect(combobox).toHaveAccessibleName(/\S/);
    }
  });

  it("gives each step's pickers their own names, distinct from the policy's team picker", () => {
    renderAt("/escalations/new");
    fireEvent.click(screen.getByRole("button", { name: /Add Step/i }));

    // "Notify" (target type) and "Schedule" (the target itself) are separate named controls.
    expect(screen.getByRole("combobox", { name: "Notify" })).toBeInTheDocument();
    expect(screen.getByRole("combobox", { name: /^Schedule$/i })).toBeInTheDocument();
    // The step target is not confused with the policy-level "Owner Team" picker.
    expect(screen.getByRole("combobox", { name: /^Owner Team/i })).toBeInTheDocument();
  });

  it("keeps step picker names unique when several steps are on screen", () => {
    renderAt("/escalations/new");
    fireEvent.click(screen.getByRole("button", { name: /Add Step/i }));
    fireEvent.click(screen.getByRole("button", { name: /Add Step/i }));

    // Two steps means two "Notify" pickers — each tied to its own label, so ids stay unique.
    expect(screen.getAllByRole("combobox", { name: "Notify" })).toHaveLength(2);
    const ids = screen.getAllByRole("combobox", { name: "Notify" }).map((el) => el.id);
    expect(new Set(ids).size).toBe(2);
  });
});

describe("EscalationDetail — add / remove steps", () => {
  it("adds step cards and removes them again", () => {
    renderAt("/escalations/new");

    expect(screen.getByText("No escalation steps yet")).toBeInTheDocument();

    // Scoped to the editor column: a step's default title is also "Level 1", and the timeline
    // beside it echoes that title.
    const editor = screen.getByRole("region", { name: "Escalation Steps" });

    fireEvent.click(screen.getByRole("button", { name: /Add Step/i }));
    expect(within(editor).getByText("Level 1")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /Add Step/i }));
    expect(within(editor).getByText("Level 2")).toBeInTheDocument();

    // Each step card carries a trash button; removing the last one drops it back to a single level.
    const trashButtons = screen
      .getAllByRole("button")
      .filter((b) => b.className.includes("text-error-500"));
    fireEvent.click(trashButtons[trashButtons.length - 1]);

    expect(within(editor).queryByText("Level 2")).toBeNull();
    expect(within(editor).getByText("Level 1")).toBeInTheDocument();
  });
});

describe("EscalationDetail — the timing the policy will actually run to", () => {
  /** The screen is an editor, so the timing has to follow the field, not a saved policy. */
  it("re-times the policy as the wait is typed, without taking focus off the field", async () => {
    const user = userEvent.setup();
    useEscalationPolicy.mockReturnValue({
      data: editPolicy({
        stepCount: 2,
        steps: [
          {
            id: "step-1",
            escalationPolicyId: "pol-1",
            level: 1,
            title: "Page primary",
            delayMinutes: 0,
            scheduleId: "sched-1",
            notifyUserIds: [],
            notifyUserNames: [],
          },
          {
            id: "step-2",
            escalationPolicyId: "pol-1",
            level: 2,
            title: "Page secondary",
            delayMinutes: 5,
            scheduleId: "sched-1",
            notifyUserIds: [],
            notifyUserNames: [],
          },
        ],
      }),
      isLoading: false,
      error: null,
    });
    renderAt("/escalations/pol-1");

    expect(screen.getByText("Runs out at t+5 min")).toBeInTheDocument();

    const wait = screen.getAllByRole("spinbutton").find((el) => !(el as HTMLInputElement).disabled)!;
    await user.clear(wait);
    await user.type(wait, "30");

    expect(screen.getByText("Runs out at t+30 min")).toBeInTheDocument();
    expect(wait).toHaveFocus();
  });

  it("shows the 2-minute floor a short wait is raised to, and the offsets that follow", () => {
    useEscalationPolicy.mockReturnValue({
      data: editPolicy({
        stepCount: 3,
        steps: [1, 2, 3].map((level) => ({
          id: `step-${level}`,
          escalationPolicyId: "pol-1",
          level,
          title: `Step ${level}`,
          delayMinutes: level === 1 ? 0 : 1,
          scheduleId: "sched-1",
          notifyUserIds: [],
          notifyUserNames: [],
        })),
      }),
      isLoading: false,
      error: null,
    });
    renderAt("/escalations/pol-1");

    expect(screen.getAllByText("Set to 1 min, waits 2 min")).toHaveLength(2);
    expect(screen.getByText("Runs out at t+4 min")).toBeInTheDocument();
  });
});

describe("EscalationDetail — building a policy from scratch", () => {
  /** The only path exercising the create → add-step id handoff: the step must attach to the id the
   * server returned, not the client's temporary one. */
  it("creates the policy, then attaches the step to the id the server returned", async () => {
    const user = userEvent.setup();
    renderAt("/escalations/new");

    await user.type(
      screen.getByPlaceholderText("e.g., Backend Services - Critical"),
      "Backend critical",
    );

    // Owner team — the rule the API does not enforce, so the picker is the only thing supplying it.
    await user.click(screen.getByRole("combobox", { name: /^Owner Team/i }));
    await user.click(await screen.findByRole("option", { name: "DB team" }));

    await user.click(screen.getByRole("button", { name: /Add Step/i }));

    // A new step defaults to a schedule target with nothing chosen; pick the on-call schedule.
    await user.click(screen.getByRole("combobox", { name: /^Schedule$/i }));
    await user.click(await screen.findByRole("option", { name: "Primary on-call" }));

    await user.click(screen.getByRole("button", { name: "Save Policy" }));

    await waitFor(() => expect(navigate).toHaveBeenCalledWith("/escalations"));

    expect(createPolicy).toHaveBeenCalledWith({
      name: "Backend critical",
      description: undefined,
      teamId: "team-1",
      exhaustionBehavior: "Stop",
      maxRepeatCycles: 3,
      maxRepeatDurationMinutes: 240,
    });
    // `created-pol` is what createPolicy resolved with — the step must follow the server's id.
    expect(addStep).toHaveBeenCalledWith(
      expect.objectContaining({
        policyId: "created-pol",
        scheduleId: "sched-1",
        teamId: null,
        notifyUserIds: [],
        delayMinutes: 0,
      }),
    );
    expect(updatePolicy).not.toHaveBeenCalled();
    // One step: there is no order to reconcile, so no reorder call.
    expect(reorderSteps).not.toHaveBeenCalled();
  });

  it("drops the chosen schedule when the step switches to a team target", async () => {
    // The case the test below does not reach: a target was already picked before the switch.
    // stepPayload keys off targetType alone, so an uncleared targetValue would travel as the new
    // type's id — a schedule id arriving as teamId pages nobody, silently.
    const user = userEvent.setup();
    renderAt("/escalations/new");

    await user.click(screen.getByRole("button", { name: /Add Step/i }));

    await user.click(screen.getByRole("combobox", { name: /^Schedule$/i }));
    await user.click(await screen.findByRole("option", { name: "Primary on-call" }));
    expect(screen.getByRole("combobox", { name: /^Schedule$/i })).toHaveTextContent("Primary on-call");

    await user.click(screen.getByRole("combobox", { name: "Notify" }));
    await user.click(await screen.findByRole("option", { name: /^Team$/i }));

    // The team picker must come up empty rather than inheriting the schedule's id.
    expect(screen.getByRole("combobox", { name: /^Team$/i })).toHaveTextContent(/Select a team/i);
  });

  it("sends a team target as teamId, not as a schedule, after switching target type", async () => {
    // The target type toggle and the target picker are separate controls writing one payload;
    // if the switch fails to clear the previous pick, a step pages the wrong target entirely.
    const user = userEvent.setup();
    renderAt("/escalations/new");

    await user.type(
      screen.getByPlaceholderText("e.g., Backend Services - Critical"),
      "Team paging",
    );
    await user.click(screen.getByRole("combobox", { name: /^Owner Team/i }));
    await user.click(await screen.findByRole("option", { name: "DB team" }));

    await user.click(screen.getByRole("button", { name: /Add Step/i }));

    // Switch the step from the default schedule target over to a team target.
    await user.click(screen.getByRole("combobox", { name: "Notify" }));
    await user.click(await screen.findByRole("option", { name: /^Team$/i }));

    await user.click(screen.getByRole("combobox", { name: /^Team$/i }));
    await user.click(await screen.findByRole("option", { name: "DB team" }));

    await user.click(screen.getByRole("button", { name: "Save Policy" }));

    await waitFor(() => expect(navigate).toHaveBeenCalledWith("/escalations"));
    expect(addStep).toHaveBeenCalledWith(
      expect.objectContaining({
        policyId: "created-pol",
        teamId: "team-1",
        scheduleId: null,
        notifyAllTeamMembers: false,
      }),
    );
  });

  it("does not create a second policy when a retry follows a failed step call", async () => {
    // create succeeded, add-step failed. The policy exists server-side now; a retry that called
    // create again would leave an orphaned duplicate policy behind on every attempt.
    const user = userEvent.setup();
    addStep.mockRejectedValueOnce(new Error("500 from step endpoint"));
    renderAt("/escalations/new");

    await user.type(
      screen.getByPlaceholderText("e.g., Backend Services - Critical"),
      "Retry policy",
    );
    await user.click(screen.getByRole("combobox", { name: /^Owner Team/i }));
    await user.click(await screen.findByRole("option", { name: "DB team" }));
    await user.click(screen.getByRole("button", { name: /Add Step/i }));
    await user.click(screen.getByRole("combobox", { name: /^Schedule$/i }));
    await user.click(await screen.findByRole("option", { name: "Primary on-call" }));

    await user.click(screen.getByRole("button", { name: "Save Policy" }));
    await waitFor(() => expect(addStep).toHaveBeenCalledTimes(1));
    expect(navigate).not.toHaveBeenCalled();

    // Retry the same save.
    await user.click(screen.getByRole("button", { name: "Save Policy" }));

    await waitFor(() => expect(navigate).toHaveBeenCalledWith("/escalations"));
    expect(createPolicy).toHaveBeenCalledTimes(1);
    // The retry updates the policy it already created, and re-attempts the step against it.
    expect(updatePolicy).toHaveBeenCalledWith(
      expect.objectContaining({ id: "created-pol", name: "Retry policy" }),
    );
    expect(addStep).toHaveBeenCalledTimes(2);
  });
});

describe("EscalationDetail — a half-built policy never reaches the server", () => {
  it("blocks save when the owner team is missing, and surfaces it loudly", async () => {
    renderAt("/escalations/new");

    // A named policy with a step, but no owner team chosen — one of the two rules this screen owns.
    fireEvent.change(screen.getByPlaceholderText("e.g., Backend Services - Critical"), {
      target: { value: "Backend critical" },
    });
    fireEvent.click(screen.getByRole("button", { name: /Add Step/i }));
    fireEvent.click(screen.getByRole("button", { name: "Save Policy" }));

    await waitFor(() => expect(toast.error).toHaveBeenCalled());
    // The invalid policy is not sent — nothing is created, and no dangling paging path is left.
    expect(createPolicy).not.toHaveBeenCalled();
    expect(addStep).not.toHaveBeenCalled();
    expect(navigate).not.toHaveBeenCalled();
    expect(toast.error).toHaveBeenCalledWith(
      "Validation Error",
      "Select a team that this policy belongs to...",
    );
  });

  it("blocks save when a step has no target, even with name and team set", async () => {
    // Edit an otherwise-valid policy but blank the loaded step's schedule target, so the only
    // fault is the missing target — the step-level rule that decides whether anyone is paged.
    useEscalationPolicy.mockReturnValue({
      data: editPolicy({
        steps: [
          {
            id: "step-1",
            escalationPolicyId: "pol-1",
            level: 1,
            title: "Page primary",
            delayMinutes: 0,
            scheduleId: null,
            teamId: null,
            notifyUserIds: [],
            notifyUserNames: [],
          },
        ],
      }),
      isLoading: false,
      error: null,
    });
    renderAt("/escalations/pol-1");

    fireEvent.click(screen.getByRole("button", { name: "Save Policy" }));

    await waitFor(() => expect(toast.error).toHaveBeenCalled());
    expect(updatePolicy).not.toHaveBeenCalled();
    expect(updateStep).not.toHaveBeenCalled();
    expect(navigate).not.toHaveBeenCalled();
    expect(toast.error).toHaveBeenCalledWith("Validation Error", "No target selected");
  });
});

describe("EscalationDetail — saving an existing policy", () => {
  it("updates the policy and its step, then returns to the list", async () => {
    useEscalationPolicy.mockReturnValue({ data: editPolicy(), isLoading: false, error: null });
    renderAt("/escalations/pol-1");

    // The form is hydrated from the loaded policy (team + one valid schedule-target step).
    fireEvent.click(screen.getByRole("button", { name: "Save Policy" }));

    await waitFor(() => expect(navigate).toHaveBeenCalledWith("/escalations"));
    expect(updatePolicy).toHaveBeenCalledWith(
      expect.objectContaining({ id: "pol-1", name: "DB on-call", teamId: "team-1" }),
    );
    expect(updateStep).toHaveBeenCalledWith(
      expect.objectContaining({ policyId: "pol-1", stepId: "step-1" }),
    );
    expect(createPolicy).not.toHaveBeenCalled();
  });

  it("deletes the persisted step on save when the user removed it", async () => {
    // Two steps so the policy stays valid (>= 1 step) after one is removed.
    useEscalationPolicy.mockReturnValue({
      data: editPolicy({
        steps: [
          {
            id: "step-1",
            escalationPolicyId: "pol-1",
            level: 1,
            title: "Page primary",
            delayMinutes: 0,
            scheduleId: "sched-1",
            notifyUserIds: [],
            notifyUserNames: [],
          },
          {
            id: "step-2",
            escalationPolicyId: "pol-1",
            level: 2,
            title: "Page secondary",
            delayMinutes: 5,
            scheduleId: "sched-1",
            notifyUserIds: [],
            notifyUserNames: [],
          },
        ],
      }),
      isLoading: false,
      error: null,
    });
    renderAt("/escalations/pol-1");

    // The step cards each expose a trash button; the header Delete button also carries the
    // error color, so drop it and take the last step's trash control.
    const trashButtons = screen
      .getAllByRole("button")
      .filter((b) => b.className.includes("text-error-500") && b.className.includes("ml-auto"));
    fireEvent.click(trashButtons[trashButtons.length - 1]);

    fireEvent.click(screen.getByRole("button", { name: "Save Policy" }));

    await waitFor(() => expect(navigate).toHaveBeenCalledWith("/escalations"));
    expect(removeStep).toHaveBeenCalledWith({ policyId: "pol-1", stepId: "step-2" });
    expect(updateStep).toHaveBeenCalledWith(
      expect.objectContaining({ policyId: "pol-1", stepId: "step-1" }),
    );
  });

  it("stays on the page (no navigate) when the save is rejected, so the operator can retry", async () => {
    useEscalationPolicy.mockReturnValue({ data: editPolicy(), isLoading: false, error: null });
    updatePolicy.mockRejectedValue(new Error("409 conflict"));
    renderAt("/escalations/pol-1");

    fireEvent.click(screen.getByRole("button", { name: "Save Policy" }));

    await waitFor(() => expect(updatePolicy).toHaveBeenCalled());
    // The failing mutation surfaced its own toast; the screen must not pretend success by leaving.
    expect(navigate).not.toHaveBeenCalled();
  });

  it("delete confirmation fires the delete mutation and returns to the list", async () => {
    useEscalationPolicy.mockReturnValue({ data: editPolicy(), isLoading: false, error: null });
    deletePolicy.mockImplementation((_id: string, opts?: { onSuccess?: () => void }) => opts?.onSuccess?.());
    renderAt("/escalations/pol-1");

    fireEvent.click(screen.getByRole("button", { name: /Delete$/i }));
    const dialog = await screen.findByRole("dialog");
    fireEvent.click(within(dialog).getByRole("button", { name: "Delete Policy" }));

    expect(deletePolicy).toHaveBeenCalledWith("pol-1", expect.anything());
    await waitFor(() => expect(navigate).toHaveBeenCalledWith("/escalations"));
  });
});
