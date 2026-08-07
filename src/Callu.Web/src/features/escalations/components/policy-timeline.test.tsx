import "@testing-library/jest-dom/vitest";
import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";

/** The card an admin trusts when deciding whether the third person is reached soon enough. Its two
 * load-bearing claims are the step a floor moved and the moment the policy has nobody left. */

import { PolicyTimeline } from "./policy-timeline";
import { describeTarget, type TargetDirectory } from "../utils/describe-target";
import type { LocalStep } from "../utils/step-mapping";

function step(over: Partial<LocalStep> = {}): LocalStep {
  return {
    id: crypto.randomUUID(),
    level: 1,
    delayMinutes: 0,
    title: "Page primary",
    description: "",
    targetType: "schedule",
    targetValue: "sched-1",
    targetUserIds: [],
    notifyAll: false,
    notifyBothOnCall: false,
    ...over,
  };
}

// The editor's own resolver, not a stand-in for it: a card that names the wrong target is the
// failure this screen exists to prevent.
const DIRECTORY: TargetDirectory = {
  users: [{ id: "u-1", email: "ada@example.com", firstName: "Ada", lastName: "Lovelace" }],
  teams: [{ id: "team-1", name: "DB team" }],
  schedules: [
    { id: "sched-1", name: "Primary on-call" },
    { id: "sched-2", name: "Database on-call" },
  ],
};

function show(steps: LocalStep[]) {
  return render(
    <PolicyTimeline steps={steps} describeTarget={(s) => describeTarget(s, DIRECTORY)} />,
  );
}

describe("PolicyTimeline — the floor", () => {
  it("marks the step the floor moved, naming the configured minute and the effective one", () => {
    show([
      step({ level: 1, delayMinutes: 0, title: "First" }),
      step({ level: 2, delayMinutes: 1, title: "Second" }),
      step({ level: 3, delayMinutes: 1, title: "Third" }),
    ]);

    expect(screen.getAllByText("Set to 1 min, waits 2 min")).toHaveLength(2);
    // The offsets that follow from it: 1 + 1 typed reads as t+2 and t+4, never t+1 and t+2.
    expect(screen.getByText("t+2 min")).toBeInTheDocument();
    expect(screen.getByText("t+4 min")).toBeInTheDocument();
  });

  it("leaves a step at or above the floor unmarked, showing its wait as written", () => {
    show([
      step({ level: 1, delayMinutes: 0, title: "First" }),
      step({ level: 2, delayMinutes: 5, title: "Second" }),
    ]);

    expect(screen.queryByText(/Set to/)).not.toBeInTheDocument();
    expect(screen.getByText("After 5 min")).toBeInTheDocument();
    expect(screen.getByText("t+5 min")).toBeInTheDocument();
  });

  it("does not mark a first step written below the floor, because it is not raised", () => {
    show([step({ level: 1, delayMinutes: 1, title: "Only" })]);

    expect(screen.queryByText(/Set to/)).not.toBeInTheDocument();
    expect(screen.getByText("t+1 min")).toBeInTheDocument();
  });
});

describe("PolicyTimeline — the total", () => {
  it("says when the policy runs out, and that nobody follows the last step", () => {
    show([
      step({ level: 1, delayMinutes: 0 }),
      step({ level: 2, delayMinutes: 5 }),
      step({ level: 3, delayMinutes: 15 }),
    ]);

    expect(screen.getByText("Runs out at t+20 min")).toBeInTheDocument();
    expect(screen.getByText(/Nobody is escalated to after it/i)).toBeInTheDocument();
  });

  it("counts the floor into the total, not the typed delays", () => {
    show([
      step({ level: 1, delayMinutes: 0 }),
      step({ level: 2, delayMinutes: 1 }),
      step({ level: 3, delayMinutes: 1 }),
    ]);

    expect(screen.getByText("Runs out at t+4 min")).toBeInTheDocument();
  });

  it("reads a policy measured in hours as hours", () => {
    show([step({ level: 1, delayMinutes: 0 }), step({ level: 2, delayMinutes: 150 })]);

    expect(screen.getByText("t+2h 30m")).toBeInTheDocument();
    expect(screen.getByText("Runs out at t+2h 30m")).toBeInTheDocument();
  });
});

describe("PolicyTimeline — edge cases", () => {
  it("says a policy with no steps pages nobody, and claims no total", () => {
    show([]);

    expect(screen.getByText(/pages nobody/i)).toBeInTheDocument();
    expect(screen.queryByText(/Runs out at/)).not.toBeInTheDocument();
  });

  it("places a single immediate step at the start and runs out there", () => {
    show([step({ level: 1, delayMinutes: 0, title: "Only" })]);

    expect(screen.getByText("t+0 min")).toBeInTheDocument();
    expect(screen.getByText("Runs out at t+0 min")).toBeInTheDocument();
  });

  it("does not present a step with no target chosen as if it reaches someone", () => {
    show([step({ level: 1, delayMinutes: 0, targetValue: "" })]);

    expect(screen.getByText("Not configured")).toBeInTheDocument();
  });

  it("falls back to the level when a step has no title yet", () => {
    show([step({ level: 1, delayMinutes: 0, title: "" })]);

    expect(screen.getByText("Level 1")).toBeInTheDocument();
  });
});

