import type { ScheduleDetailDto } from "../types/schedule.types";

/** The config form derived from a stored schedule — a lossy view that re-expresses the first rotation
 * alone as one shift window plus one cadence, so it cannot round-trip differing rotations. */
export interface ScheduleFormValues {
  scheduleName: string;
  description: string;
  teamId: string;
  scheduleTimezone: string;
  rotationType: "daily" | "weekly" | "custom";
  /** Days per member, as a form string. Paired with rotationType. */
  rotationInterval: string;
  shiftStart: string;
  shiftEnd: string;
  /** Unique member ids in rotation order; the first is the primary. */
  memberIds: string[];
}

/** Recovers the form fields from a stored schedule. Pure, so re-saving an untouched schedule cannot
 * drift the rota; the caller must seed the inputs and the "unchanged" snapshot from one call. */
export function deriveFormFromSchedule(schedule: ScheduleDetailDto): ScheduleFormValues {
  const sortedRotations = [...schedule.rotations].sort((a, b) => a.order - b.order);
  const memberIds = [...new Set(sortedRotations.map((r) => r.userId))];

  let rotationType: ScheduleFormValues["rotationType"] = "weekly";
  let rotationInterval = "7";
  let shiftStart = "00:00";
  let shiftEnd = "23:59";

  if (sortedRotations.length > 0) {
    const firstRotation = sortedRotations[0];
    const handover = firstRotation.handoverStartLocal ? new Date(firstRotation.handoverStartLocal) : new Date();
    const shiftLen = firstRotation.shiftLengthMinutes ?? 1440;
    const pad = (n: number) => n.toString().padStart(2, "0");

    // A block of 24h or more is modelled as 24/7 in this form. The rotation's handover clock
    // time is not lost by that: it lives on in the cycle anchor.
    if (shiftLen < 24 * 60) {
      const startH = handover.getHours();
      const startM = handover.getMinutes();
      shiftStart = `${pad(startH)}:${pad(startM)}`;
      const totalEnd = startH * 60 + startM + shiftLen;
      shiftEnd = `${pad(Math.floor(totalEnd / 60) % 24)}:${pad(totalEnd % 60)}`;
    }

    const n = Math.max(memberIds.length, 1);
    if (firstRotation.recurrenceIntervalDays != null) {
      const daysPerMember = Math.max(1, Math.round(firstRotation.recurrenceIntervalDays / n));
      if (daysPerMember === 1) {
        rotationType = "daily";
        rotationInterval = "1";
      } else if (daysPerMember === 7) {
        rotationType = "weekly";
        rotationInterval = "7";
      } else {
        rotationType = "custom";
        rotationInterval = String(daysPerMember);
      }
    } else {
      const recType = firstRotation.recurrenceType ?? "Weekly";
      if (recType === "Daily") {
        rotationType = "daily";
        rotationInterval = "1";
      } else if (recType === "Weekly" || recType === "None") {
        rotationType = "weekly";
        rotationInterval = "7";
      } else if (recType === "Biweekly") {
        rotationType = "custom";
        rotationInterval = "14";
      } else {
        rotationType = "custom";
        rotationInterval = "30";
      }
    }
  }

  return {
    scheduleName: schedule.name ?? "",
    description: schedule.description ?? "",
    teamId: schedule.teamId ?? "",
    scheduleTimezone: schedule.timezone ?? "UTC",
    rotationType,
    rotationInterval,
    shiftStart,
    shiftEnd,
    memberIds,
  };
}
