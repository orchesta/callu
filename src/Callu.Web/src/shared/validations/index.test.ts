import { describe, it, expect } from "vitest";
import {
  alertRuleSchema,
  escalationPolicySchema,
  escalationStepSchema,
  incidentNoteSchema,
  loginSchema,
} from "./index";

// A schema looser than the server is a broken form; one that is tighter blocks valid input.

const validStep = {
  title: "Page the primary",
  delayMinutes: 0,
  notifyUserIds: ["u1"],
};

describe("escalationStepSchema", () => {
  it("accepts a step that names users", () => {
    expect(escalationStepSchema.safeParse(validStep).success).toBe(true);
  });

  it("accepts a step that targets a schedule or a team instead of users", () => {
    expect(
      escalationStepSchema.safeParse({
        title: "Page the on-call",
        delayMinutes: 5,
        scheduleId: "s1",
        notifyUserIds: [],
      }).success
    ).toBe(true);

    expect(
      escalationStepSchema.safeParse({
        title: "Page the team",
        delayMinutes: 5,
        teamId: "t1",
        notifyUserIds: [],
      }).success
    ).toBe(true);
  });

  it("rejects a step with no target at all — it would page nobody", () => {
    const result = escalationStepSchema.safeParse({
      title: "Page nobody",
      delayMinutes: 0,
      notifyUserIds: [],
    });

    expect(result.success).toBe(false);
    expect(result.error?.issues[0]?.path).toEqual(["notifyUserIds"]);
  });

  it("rejects a blank user id — an unselected picker is not a target", () => {
    expect(escalationStepSchema.safeParse({ ...validStep, notifyUserIds: [""] }).success).toBe(false);
  });

  it("requires a title and trims it before measuring", () => {
    expect(escalationStepSchema.safeParse({ ...validStep, title: "   " }).success).toBe(false);
    expect(escalationStepSchema.safeParse({ ...validStep, title: "" }).success).toBe(false);
  });

  it("caps the title at EscalationStep.Title's 100 characters", () => {
    expect(escalationStepSchema.safeParse({ ...validStep, title: "x".repeat(100) }).success).toBe(true);
    expect(escalationStepSchema.safeParse({ ...validStep, title: "x".repeat(101) }).success).toBe(false);
  });

  it("caps the description at EscalationStep.Description's 500 characters", () => {
    expect(escalationStepSchema.safeParse({ ...validStep, description: "x".repeat(500) }).success).toBe(true);
    expect(escalationStepSchema.safeParse({ ...validStep, description: "x".repeat(501) }).success).toBe(false);
  });

  it("rejects a negative or fractional delay", () => {
    expect(escalationStepSchema.safeParse({ ...validStep, delayMinutes: -1 }).success).toBe(false);
    expect(escalationStepSchema.safeParse({ ...validStep, delayMinutes: 1.5 }).success).toBe(false);
    expect(escalationStepSchema.safeParse({ ...validStep, delayMinutes: 0 }).success).toBe(true);
  });
});

describe("escalationPolicySchema", () => {
  it("accepts a policy with at least one step", () => {
    const result = escalationPolicySchema.safeParse({
      name: "Primary paging",
      steps: [validStep],
    });

    expect(result.success).toBe(true);
    expect(result.data?.isActive).toBe(true);
  });

  it("rejects a policy with no steps — it would never page", () => {
    expect(escalationPolicySchema.safeParse({ name: "Empty", steps: [] }).success).toBe(false);
  });

  it("rejects a policy whose name is blank", () => {
    expect(escalationPolicySchema.safeParse({ name: "  ", steps: [validStep] }).success).toBe(false);
  });

  it("caps the name at EscalationPolicy.Name's 100 characters", () => {
    expect(escalationPolicySchema.safeParse({ name: "x".repeat(100), steps: [validStep] }).success).toBe(true);
    expect(escalationPolicySchema.safeParse({ name: "x".repeat(101), steps: [validStep] }).success).toBe(false);
  });

  it("caps the description at EscalationPolicy.Description's 500 characters", () => {
    const parse = (description: string) =>
      escalationPolicySchema.safeParse({ name: "Primary paging", description, steps: [validStep] }).success;

    expect(parse("x".repeat(500))).toBe(true);
    expect(parse("x".repeat(501))).toBe(false);
  });

  it("propagates a bad step out of the policy", () => {
    const result = escalationPolicySchema.safeParse({
      name: "Primary paging",
      steps: [{ title: "No target", delayMinutes: 0, notifyUserIds: [] }],
    });

    expect(result.success).toBe(false);
  });
});

