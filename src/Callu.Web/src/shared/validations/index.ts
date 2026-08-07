import { z } from "zod";

export const alertRuleConditionSchema = z.object({
  field: z.string().min(1, "Condition field is required"),
  operator: z.string().min(1, "Operator is required"),
  value: z.string().min(1, "Condition value is required"),
});

export const alertRuleActionSchema = z.object({
  type: z.string().min(1, "Action type is required"),
  target: z.string().optional(),
  value: z.string().optional(),
  // SuppressNotification only: opt-in paging suppression.
  suppressPaging: z.boolean().optional(),
});

export const alertRuleSchema = z.object({
  name: z.string().min(2, "Name must be at least 2 characters").max(100),
  description: z.string().max(1000, "Description cannot exceed 1000 characters").optional(),
  priority: z.number().min(1, "Priority must be at least 1").max(999, "Priority cannot exceed 999"),
  isEnabled: z.boolean().default(true),
  conditions: z.array(alertRuleConditionSchema).min(1, "At least one condition is required"),
  actions: z.array(alertRuleActionSchema).min(1, "At least one action is required"),
});

export type AlertRuleFormData = z.infer<typeof alertRuleSchema>;

// Escalation policy and step limits mirror the backend validators, so a form that passes here does
// not come back as a 400 with nothing to attach it to.
export const escalationStepSchema = z
  .object({
    title: z
      .string()
      .trim()
      .min(1, "Step title is required")
      .max(100, "Step title cannot exceed 100 characters"),
    description: z.string().max(500, "Description cannot exceed 500 characters").optional(),
    delayMinutes: z
      .number({ message: "Delay must be a number" })
      .int("Delay must be a whole number")
      .min(0, "Delay must be 0 or greater"),
    scheduleId: z.string().nullish(),
    teamId: z.string().nullish(),
    notifyAllTeamMembers: z.boolean().optional(),
    notifyBothOnCall: z.boolean().optional(),
    // A blank id is not a target — the backend rejects it, and an unselected picker must not
    // pass as "someone will be paged".
    notifyUserIds: z.array(z.string().min(1, "Select a user")).default([]),
  })
  .refine(
    (step) => step.notifyUserIds.length > 0 || !!step.scheduleId || !!step.teamId,
    {
      message: "Select users, a schedule, or a team for notifications",
      path: ["notifyUserIds"],
    }
  );

export type EscalationStepFormData = z.infer<typeof escalationStepSchema>;

export const escalationPolicySchema = z.object({
  name: z
    .string()
    .trim()
    .min(1, "Policy name is required")
    .max(100, "Policy name cannot exceed 100 characters"),
  description: z.string().max(500, "Description cannot exceed 500 characters").optional(),
  teamId: z.string().nullish(),
  isActive: z.boolean().default(true),
  exhaustionBehavior: z.enum(["Stop", "Repeat"]).default("Stop"),
  maxRepeatCycles: z.number().int().min(1).max(10).default(3),
  maxRepeatDurationMinutes: z.number().int().min(60).max(24 * 60).default(4 * 60),
  steps: z.array(escalationStepSchema).min(1, "At least one step is required"),
});

export type EscalationPolicyFormData = z.infer<typeof escalationPolicySchema>;

/** Incident note. Content limit matches IncidentNote.Content ([StringLength(4000)]). */
export const incidentNoteSchema = z.object({
  content: z
    .string()
    .trim()
    .min(1, "Note content is required")
    .max(4000, "Note cannot exceed 4000 characters"),
});

export type IncidentNoteFormData = z.infer<typeof incidentNoteSchema>;

export const loginSchema = z.object({
  email: z.email("Enter a valid email address"),
  password: z.string().min(1, "Password is required"),
});

export type LoginFormData = z.infer<typeof loginSchema>;
