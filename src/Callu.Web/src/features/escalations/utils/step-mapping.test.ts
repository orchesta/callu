import { describe, it, expect } from "vitest";
import { stepPayload, stepErrorMessages, apiStepToLocal, type LocalStep } from "./step-mapping";
import type { EscalationStepDto } from "../types/escalation.types";

/** stepPayload decides who a step pages, so these pin that switching target type clears the id
 * fields belonging to the other types. */

function local(over: Partial<LocalStep> = {}): LocalStep {
  return {
    id: "s1",
    level: 1,
    delayMinutes: 0,
    title: "Page primary",
    description: "",
    targetType: "schedule",
    targetValue: "",
    targetUserIds: [],
    notifyAll: false,
    notifyBothOnCall: false,
    ...over,
  };
}

function dto(over: Partial<EscalationStepDto> = {}): EscalationStepDto {
  return {
    id: "s1",
    escalationPolicyId: "p1",
    level: 1,
    title: "Page primary",
    delayMinutes: 0,
    notifyUserIds: [],
    notifyUserNames: [],
    ...over,
  };
}

describe("stepPayload — target routing", () => {
  it("sends a schedule target as scheduleId and nothing else", () => {
    const p = stepPayload(local({ targetType: "schedule", targetValue: "sched-1" }));
    expect(p.scheduleId).toBe("sched-1");
    expect(p.teamId).toBeNull();
    expect(p.notifyUserIds).toEqual([]);
  });

  it("sends a team target as teamId, leaving scheduleId null", () => {
    const p = stepPayload(local({ targetType: "team", targetValue: "team-1" }));
    expect(p.teamId).toBe("team-1");
    expect(p.scheduleId).toBeNull();
    expect(p.notifyUserIds).toEqual([]);
  });

  it("sends a user target as notifyUserIds, leaving both ids null", () => {
    const p = stepPayload(local({ targetType: "user", targetUserIds: ["u1", "u2"] }));
    expect(p.notifyUserIds).toEqual(["u1", "u2"]);
    expect(p.scheduleId).toBeNull();
    expect(p.teamId).toBeNull();
  });

  // The regression this screen actually had: targetValue still holds the old schedule id after the
  // user switches to a team, and it must not travel as a schedule.
  it("does not leak a stale targetValue into the wrong field after a type switch", () => {
    const switched = local({ targetType: "team", targetValue: "team-1", targetUserIds: ["u1"] });
    const p = stepPayload(switched);
    expect(p.teamId).toBe("team-1");
    expect(p.scheduleId).toBeNull();
    expect(p.notifyUserIds).toEqual([]);
  });

  it("falls back to targetValue as the single user when the multi-list is empty", () => {
    const p = stepPayload(local({ targetType: "user", targetValue: "u9", targetUserIds: [] }));
    expect(p.notifyUserIds).toEqual(["u9"]);
  });

  it("prefers the multi-user list over targetValue when both are set", () => {
    const p = stepPayload(local({ targetType: "user", targetValue: "u9", targetUserIds: ["u1", "u2"] }));
    expect(p.notifyUserIds).toEqual(["u1", "u2"]);
  });

  it("sends an unset target as null rather than an empty string", () => {
    expect(stepPayload(local({ targetType: "schedule", targetValue: "" })).scheduleId).toBeNull();
    expect(stepPayload(local({ targetType: "team", targetValue: "" })).teamId).toBeNull();
  });

  it("sends no user ids when a user step has no target at all", () => {
    expect(stepPayload(local({ targetType: "user" })).notifyUserIds).toEqual([]);
  });

  it("drops an empty description rather than sending a blank string", () => {
    expect(stepPayload(local({ description: "" })).description).toBeUndefined();
    expect(stepPayload(local({ description: "why" })).description).toBe("why");
  });

  it("carries the two on-call flags through", () => {
    const p = stepPayload(local({ notifyAll: true, notifyBothOnCall: true }));
    expect(p.notifyAllTeamMembers).toBe(true);
    expect(p.notifyBothOnCall).toBe(true);
  });
});

describe("apiStepToLocal — target inference", () => {
  it("reads a team step back as a team target", () => {
    const s = apiStepToLocal(dto({ teamId: "team-1" }));
    expect(s.targetType).toBe("team");
    expect(s.targetValue).toBe("team-1");
  });

  it("reads a user step back as a user target, keeping the whole list", () => {
    const s = apiStepToLocal(dto({ notifyUserIds: ["u1", "u2"] }));
    expect(s.targetType).toBe("user");
    expect(s.targetValue).toBe("u1");
    expect(s.targetUserIds).toEqual(["u1", "u2"]);
  });

  it("copies the user list rather than aliasing the DTO's array", () => {
    const source = dto({ notifyUserIds: ["u1"] });
    const s = apiStepToLocal(source);
    s.targetUserIds.push("u2");
    expect(source.notifyUserIds).toEqual(["u1"]);
  });

  it("reads a schedule step back as a schedule target", () => {
    const s = apiStepToLocal(dto({ scheduleId: "sched-1" }));
    expect(s.targetType).toBe("schedule");
    expect(s.targetValue).toBe("sched-1");
  });

  // Pinned precedence: team wins over users, users win over schedule. A step carrying more than
  // one of these is already malformed; this records which one the editor shows.
  it("prefers team over users, and users over schedule", () => {
    expect(apiStepToLocal(dto({ teamId: "t", notifyUserIds: ["u"], scheduleId: "s" })).targetType).toBe("team");
    expect(apiStepToLocal(dto({ notifyUserIds: ["u"], scheduleId: "s" })).targetType).toBe("user");
  });

  it("defaults an targetless step to an empty schedule target", () => {
    const s = apiStepToLocal(dto());
    expect(s.targetType).toBe("schedule");
    expect(s.targetValue).toBe("");
  });

  it("defaults the optional flags to false and description to empty", () => {
    const s = apiStepToLocal(dto());
    expect(s.notifyAll).toBe(false);
    expect(s.notifyBothOnCall).toBe(false);
    expect(s.description).toBe("");
  });

  it("round-trips a team step through stepPayload unchanged", () => {
    const p = stepPayload(apiStepToLocal(dto({ teamId: "team-1", notifyAllTeamMembers: true })));
    expect(p.teamId).toBe("team-1");
    expect(p.scheduleId).toBeNull();
    expect(p.notifyAllTeamMembers).toBe(true);
  });
});

describe("stepErrorMessages", () => {
  it("returns nothing for a step with no errors", () => {
    expect(stepErrorMessages(undefined)).toEqual([]);
    expect(stepErrorMessages(null)).toEqual([]);
    expect(stepErrorMessages("not an object")).toEqual([]);
  });

  it("passes a field's own message through", () => {
    expect(stepErrorMessages({ title: { message: "Title is required" } })).toEqual(["Title is required"]);
  });

  it("skips fields that carry no message", () => {
    expect(stepErrorMessages({ title: { message: "boom" }, delayMinutes: undefined, description: {} })).toEqual(["boom"]);
  });

  // notifyUserIds is the one field whose schema message is replaced by this screen's own locale key.
  it("substitutes the localized no-target message for notifyUserIds", () => {
    const [message] = stepErrorMessages({ notifyUserIds: { message: "Array must contain at least 1 element(s)" } });
    expect(message).not.toBe("Array must contain at least 1 element(s)");
    expect(message).toBeTruthy();
  });
});
