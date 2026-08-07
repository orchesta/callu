import type { OnCallOverrideDto, ScheduleRotationDto } from "../types/schedule.types";

export type Band = {
  key: string;
  label: string;
  userId: string;
  colour: string;
  start: number;
  end: number;
  isOverride: boolean;
  isPrimary: boolean;
  order: number;
};

export type Gap = { start: number; end: number };

// A gap shorter than this is a rounding artefact between two adjacent shifts, not a hole in cover.
const GAP_FLOOR_MS = 60_000;

// Distinct on a dark surface and distinguishable with the common colour deficiencies. Colour is
// never the only carrier here: every block is labelled and titled, and gaps are called out in
// words below the strip.
const PALETTE = [
  "#5b8def",
  "#2ee6a6",
  "#c08cff",
  "#ffb05c",
  "#38e0ff",
  "#ff8fa3",
  "#9fd356",
  "#f7d154",
];

export function colourFor(userId: string): string {
  let hash = 0;
  for (let i = 0; i < userId.length; i++) hash = (hash * 31 + userId.charCodeAt(i)) >>> 0;
  return PALETTE[hash % PALETTE.length];
}

/** How far the zone is from UTC at a given instant, in milliseconds. */
function zoneOffsetMs(instant: number, tz: string): number {
  const parts = Object.fromEntries(
    new Intl.DateTimeFormat("en-US", {
      timeZone: tz,
      hour12: false,
      year: "numeric",
      month: "2-digit",
      day: "2-digit",
      hour: "2-digit",
      minute: "2-digit",
      second: "2-digit",
    })
      .formatToParts(new Date(instant))
      .map((p) => [p.type, p.value]),
  );
  const asIfUtc = Date.UTC(
    Number(parts.year),
    Number(parts.month) - 1,
    Number(parts.day),
    Number(parts.hour) % 24,
    Number(parts.minute),
    Number(parts.second),
  );
  return asIfUtc - instant;
}

/** The instant of local midnight, `dayOffset` calendar days from today, in the schedule's zone. */
// Not `midnight + n * 24h`: a DST day is 23 or 25 hours, so adding fixed days walks the ticks off
// the calendar. The offset is re-read after the first guess because the guess can land on the
// other side of a transition from the answer.
export function zonedMidnight(tz: string, dayOffset: number, from: Date = new Date()): number {
  const ymd = new Intl.DateTimeFormat("en-CA", {
    timeZone: tz,
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).format(from);
  const [year, month, day] = ymd.split("-").map(Number);

  const wallClock = Date.UTC(year, month - 1, day + dayOffset);
  const guess = wallClock - zoneOffsetMs(wallClock, tz);
  return wallClock - zoneOffsetMs(guess, tz);
}

export function toBands(
  occurrences: ScheduleRotationDto[],
  overrides: OnCallOverrideDto[],
): Band[] {
  const fromRotations = occurrences
    .filter((o) => o.startUtc && o.endUtc)
    .map((o, i) => ({
      key: `r-${o.id}-${i}`,
      label: o.userName ?? o.userId,
      userId: o.userId,
      colour: colourFor(o.userId),
      start: new Date(o.startUtc!).getTime(),
      end: new Date(o.endUtc!).getTime(),
      isOverride: false,
      isPrimary: o.isPrimary,
      order: o.order,
    }));

  const fromOverrides = overrides
    .filter((o) => o.isActive)
    .map((o) => ({
      key: `o-${o.id}`,
      label: o.overrideUserName ?? o.overrideUserId,
      userId: o.overrideUserId,
      colour: colourFor(o.overrideUserId),
      start: new Date(o.startUtc).getTime(),
      end: new Date(o.endUtc).getTime(),
      isOverride: true,
      isPrimary: false,
      order: 0,
    }));

  return [...fromRotations, ...fromOverrides]
    .filter((b) => Number.isFinite(b.start) && Number.isFinite(b.end) && b.end > b.start)
    .sort((a, b) => a.start - b.start);
}

/** The bands covering `at`, in the order the server would page them. */
// The server names at most two: the primary — the covering rotation flagged primary, else the
// first by order — and one other. An override takes the primary slot without standing anyone
// down, so the holder it displaced stays named as the backup; only the latest-starting override
// applies.
export function onCallAt(bands: Band[], at: number): Band[] {
  const covering = bands.filter((b) => b.start <= at && b.end > at);

  const rotations = covering
    .filter((b) => !b.isOverride)
    .sort((a, b) => a.order - b.order);

  const primary = rotations.find((b) => b.isPrimary) ?? rotations[0];
  const secondary = rotations.find((b) => b !== primary);

  const overrides = covering.filter((b) => b.isOverride);
  if (overrides.length === 0) return [primary, secondary].filter((b) => b !== undefined);

  const active = overrides.reduce((latest, b) => (b.start > latest.start ? b : latest));
  const backup = [primary, secondary].find((b) => b !== undefined && b.userId !== active.userId);
  return backup ? [active, backup] : [active];
}

/** Stretches of the window nobody is on call for. */
// Computed from the union of every band, not from the rotation list in order: an override can
// cover a hole the rotations leave, and two rotations can overlap.
export function findGaps(bands: Band[], from: number, to: number): Gap[] {
  const covered = [...bands]
    .map((b) => ({ start: Math.max(b.start, from), end: Math.min(b.end, to) }))
    .filter((b) => b.end > b.start)
    .sort((a, b) => a.start - b.start);

  const gaps: Gap[] = [];
  let cursor = from;
  for (const span of covered) {
    if (span.start - cursor > GAP_FLOOR_MS) gaps.push({ start: cursor, end: span.start });
    cursor = Math.max(cursor, span.end);
  }
  if (to - cursor > GAP_FLOOR_MS) gaps.push({ start: cursor, end: to });
  return gaps;
}
