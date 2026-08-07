import "@testing-library/jest-dom/vitest";
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen } from "@testing-library/react";

import { OnCallStrip } from "./on-call-strip";
import { findGaps, toBands, zonedMidnight } from "../utils/on-call-window";
import type { OnCallOverrideDto, ScheduleRotationDto } from "../types/schedule.types";

const HOUR = 3_600_000;
const DAY = 24 * HOUR;
const NOW = new Date("2026-07-29T09:00:00Z");

function shift(user: string, startIso: string, endIso: string): ScheduleRotationDto {
  return {
    id: `${user}-${startIso}`,
    scheduleId: "s1",
    userId: user,
    userName: user,
    isPrimary: true,
    order: 0,
    shiftLengthMinutes: 1440,
    startUtc: startIso,
    endUtc: endIso,
  };
}

function override(user: string, startIso: string, endIso: string, isActive = true): OnCallOverrideDto {
  return {
    id: `o-${user}-${startIso}`,
    scheduleId: "s1",
    scheduleName: "Payments",
    overrideUserId: user,
    overrideUserName: user,
    startUtc: startIso,
    endUtc: endIso,
    isActive,
  };
}

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true });
  vi.setSystemTime(NOW);
});

afterEach(() => vi.useRealTimers());

describe("the window the strip draws", () => {
  // The schedule's zone decides the day boundary, not the browser's. Treating the local calendar
  // date as UTC midnight shifts every block by the zone's offset — three hours in Istanbul.
  it("starts at midnight in the given zone, not at UTC midnight", () => {
    const istanbul = zonedMidnight("Europe/Istanbul", 0, new Date("2026-07-29T09:00:00Z"));

    // Istanbul is UTC+3 all year, so its midnight is 21:00 UTC the day before.
    expect(new Date(istanbul).toISOString()).toBe("2026-07-28T21:00:00.000Z");
  });

  it("uses UTC midnight when the zone is UTC", () => {
    const utc = zonedMidnight("UTC", 0, new Date("2026-07-29T09:00:00Z"));

    expect(new Date(utc).toISOString()).toBe("2026-07-29T00:00:00.000Z");
  });

  // A DST day is 23 or 25 hours. Walking the window by a fixed 24h drifts off the calendar, so
  // the day ticks stop naming the day they sit under.
  it("walks calendar days, so a spring-forward day is 23 hours", () => {
    // The clocks go forward at 2am on the Sunday, so the short day is Sunday to Monday.
    const base = new Date("2026-03-07T12:00:00Z");
    const sunday = zonedMidnight("America/New_York", 1, base);
    const monday = zonedMidnight("America/New_York", 2, base);

    expect(monday - sunday).toBe(23 * HOUR);
  });

  it("walks calendar days, so a fall-back day is 25 hours", () => {
    const base = new Date("2026-10-31T12:00:00Z");
    const sunday = zonedMidnight("America/New_York", 1, base);
    const monday = zonedMidnight("America/New_York", 2, base);

    expect(monday - sunday).toBe(25 * HOUR);
  });
});

describe("gaps in cover", () => {
  const from = Date.parse("2026-07-29T00:00:00Z");
  const to = from + 3 * DAY;

  it("finds the hole between two shifts", () => {
    const bands = toBands(
      [
        shift("ada", "2026-07-29T00:00:00Z", "2026-07-29T12:00:00Z"),
        shift("mert", "2026-07-29T18:00:00Z", "2026-08-01T00:00:00Z"),
      ],
      [],
    );

    const gaps = findGaps(bands, from, to);

    expect(gaps).toHaveLength(1);
    expect(gaps[0].end - gaps[0].start).toBe(6 * HOUR);
  });

  // The whole reason overrides are drawn on the same strip: one can be the thing that closes a
  // hole the rotations leave, and a gap report that ignored them would cry wolf.
  it("does not report a hole an override covers", () => {
    const bands = toBands(
      [
        shift("ada", "2026-07-29T00:00:00Z", "2026-07-29T12:00:00Z"),
        shift("mert", "2026-07-29T18:00:00Z", "2026-08-01T00:00:00Z"),
      ],
      [override("efe", "2026-07-29T12:00:00Z", "2026-07-29T18:00:00Z")],
    );

    expect(findGaps(bands, from, to)).toHaveLength(0);
  });

  it("reports the tail of the window when cover runs out", () => {
    const bands = toBands([shift("ada", "2026-07-29T00:00:00Z", "2026-07-30T00:00:00Z")], []);

    const gaps = findGaps(bands, from, to);

    expect(gaps).toHaveLength(1);
    expect(gaps[0].end).toBe(to);
  });

  it("treats overlapping shifts as covered rather than as a gap", () => {
    const bands = toBands(
      [
        shift("ada", "2026-07-29T00:00:00Z", "2026-07-30T06:00:00Z"),
        shift("mert", "2026-07-30T00:00:00Z", "2026-08-01T00:00:00Z"),
      ],
      [],
    );

    expect(findGaps(bands, from, to)).toHaveLength(0);
  });

  // Two shifts that meet exactly leave a zero-length hole; rounding can make it a few seconds.
  it("ignores a sub-minute seam between adjacent shifts", () => {
    const bands = toBands(
      [
        shift("ada", "2026-07-29T00:00:00Z", "2026-07-29T11:59:59Z"),
        shift("mert", "2026-07-29T12:00:00Z", "2026-08-01T00:00:00Z"),
      ],
      [],
    );

    expect(findGaps(bands, from, to)).toHaveLength(0);
  });
});

