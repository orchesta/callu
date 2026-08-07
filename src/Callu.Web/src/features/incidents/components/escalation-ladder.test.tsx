import "@testing-library/jest-dom/vitest";
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { act, render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router";

/** The card an operator reads at 3am: which step is paging, who it reaches, and how long until the
 * next one. Its one non-obvious job is saying when nobody else is coming. */

import { EscalationLadder } from "./escalation-ladder";
import type {
  IncidentEscalation,
  IncidentEscalationStep,
  IncidentEscalationStepState,
} from "../types/incident.types";

const NOW = new Date("2026-07-29T14:00:00Z");

function step(over: Partial<IncidentEscalationStep> = {}): IncidentEscalationStep {
  return {
    id: crypto.randomUUID(),
    level: 1,
    title: "On-call engineer",
    delayMinutes: 5,
    notifyAllTeamMembers: false,
    notifyBothOnCall: false,
    notifyUserNames: [],
    state: "Pending" as IncidentEscalationStepState,
    ...over,
  };
}

function ladder(over: Partial<IncidentEscalation> = {}): IncidentEscalation {
  return {
    policyName: "Payments policy",
    runState: "Running",
    startedAt: "2026-07-29T13:50:00Z",
    exhaustionBehavior: "Stop",
    maxRepeatCycles: 3,
    cyclesCompleted: 0,
    steps: [],
    ...over,
  };
}

function show(escalation: IncidentEscalation) {
  return render(
    <MemoryRouter>
      <EscalationLadder escalation={escalation} />
    </MemoryRouter>,
  );
}

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true });
  vi.setSystemTime(NOW);
});

afterEach(() => {
  vi.useRealTimers();
});

describe("the escalation ladder", () => {
  it("counts down to the step that pages next, not the one already paging", () => {
    show(
      ladder({
        nextStepDueAt: new Date(NOW.getTime() + 4 * 60_000 + 30_000).toISOString(),
        steps: [
          step({ level: 1, title: "First", state: "Current", pagedAt: "2026-07-29T13:58:00Z" }),
          step({ level: 2, title: "Second" }),
        ],
      }),
    );

    // Whole minutes, floored: a 4m30s wait reads as 4, never as a jittering 4:29.
    expect(screen.getByText(/4 min/i)).toBeInTheDocument();
  });

  it("says a page is on its way once the due time has passed", () => {
    show(
      ladder({
        // A step that reached nobody backdates the clock, so the next one is already due.
        nextStepDueAt: new Date(NOW.getTime() - 60_000).toISOString(),
        steps: [
          step({ level: 1, state: "Current", pagedAt: "2026-07-29T13:55:00Z" }),
          step({ level: 2 }),
        ],
      }),
    );

    expect(screen.getByText(/paging now/i)).toBeInTheDocument();
  });

  it("shows no countdown when nothing is running", () => {
    show(
      ladder({
        runState: "Stopped",
        nextStepDueAt: null,
        steps: [step({ level: 1, state: "Current" }), step({ level: 2 })],
      }),
    );

    expect(screen.queryByText(/next step in/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/paging now/i)).not.toBeInTheDocument();
  });

  /** The thing the old screen never said. */
  it("warns on the last step that nobody else is paged after it", () => {
    show(
      ladder({
        nextStepDueAt: null,
        steps: [step({ level: 1, title: "Only step", state: "Current" })],
      }),
    );

    expect(screen.getByText(/nobody else is paged after it/i)).toBeInTheDocument();
  });

  it("says the run starts over instead of ending when the policy repeats", () => {
    show(
      ladder({
        nextStepDueAt: null,
        exhaustionBehavior: "Repeat",
        maxRepeatCycles: 3,
        cyclesCompleted: 0,
        steps: [step({ level: 1, title: "Only step", state: "Current" })],
      }),
    );

    expect(screen.queryByText(/nobody else is paged after it/i)).not.toBeInTheDocument();
    expect(screen.getByText(/starts over from step 1 \(pass 2 of 3\)/i)).toBeInTheDocument();
  });

  it("stops promising another pass once the repeating policy is on its last one", () => {
    show(
      ladder({
        nextStepDueAt: null,
        exhaustionBehavior: "Repeat",
        maxRepeatCycles: 3,
        cyclesCompleted: 2,
        steps: [step({ level: 1, title: "Only step", state: "Current" })],
      }),
    );

    expect(screen.getByText(/nobody else is paged after it/i)).toBeInTheDocument();
  });

  it("does not claim the run is over while a later step is still to come", () => {
    show(
      ladder({
        nextStepDueAt: new Date(NOW.getTime() + 120_000).toISOString(),
        steps: [step({ level: 1, state: "Current" }), step({ level: 2 })],
      }),
    );

    expect(screen.queryByText(/nobody else is paged after it/i)).not.toBeInTheDocument();
  });

  it("says plainly when the policy ran out with the incident still open", () => {
    show(
      ladder({
        runState: "Exhausted",
        nextStepDueAt: null,
        steps: [step({ level: 1, state: "Passed" }), step({ level: 2, state: "Current" })],
      }),
    );

    expect(screen.getByText(/nobody else will be paged/i)).toBeInTheDocument();
  });

  it("names who a step reaches, preferring the named users a step lists", () => {
    show(
      ladder({
        steps: [
          step({ level: 1, notifyUserNames: ["Ada Çelik", "Mert Kaya"], scheduleName: "Primary rota" }),
        ],
      }),
    );

    expect(screen.getByText("Ada Çelik, Mert Kaya")).toBeInTheDocument();
  });

  it("distinguishes a whole team from a team's on-call", () => {
    const { unmount } = show(ladder({ steps: [step({ teamName: "Platform", notifyAllTeamMembers: true })] }));
    expect(screen.getByText(/every member/i)).toBeInTheDocument();
    unmount();

    show(ladder({ steps: [step({ teamName: "Platform", notifyAllTeamMembers: false })] }));
    expect(screen.getByText(/on-call only/i)).toBeInTheDocument();
  });

  /** A step with no target pages nobody, and the run walks straight past it. */
  it("does not present a step with no target as if it reaches someone", () => {
    show(ladder({ steps: [step({ notifyUserNames: [], scheduleName: null, teamName: null })] }));

    expect(screen.getByText(/pages nobody/i)).toBeInTheDocument();
  });

  it("shows when a step paged, and shows nothing for one that has not", () => {
    show(
      ladder({
        steps: [
          step({ level: 1, state: "Passed", pagedAt: "2026-07-29T13:52:00Z" }),
          step({ level: 2, state: "Pending" }),
        ],
      }),
    );

    const rungs = screen.getAllByRole("listitem");
    expect(rungs[0].textContent).toMatch(/\d{1,2}:\d{2}/);
    expect(rungs[1].textContent).not.toMatch(/\d{1,2}:\d{2}\s*$/);
  });

  it("tells the operator there is nothing paging when no policy is attached", () => {
    show(ladder({ runState: "NotConfigured", policyName: null, steps: [] }));

    expect(screen.getByText(/nobody is paged automatically/i)).toBeInTheDocument();
  });

  it("keeps the countdown moving without a refetch", () => {
    show(
      ladder({
        nextStepDueAt: new Date(NOW.getTime() + 3 * 60_000 + 5_000).toISOString(),
        steps: [step({ level: 1, state: "Current" }), step({ level: 2 })],
      }),
    );

    expect(screen.getByText(/3 min/i)).toBeInTheDocument();

    act(() => {
      vi.advanceTimersByTime(70_000);
    });

    expect(screen.getByText(/1 min/i)).toBeInTheDocument();
  });
});
