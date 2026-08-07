import { t } from "@/shared/locales/i18n";
import type { LocalStep } from "./step-mapping";

/** The pickable targets, as the editor already has them loaded. */
export interface TargetDirectory {
  users: readonly {
    id: string;
    email: string;
    displayName?: string;
    firstName?: string;
    lastName?: string;
  }[];
  teams: readonly { id: string; name: string }[];
  schedules: readonly { id: string; name: string }[];
}

/** The name an admin recognises a user by. */
// A user who has since been removed is still referenced by the step, so a truncated id beats an
// empty label: it is at least searchable.
export function userLabel(users: TargetDirectory["users"], userId: string): string {
  const user = users.find((u) => u.id === userId);
  if (!user) return userId.slice(0, 8);
  return `${user.firstName ?? ""} ${user.lastName ?? ""}`.trim() || user.displayName || user.email;
}

/** Who a step reaches, in the names the admin picked them by. */
export function describeTarget(step: LocalStep, directory: TargetDirectory): string {
  if (step.targetType === "user") {
    const ids = step.targetUserIds.length > 0
      ? step.targetUserIds
      : step.targetValue
        ? [step.targetValue]
        : [];
    return ids.length > 0
      ? ids.map((id) => userLabel(directory.users, id)).join(", ")
      : t("escalations.notConfigured");
  }

  if (!step.targetValue) return t("escalations.notConfigured");

  return step.targetType === "team"
    ? directory.teams.find((tm) => tm.id === step.targetValue)?.name ?? step.targetValue
    : directory.schedules.find((s) => s.id === step.targetValue)?.name ?? step.targetValue;
}