describe("PolicyTimeline — what the step list already told the admin", () => {
  it("keeps level, title and who the step notifies", () => {
    show([step({ level: 1, delayMinutes: 0, title: "Page primary" })]);

    const item = screen.getByRole("listitem");
    expect(item).toHaveTextContent("1");
    expect(item).toHaveTextContent("Page primary");
    expect(item).toHaveTextContent("Primary on-call");
  });

  it("keeps the whole-team and both-on-call notes", () => {
    const { unmount } = show([
      step({ level: 1, delayMinutes: 0, targetType: "team", targetValue: "team-1", notifyAll: true }),
    ]);
    expect(screen.getByText("(All team members)")).toBeInTheDocument();
    unmount();

    show([step({ level: 1, delayMinutes: 0, notifyBothOnCall: true })]);
    expect(screen.getByText("(Both primary and secondary on-call)")).toBeInTheDocument();
  });
});

describe("PolicyTimeline — each row belongs to its own step", () => {
  it("keeps every row's title, target and notes on the step that row is numbered for", () => {
    show([
      step({
        level: 1,
        delayMinutes: 0,
        title: "Wake the primary",
        targetType: "schedule",
        targetValue: "sched-1",
        notifyBothOnCall: true,
      }),
      step({
        level: 2,
        delayMinutes: 1,
        title: "Bring in the database crew",
        targetType: "team",
        targetValue: "team-1",
        notifyAll: true,
      }),
      step({
        level: 3,
        delayMinutes: 30,
        title: "Wake the manager",
        targetType: "user",
        targetValue: "",
        targetUserIds: ["u-1"],
      }),
    ]);

    const rows = screen.getAllByRole("listitem");
    expect(rows).toHaveLength(3);

    expect(rows[0]).toHaveTextContent("Wake the primary");
    expect(rows[0]).toHaveTextContent("Primary on-call");
    expect(rows[0]).toHaveTextContent("t+0 min");
    expect(rows[0]).toHaveTextContent("(Both primary and secondary on-call)");
    expect(rows[0]).not.toHaveTextContent("DB team");

    expect(rows[1]).toHaveTextContent("Bring in the database crew");
    expect(rows[1]).toHaveTextContent("DB team");
    expect(rows[1]).toHaveTextContent("t+2 min");
    expect(rows[1]).toHaveTextContent("Set to 1 min, waits 2 min");
    expect(rows[1]).toHaveTextContent("(All team members)");
    expect(rows[1]).not.toHaveTextContent("Primary on-call");

    expect(rows[2]).toHaveTextContent("Wake the manager");
    expect(rows[2]).toHaveTextContent("Ada Lovelace");
    expect(rows[2]).toHaveTextContent("t+32 min");
    expect(rows[2]).toHaveTextContent("After 30 min");
    expect(rows[2]).not.toHaveTextContent("(All team members)");
  });

  it("keeps a schedule step's both-on-call note off the team step next to it", () => {
    show([
      step({ level: 1, delayMinutes: 0, targetType: "team", targetValue: "team-1", notifyAll: true }),
      step({ level: 2, delayMinutes: 5, targetValue: "sched-2", notifyBothOnCall: true }),
    ]);

    const rows = screen.getAllByRole("listitem");
    expect(rows[0]).toHaveTextContent("(All team members)");
    expect(rows[0]).not.toHaveTextContent("(Both primary and secondary on-call)");
    expect(rows[1]).toHaveTextContent("Database on-call");
    expect(rows[1]).toHaveTextContent("(Both primary and secondary on-call)");
  });
});

/** The runner does not wait on a step that reached nobody, so these offsets are the slowest the
 * policy can run, not the only way it can run. The card has to say which of the two it is. */
describe("PolicyTimeline — what the offsets assume", () => {
  it("says the offsets assume every step reaches somebody", () => {
    show([step({ level: 1, delayMinutes: 0 })]);

    expect(
      screen.getByText(/assuming every step reaches somebody and none of them acknowledges/i),
    ).toBeInTheDocument();
    expect(screen.queryByText(/if nobody responds/i)).not.toBeInTheDocument();
  });

  it("warns that a schedule step reaching nobody lets the next one fire sooner", () => {
    show([step({ level: 1, delayMinutes: 0, targetType: "schedule", targetValue: "sched-1" })]);

    expect(screen.getByText(/does not hold the next one back/i)).toBeInTheDocument();
  });

  it("warns for a team step too", () => {
    show([step({ level: 1, delayMinutes: 0, targetType: "team", targetValue: "team-1" })]);

    expect(screen.getByText(/does not hold the next one back/i)).toBeInTheDocument();
  });

  // A named user is always somebody to wait for, so nothing there can run early.
  it("leaves the warning off a policy that only pages named people", () => {
    show([
      step({ level: 1, delayMinutes: 0, targetType: "user", targetValue: "", targetUserIds: ["u-1"] }),
      step({ level: 2, delayMinutes: 5, targetType: "user", targetValue: "", targetUserIds: ["u-1"] }),
    ]);

    expect(screen.queryByText(/does not hold the next one back/i)).not.toBeInTheDocument();
  });
});

describe("PolicyTimeline — the drawing", () => {
  it("spaces the steps by how long the wait is, clamped so a long one cannot erase a short one", () => {
    const { container } = show([
      step({ level: 1, delayMinutes: 0 }),
      step({ level: 2, delayMinutes: 2 }),
      step({ level: 3, delayMinutes: 240 }),
    ]);

    const rises = [...container.querySelectorAll("li span[aria-hidden]")].map(
      (el) => parseFloat((el as HTMLElement).style.height) || 0,
    );

    expect(rises[0]).toBeLessThan(rises[1]);
    expect(rises[1]).toBeLessThan(rises[2]);
    // 2 minutes against 240 is a hairline without the clamp; it stays legible.
    expect(rises[1]).toBeGreaterThanOrEqual(8);
  });
});
