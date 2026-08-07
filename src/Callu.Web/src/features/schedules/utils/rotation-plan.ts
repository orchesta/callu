import type {
  RecurrenceType,
  ScheduleRotationDto,
  SchedulePlanRotation,
} from "../types/schedule.types";
import { buildRotationTemplate } from "./rotation-template";

/** Cadence enum that best describes a full cycle of `days`. */
export function cadenceForCycle(days: number): RecurrenceType {
  if (days <= 1) return "Daily";
  if (days <= 7) return "Weekly";
  if (days <= 14) return "Biweekly";
  return "Monthly";
}

export interface RotationPlanInput {
  /** Rotations as currently stored. */
  rotations: readonly ScheduleRotationDto[];
  /** Member ids in rotation order; a stored rotation missing from this list is removed. */
  selectedMemberIds: readonly string[];
  /** Whether the form changed the rotation layout. False means no rotation field may be rewritten,
   * because the form's timing fields are derived from the first rotation alone. */
  rotationLayoutChanged: boolean;
  anchor: Date;
  daysPerMember: number;
  shiftStart: string;
  shiftEnd: string;
  is247: boolean;
}

/** The rotations the schedule should have after this save, in order, or null when the layout is
 * untouched — which the caller must turn into an omitted `rotations` field. */
export function buildSchedulePlanRotations(
  input: RotationPlanInput,
): SchedulePlanRotation[] | null {
  const {
    rotations,
    selectedMemberIds,
    rotationLayoutChanged,
    anchor,
    daysPerMember,
    shiftStart,
    shiftEnd,
    is247,
  } = input;

  if (!rotationLayoutChanged) return null;

  const intervalDays = Math.max(selectedMemberIds.length, 1) * daysPerMember;
  const cadence = cadenceForCycle(intervalDays);

  return selectedMemberIds.map((userId, index) => {
    const tpl = buildRotationTemplate({
      anchor,
      memberIndex: index,
      daysPerMember,
      shiftStart,
      shiftEnd,
      is247,
    });
    // Reuse the member's stored rotation row so its id, and anything hanging off it, survives
    // a re-order. A member with no stored rotation gets a new one (no id).
    const existing = rotations.find((r) => r.userId === userId);

    return {
      id: existing?.id,
      userId,
      handoverStartLocal: tpl.handoverStartLocal,
      shiftLengthMinutes: tpl.shiftLengthMinutes,
      ownershipDays: tpl.ownershipDays,
      isPrimary: index === 0,
      order: index + 1,
      recurrenceType: cadence,
      recurrenceIntervalDays: intervalDays,
    };
  });
}
