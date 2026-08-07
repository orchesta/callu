import { t } from "@/shared/locales/i18n";
import type { EscalationStepDto } from "../types/escalation.types";
import type { PolicyFormValues } from "./policy-form-schema";

/** A step as the editor holds it; `targetType` is editor-only, because the API infers the target from
 * whichever of scheduleId/teamId/notifyUserIds is set. */
export interface LocalStep {
  id: string;
  level: number;
  delayMinutes: number;
  title: string;
  description: string;
  targetType: "schedule" | "team" | "user";
  /** Single target id (schedule, team, or first user). Use `targetUserIds` when targetType==="user". */
  targetValue: string;
  /** Multi-user list when targetType==="user". Empty for schedule/team targets. */
  targetUserIds: string[];
  /** Team-target only: notify every team member, not just on-call. */
  notifyAll: boolean;
  /** Schedule-target only: page both primary and secondary on-call. */
  notifyBothOnCall: boolean;
  isNew?: boolean;
}

/** LocalStep → the shape both the schema and the step endpoints expect. */
export function stepPayload(step: LocalStep): PolicyFormValues["steps"][number] {
  const userIds = step.targetUserIds.length > 0
    ? step.targetUserIds
    : step.targetValue
      ? [step.targetValue]
      : [];

  return {
    title: step.title,
    description: step.description || undefined,
    delayMinutes: step.delayMinutes,
    scheduleId: step.targetType === "schedule" ? step.targetValue || null : null,
    teamId: step.targetType === "team" ? step.targetValue || null : null,
    notifyUserIds: step.targetType === "user" ? userIds : [],
    notifyAllTeamMembers: step.notifyAll,
    notifyBothOnCall: step.notifyBothOnCall,
  };
}

/**
 * The schema's messages are English; the two rules this screen shipped before it validated with zod
 * already had locale keys, so keep using them.
 */
export function stepErrorMessages(stepError: unknown): string[] {
  if (!stepError || typeof stepError !== "object") return [];

  return Object.entries(stepError as Record<string, { message?: string } | undefined>)
    .map(([field, error]) => {
      if (!error?.message) return null;
      return field === "notifyUserIds" ? t("escalations.validationNoTarget") : error.message;
    })
    .filter((message): message is string => !!message);
}

export function apiStepToLocal(step: EscalationStepDto): LocalStep {
  let targetType: "schedule" | "team" | "user" = "schedule";
  let targetValue = "";
  let targetUserIds: string[] = [];

  if (step.teamId) {
    targetType = "team";
    targetValue = step.teamId;
  } else if (step.notifyUserIds && step.notifyUserIds.length > 0) {
    targetType = "user";
    targetValue = step.notifyUserIds[0];
    targetUserIds = [...step.notifyUserIds];
  } else if (step.scheduleId) {
    targetType = "schedule";
    targetValue = step.scheduleId;
  }

  return {
    id: step.id,
    level: step.level,
    delayMinutes: step.delayMinutes,
    title: step.title,
    description: step.description ?? "",
    targetType,
    targetValue,
    targetUserIds,
    notifyAll: step.notifyAllTeamMembers ?? false,
    notifyBothOnCall: step.notifyBothOnCall ?? false,
  };
}
