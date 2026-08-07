import { z } from "zod";
import { escalationPolicySchema } from "@/shared/validations";
import { t } from "@/shared/locales/i18n";

/** The shared backend-mirroring schema plus two form-only rules — an owner team is required and the
 * first step pages immediately — with messages resolved at parse time so they follow the locale. */
export const policyFormSchema = escalationPolicySchema
  .extend({
    teamId: z.string().min(1, { error: () => t("escalations.selectTeamForPolicyPlaceholder") }),
  })
  .superRefine((policy, ctx) => {
    if ((policy.steps[0]?.delayMinutes ?? 0) > 0) {
      ctx.addIssue({
        code: "custom",
        path: ["steps", 0, "delayMinutes"],
        message: t("escalations.validationFirstStepDelay"),
      });
    }
  });

export type PolicyFormValues = z.input<typeof policyFormSchema>;
export type PolicyFormData = z.output<typeof policyFormSchema>;
