import { describe, it, expect } from "vitest";

/** The one line on a step that says who it reaches. An admin signs off on the policy from it. */

import { describeTarget, type TargetDirectory } from "./describe-target";
import type { LocalStep } from "./step-mapping";

const DIRECTORY: TargetDirectory = {
  users: [
    { id: "u-named", email: "ada@example.com", firstName: "Ada", lastName: "Lovelace" },
    { id: "u-display", email: "efe@example.com", displayName: "Efe K." },
    { id: "u-bare", email: "deniz@example.com" },
  ],
  teams: [{ id: "team-1", name: "DB team" }],
  schedules: [{ id: "sched-1", name: "Primary on-call" }],
};

function step(over: Partial<LocalStep> = {}): LocalStep {
  return {
    id: "step-1",
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

const label = (over: Partial<LocalStep>) => describeTarget(step(over), DIRECTORY);

describe("describeTarget — schedules and teams", () => {
  it("names the schedule", () => {
    expect(label({ targetType: "schedule", targetValue: "sched-1" })).toBe("Primary on-call");
  });

  it("names the team", () => {
    expect(label({ targetType: "team", targetValue: "team-1" })).toBe("DB team");
  });

  // A schedule deleted out from under the policy still leaves the step pointing at its id.
  it("falls back to the id when the schedule is not in the list", () => {
    expect(label({ targetType: "schedule", targetValue: "sched-gone" })).toBe("sched-gone");
  });

  it("falls back to the id when the team is not in the list", () => {
    expect(label({ targetType: "team", targetValue: "team-gone" })).toBe("team-gone");
  });
});

// An unset target is the state that pages nobody, so it has to read as a warning rather than as a
// blank the eye skips over.
describe("describeTarget — nothing chosen", () => {
  it("says so for a schedule step with no schedule", () => {
    expect(label({ targetType: "schedule", targetValue: "" })).toBe("Not configured");
  });

  it("says so for a team step with no team", () => {
    expect(label({ targetType: "team", targetValue: "" })).toBe("Not configured");
  });

  it("says so for a user step with nobody picked", () => {
    expect(label({ targetType: "user", targetValue: "", targetUserIds: [] })).toBe(
      "Not configured",
    );
  });
});

describe("describeTarget — users", () => {
  it("uses the first and last name", () => {
    expect(label({ targetType: "user", targetUserIds: ["u-named"] })).toBe("Ada Lovelace");
  });

  it("uses the display name when there is no first or last name", () => {
    expect(label({ targetType: "user", targetUserIds: ["u-display"] })).toBe("Efe K.");
  });

  it("uses the email when there is no name at all", () => {
    expect(label({ targetType: "user", targetUserIds: ["u-bare"] })).toBe(
      "deniz@example.com",
    );
  });

  it("lists every user the step pages", () => {
    expect(label({ targetType: "user", targetUserIds: ["u-named", "u-display"] })).toBe(
      "Ada Lovelace, Efe K.",
    );
  });

  // Older steps carry a single id in targetValue rather than in the list.
  it("reads a single user out of targetValue when the list is empty", () => {
    expect(label({ targetType: "user", targetValue: "u-named", targetUserIds: [] })).toBe(
      "Ada Lovelace",
    );
  });

  it("shortens an id that matches nobody rather than showing an empty name", () => {
    expect(label({ targetType: "user", targetUserIds: ["0123456789abcdef"] })).toBe(
      "01234567",
    );
  });
});
