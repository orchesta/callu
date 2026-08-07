import { describe, it, expect } from "vitest";

/** Who the card names at a given instant. It has to match the set the server pages, or the
 * headline and the strip drawn under it name different people. */

import { onCallAt, toBands } from "./on-call-window";
import type { OnCallOverrideDto, ScheduleRotationDto } from "../types/schedule.types";

const AT = Date.parse("2026-07-29T10:00:00Z");

function shift(
  userId: string,
  startIso: string,
  endIso: string,
  rank: { isPrimary?: boolean; order?: number } = {},
): ScheduleRotationDto {
  return {
    id: `${userId}-${startIso}`,
    scheduleId: "s1",
    userId,
    userName: userId,
    isPrimary: rank.isPrimary ?? true,
    order: rank.order ?? 0,
    shiftLengthMinutes: 480,
    startUtc: startIso,
    endUtc: endIso,
  };
}

function override(
  userId: string,
  startIso: string,
  endIso: string,
  isActive = true,
): OnCallOverrideDto {
  return {
    id: `o-${userId}-${startIso}`,
    scheduleId: "s1",
    scheduleName: "Payments",
    overrideUserId: userId,
    overrideUserName: userId,
    startUtc: startIso,
    endUtc: endIso,
    isActive,
  };
}

describe("onCallAt — nobody", () => {
  it("names nobody when no shift covers the instant", () => {
    const bands = toBands([shift("ada", "2026-07-29T12:00:00Z", "2026-07-29T20:00:00Z")], []);

    expect(onCallAt(bands, AT)).toEqual([]);
  });

  it("treats the end of a shift as the moment it stops covering", () => {
    const bands = toBands([shift("ada", "2026-07-29T02:00:00Z", "2026-07-29T10:00:00Z")], []);

    expect(onCallAt(bands, AT)).toEqual([]);
  });

  it("treats the start of a shift as covered", () => {
    const bands = toBands([shift("ada", "2026-07-29T10:00:00Z", "2026-07-29T18:00:00Z")], []);

    expect(onCallAt(bands, AT).map((b) => b.userId)).toEqual(["ada"]);
  });
});

describe("onCallAt — the rotation", () => {
  it("names the holder of the shift covering the instant", () => {
    const bands = toBands(
      [
        shift("ada", "2026-07-29T02:00:00Z", "2026-07-29T18:00:00Z"),
        shift("mert", "2026-07-29T18:00:00Z", "2026-07-30T02:00:00Z"),
      ],
      [],
    );

    expect(onCallAt(bands, AT).map((b) => b.userId)).toEqual(["ada"]);
  });

  it("names both holders when two shifts overlap the instant", () => {
    const bands = toBands(
      [
        shift("ada", "2026-07-29T02:00:00Z", "2026-07-29T18:00:00Z"),
        shift("mert", "2026-07-29T08:00:00Z", "2026-07-29T16:00:00Z"),
      ],
      [],
    );

    expect(onCallAt(bands, AT).map((b) => b.userId)).toEqual(["ada", "mert"]);
  });

  it("ignores a cancelled override, leaving the rotation in place", () => {
    const bands = toBands(
      [shift("ada", "2026-07-29T02:00:00Z", "2026-07-29T18:00:00Z")],
      [override("efe", "2026-07-29T08:00:00Z", "2026-07-29T12:00:00Z", false)],
    );

    expect(onCallAt(bands, AT).map((b) => b.userId)).toEqual(["ada"]);
  });
});

