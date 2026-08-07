export interface ShiftLengthParams {
  shiftStart: string;
  shiftEnd: string;
  is247: boolean;
  daysPerMember: number;
}

export function computeShiftLengthMinutes({
  shiftStart,
  shiftEnd,
  is247,
  daysPerMember,
}: ShiftLengthParams): number {
  if (is247) {
    return daysPerMember * 24 * 60;
  }

  const [startHour, startMinute] = shiftStart.split(":").map(Number);
  const [endHour, endMinute] = shiftEnd.split(":").map(Number);

  let minutes = endHour * 60 + endMinute - (startHour * 60 + startMinute);
  if (minutes <= 0) {
    minutes += 24 * 60;
  }
  return minutes;
}

export interface RotationTemplateParams {
  /** Shared cycle anchor — every member is phased off this single date. */
  anchor: Date;
  /** 0-based position of the member in the rotation order. */
  memberIndex: number;
  daysPerMember: number;
  shiftStart: string;
  shiftEnd: string;
  is247: boolean;
}

export interface RotationTemplate {
  handoverStartLocal: string;
  shiftLengthMinutes: number;
  /**
   * Calendar days the member owns per cycle — the backend materializes one occurrence per
   * owned day. Undefined for 24/7, where a single block already spans the whole window.
   */
  ownershipDays?: number;
}

/** The API accepts ownershipDays 1..31. Longer non-24/7 blocks are not expressible. */
export const MAX_OWNERSHIP_DAYS = 31;

/** The API caps shiftLengthMinutes at 30 days, which bounds a 24/7 block to 30 days. */
export const MAX_SHIFT_LENGTH_MINUTES = 60 * 24 * 30;
export const MAX_247_BLOCK_DAYS = MAX_SHIFT_LENGTH_MINUTES / (24 * 60);

/** Longest block the API accepts for the given coverage mode. */
export function maxBlockDays(is247: boolean): number {
  return is247 ? MAX_247_BLOCK_DAYS : MAX_OWNERSHIP_DAYS;
}

/** Whether a block is within the caps the API enforces. An over-limit block is rejected outright, so
 * callers must refuse to save one rather than let it degrade into partly uncovered cycles. */
export function isRotationBlockSupported(daysPerMember: number, is247: boolean): boolean {
  if (!Number.isFinite(daysPerMember) || daysPerMember < 1) return false;
  return daysPerMember <= maxBlockDays(is247);
}

/**
 * Formats a wall clock carried in a Date's UTC fields. UTC is used as a plain calendar with
 * no daylight saving, so the value read back is exactly the one that was written.
 */
function formatWallClock(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, "0");
  return (
    `${date.getUTCFullYear()}-${pad(date.getUTCMonth() + 1)}-${pad(date.getUTCDate())}T` +
    `${pad(date.getUTCHours())}:${pad(date.getUTCMinutes())}:00`
  );
}

/** Shared phase anchor taken from the earliest persisted handover, keeping its clock time. Derived
 * from stored data rather than the current time, so re-saving an unchanged schedule is idempotent. */
export function resolveCycleAnchor(
  handovers: readonly (string | null | undefined)[],
  fallback?: string | null,
): Date {
  const times = handovers
    .filter((h): h is string => !!h)
    .map((h) => new Date(h).getTime())
    .filter((ms) => Number.isFinite(ms));

  if (times.length > 0) {
    return new Date(Math.min(...times));
  }

  let anchor = fallback ? new Date(fallback) : new Date();
  if (Number.isNaN(anchor.getTime())) {
    anchor = new Date();
  }
  anchor.setHours(0, 0, 0, 0);
  return anchor;
}

/**
 * Builds the rotation template for one member: its handover is the shared anchor shifted by
 * its position in the order, so the whole schedule stays on one grid.
 */
export function buildRotationTemplate({
  anchor,
  memberIndex,
  daysPerMember,
  shiftStart,
  shiftEnd,
  is247,
}: RotationTemplateParams): RotationTemplate {
  // `handoverStartLocal` is a wall clock the backend resolves in the schedule's IANA zone, so
  // the arithmetic runs in UTC — local-zone mutators normalise it away through a DST gap.
  const [startHour, startMinute] = is247
    ? [anchor.getHours(), anchor.getMinutes()]
    : shiftStart.split(":").map(Number);

  const handover = new Date(
    Date.UTC(
      anchor.getFullYear(),
      anchor.getMonth(),
      anchor.getDate() + memberIndex * daysPerMember,
      startHour,
      startMinute,
      0,
      0,
    ),
  );

  return {
    handoverStartLocal: formatWallClock(handover),
    shiftLengthMinutes: computeShiftLengthMinutes({ shiftStart, shiftEnd, is247, daysPerMember }),
    // Out-of-range values are sent as-is so the API rejects them loudly; the UI blocks the
    // configuration before it gets here.
    ownershipDays: is247 ? undefined : daysPerMember,
  };
}
