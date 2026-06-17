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
});

export const alertRuleSchema = z.object({
  name: z.string().min(2, "Name must be at least 2 characters").max(100),
  description: z.string().optional(),
  priority: z.number().min(1, "Priority must be at least 1").max(999, "Priority cannot exceed 999"),
  isEnabled: z.boolean().default(true),
  conditions: z.array(alertRuleConditionSchema).min(1, "At least one condition is required"),
  actions: z.array(alertRuleActionSchema).min(1, "At least one action is required"),
});

export type AlertRuleFormData = z.infer<typeof alertRuleSchema>;