describe("what the strip carries", () => {
  it("does not count a cancelled override as cover", () => {
    const bands = toBands([], [override("efe", "2026-07-29T00:00:00Z", "2026-07-30T00:00:00Z", false)]);

    expect(bands).toHaveLength(0);
  });

  it("names the gap in words, not only in colour", () => {
    render(
      <OnCallStrip
        occurrences={[shift("ada", "2026-07-29T00:00:00Z", "2026-07-29T06:00:00Z")]}
        overrides={[]}
        days={2}
        timeZone="UTC"
        dateLocale="en-GB"
      />,
    );

    expect(screen.getByText(/gap\(s\) in cover/i)).toBeInTheDocument();
  });

  it("says nothing about gaps when cover is complete", () => {
    render(
      <OnCallStrip
        occurrences={[shift("ada", "2026-07-29T00:00:00Z", "2026-08-05T00:00:00Z")]}
        overrides={[]}
        days={2}
        timeZone="UTC"
        dateLocale="en-GB"
      />,
    );

    expect(screen.queryByText(/gap\(s\) in cover/i)).not.toBeInTheDocument();
  });

  // The schedule screen previews a whole day and is fed shifts that have already ended, so its
  // window has to keep opening at midnight even though the dashboard's does not.
  it("opens at midnight in the schedule's zone when no window start is given", () => {
    render(
      <OnCallStrip
        occurrences={[shift("ada", "2026-07-29T06:00:00Z", "2026-08-05T00:00:00Z")]}
        overrides={[]}
        days={2}
        timeZone="UTC"
        dateLocale="en-GB"
      />,
    );

    expect(screen.getByText(/1 gap\(s\) in cover, 6h in total/i)).toBeInTheDocument();
  });

  it("opens at the given instant instead, so nothing before it counts as a hole", () => {
    render(
      <OnCallStrip
        occurrences={[shift("ada", "2026-07-29T06:00:00Z", "2026-08-05T00:00:00Z")]}
        overrides={[]}
        days={2}
        timeZone="UTC"
        dateLocale="en-GB"
        windowStart={Date.parse("2026-07-29T09:00:00Z")}
      />,
    );

    expect(screen.queryByText(/gap\(s\) in cover/i)).not.toBeInTheDocument();
  });

  /// The shift covering the window's start began before it, and must still be drawn.
  // Its width was already clipped to the window; its left edge was not, so the block covering
  // "now" — the whole point of the card — hung off the track and was cut away.
  it("draws the shift already running when the window opens, flush to the left edge", () => {
    const { container } = render(
      <OnCallStrip
        occurrences={[shift("ada", "2026-07-29T00:00:00Z", "2026-07-30T00:00:00Z")]}
        overrides={[]}
        days={2}
        timeZone="UTC"
        dateLocale="en-GB"
        windowStart={Date.parse("2026-07-29T09:00:00Z")}
      />,
    );

    const block = container.querySelector<HTMLElement>('[title*="ada"]');
    expect(block).not.toBeNull();
    expect(block!.style.left).toBe("0%");
  });

  it("lists each person once, however many shifts they hold", () => {
    render(
      <OnCallStrip
        occurrences={[
          shift("ada", "2026-07-29T00:00:00Z", "2026-07-30T00:00:00Z"),
          shift("ada", "2026-07-30T00:00:00Z", "2026-07-31T00:00:00Z"),
        ]}
        overrides={[]}
        days={2}
        timeZone="UTC"
        dateLocale="en-GB"
      />,
    );

    expect(screen.getAllByText("ada")).toHaveLength(3);
  });
});