describe("onCallAt — an override", () => {
  // The server puts the override in the primary slot and leaves the person it displaced in the
  // secondary one, and a step set to page both reaches both. Naming only the override would hide
  // somebody whose phone actually rings.
  it("names the override first and keeps the rotation holder it displaced", () => {
    const bands = toBands(
      [shift("ada", "2026-07-29T02:00:00Z", "2026-07-29T18:00:00Z")],
      [override("efe", "2026-07-29T08:00:00Z", "2026-07-29T12:00:00Z")],
    );

    const current = onCallAt(bands, AT);

    expect(current.map((b) => b.userId)).toEqual(["efe", "ada"]);
    expect(current[0].isOverride).toBe(true);
    expect(current[1].isOverride).toBe(false);
  });

  it("names one person when the override covers their own shift", () => {
    const bands = toBands(
      [shift("ada", "2026-07-29T02:00:00Z", "2026-07-29T18:00:00Z")],
      [override("ada", "2026-07-29T08:00:00Z", "2026-07-29T12:00:00Z")],
    );

    expect(onCallAt(bands, AT).map((b) => b.userId)).toEqual(["ada"]);
  });

  it("names only the override when no shift is running under it", () => {
    const bands = toBands([], [override("efe", "2026-07-29T08:00:00Z", "2026-07-29T12:00:00Z")]);

    expect(onCallAt(bands, AT).map((b) => b.userId)).toEqual(["efe"]);
  });

  // The server takes exactly one override — the latest-starting one covering the instant.
  it("applies one override at a time when two of them overlap", () => {
    const bands = toBands(
      [shift("ada", "2026-07-29T02:00:00Z", "2026-07-29T18:00:00Z")],
      [
        override("efe", "2026-07-29T08:00:00Z", "2026-07-29T14:00:00Z"),
        override("deniz", "2026-07-29T09:00:00Z", "2026-07-29T11:00:00Z"),
      ],
    );

    expect(onCallAt(bands, AT).map((b) => b.userId)).toEqual(["deniz", "ada"]);
  });

  it("keeps one backup, not every shift the override overlaps", () => {
    const bands = toBands(
      [
        shift("ada", "2026-07-29T02:00:00Z", "2026-07-29T18:00:00Z"),
        shift("mert", "2026-07-29T08:00:00Z", "2026-07-29T16:00:00Z"),
      ],
      [override("efe", "2026-07-29T08:00:00Z", "2026-07-29T12:00:00Z")],
    );

    expect(onCallAt(bands, AT).map((b) => b.userId)).toEqual(["efe", "ada"]);
  });
});

/** The server names at most two people, and picks them by rank — not by who started first. */
// toBands sorts by start, so a card that just returns everything covering `at` names a third
// person the server would never page, and can put the wrong one first.
describe("matching the set the server would page", () => {
  const WINDOW = ["2026-07-29T06:00:00Z", "2026-07-29T18:00:00Z"] as const;

  it("names the rotation flagged primary first, whatever order it was written in", () => {
    const bands = toBands(
      [
        shift("mert", ...WINDOW, { isPrimary: false, order: 1 }),
        shift("ada", ...WINDOW, { isPrimary: true, order: 2 }),
      ],
      [],
    );

    expect(onCallAt(bands, AT).map((b) => b.userId)).toEqual(["ada", "mert"]);
  });

  it("falls back to the lowest order when none is flagged primary", () => {
    const bands = toBands(
      [
        shift("mert", ...WINDOW, { isPrimary: false, order: 2 }),
        shift("ada", ...WINDOW, { isPrimary: false, order: 1 }),
      ],
      [],
    );

    expect(onCallAt(bands, AT).map((b) => b.userId)).toEqual(["ada", "mert"]);
  });

  it("names two even when three shifts cover the instant", () => {
    const bands = toBands(
      [
        shift("ada", ...WINDOW, { isPrimary: true, order: 1 }),
        shift("mert", ...WINDOW, { isPrimary: false, order: 2 }),
        shift("efe", ...WINDOW, { isPrimary: false, order: 3 }),
      ],
      [],
    );

    expect(onCallAt(bands, AT).map((b) => b.userId)).toEqual(["ada", "mert"]);
  });

  it("keeps the displaced primary as the backup behind an override", () => {
    const bands = toBands(
      [
        shift("ada", ...WINDOW, { isPrimary: true, order: 1 }),
        shift("mert", ...WINDOW, { isPrimary: false, order: 2 }),
      ],
      [override("efe", ...WINDOW)],
    );

    expect(onCallAt(bands, AT).map((b) => b.userId)).toEqual(["efe", "ada"]);
  });
});