describe("incidentNoteSchema", () => {
  it("accepts a note and trims surrounding whitespace", () => {
    const result = incidentNoteSchema.safeParse({ content: "  Rebooted the pod  " });

    expect(result.success).toBe(true);
    expect(result.data?.content).toBe("Rebooted the pod");
  });

  it("rejects an empty or whitespace-only note", () => {
    expect(incidentNoteSchema.safeParse({ content: "" }).success).toBe(false);
    expect(incidentNoteSchema.safeParse({ content: "   " }).success).toBe(false);
  });

  it("caps the note at IncidentNote.Content's 4000 characters", () => {
    expect(incidentNoteSchema.safeParse({ content: "x".repeat(4000) }).success).toBe(true);
    expect(incidentNoteSchema.safeParse({ content: "x".repeat(4001) }).success).toBe(false);
  });
});

describe("loginSchema", () => {
  it("accepts a well-formed login", () => {
    expect(loginSchema.safeParse({ email: "ada@example.io", password: "hunter2" }).success).toBe(true);
  });

  it("rejects a malformed email", () => {
    expect(loginSchema.safeParse({ email: "ada", password: "hunter2" }).success).toBe(false);
  });

  it("rejects an empty password without imposing a strength rule the server does not", () => {
    expect(loginSchema.safeParse({ email: "ada@example.io", password: "" }).success).toBe(false);
    expect(loginSchema.safeParse({ email: "ada@example.io", password: "x" }).success).toBe(true);
  });
});

describe("alertRuleSchema", () => {
  const validRule = {
    name: "Page on 5xx",
    priority: 1,
    conditions: [{ field: "status", operator: "eq", value: "500" }],
    actions: [{ type: "Notify" }],
  };

  it("accepts a rule with at least one condition and one action", () => {
    const result = alertRuleSchema.safeParse(validRule);

    expect(result.success).toBe(true);
    expect(result.data?.isEnabled).toBe(true);
  });

  it("rejects a rule with no conditions or no actions", () => {
    expect(alertRuleSchema.safeParse({ ...validRule, conditions: [] }).success).toBe(false);
    expect(alertRuleSchema.safeParse({ ...validRule, actions: [] }).success).toBe(false);
  });

  it("holds priority inside the 1..999 range the backend accepts", () => {
    expect(alertRuleSchema.safeParse({ ...validRule, priority: 0 }).success).toBe(false);
    expect(alertRuleSchema.safeParse({ ...validRule, priority: 1000 }).success).toBe(false);
    expect(alertRuleSchema.safeParse({ ...validRule, priority: 999 }).success).toBe(true);
  });

  it("caps the description at AlertRule.Description's 1000 characters", () => {
    expect(alertRuleSchema.safeParse({ ...validRule, description: "x".repeat(1000) }).success).toBe(true);
    expect(alertRuleSchema.safeParse({ ...validRule, description: "x".repeat(1001) }).success).toBe(false);
  });

  it("rejects a condition that is missing a value", () => {
    expect(
      alertRuleSchema.safeParse({
        ...validRule,
        conditions: [{ field: "status", operator: "eq", value: "" }],
      }).success
    ).toBe(false);
  });
});
