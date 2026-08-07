import { describe, it, expect } from "vitest";
import { localDateTimeToUtcInstant, formatForDisplay, zoneAbbreviation, todayInZone } from "./timezone";

describe("localDateTimeToUtcInstant", () => {
  it("converts a wall-clock time in a negative-offset zone during DST (EDT, -04:00)", () => {
    const instant = localDateTimeToUtcInstant("2026-04-20T09:00:00", "America/New_York");
    expect(instant.toISOString()).toBe("2026-04-20T13:00:00.000Z");
  });

  it("converts a wall-clock time in the same zone during standard time (EST, -05:00)", () => {
    const instant = localDateTimeToUtcInstant("2026-01-20T09:00:00", "America/New_York");
    expect(instant.toISOString()).toBe("2026-01-20T14:00:00.000Z");
  });

  it("is an identity for UTC wall-clock input", () => {
    const instant = localDateTimeToUtcInstant("2026-04-20T09:00:00", "UTC");
    expect(instant.toISOString()).toBe("2026-04-20T09:00:00.000Z");
  });

  it("converts a wall-clock time in a positive-offset zone (Europe/Istanbul, +03:00)", () => {
    const instant = localDateTimeToUtcInstant("2026-04-20T09:00:00", "Europe/Istanbul");
    expect(instant.toISOString()).toBe("2026-04-20T06:00:00.000Z");
  });

  it("round-trips: the produced instant renders back to the original wall clock in the zone", () => {
    const instant = localDateTimeToUtcInstant("2026-07-01T22:30:00", "America/New_York");
    const shown = formatForDisplay(instant, "America/New_York");
    expect(shown).toContain("10:30");
    expect(shown).toContain("Jul 1, 2026");
  });

  it("uses the post-transition offset on a spring-forward day (03:30 is already EDT, -04:00)", () => {
    const instant = localDateTimeToUtcInstant("2026-03-08T03:30:00", "America/New_York");
    expect(instant.toISOString()).toBe("2026-03-08T07:30:00.000Z");
  });

  it("resolves a fall-back ambiguous time to the earlier offset (EDT, -04:00)", () => {
    const instant = localDateTimeToUtcInstant("2026-11-01T01:30:00", "America/New_York");
    expect(instant.toISOString()).toBe("2026-11-01T05:30:00.000Z");
  });

  it("handles a spring-forward day in a positive-offset zone (Europe/Istanbul does not shift, +03:00)", () => {
    const instant = localDateTimeToUtcInstant("2026-03-08T03:30:00", "Europe/Istanbul");
    expect(instant.toISOString()).toBe("2026-03-08T00:30:00.000Z");
  });
});

describe("todayInZone", () => {
  it("is still yesterday's UTC date for a zone west of UTC late in the evening", () => {
    // 2026-07-16T01:00Z is 2026-07-15 18:00 in Los Angeles: the operator's "tonight".
    const now = new Date("2026-07-16T01:00:00Z");
    expect(todayInZone("America/Los_Angeles", now)).toBe("2026-07-15");
    expect(todayInZone("UTC", now)).toBe("2026-07-16");
  });

  it("is already tomorrow's UTC date for a zone east of UTC late in the evening", () => {
    const now = new Date("2026-07-15T22:00:00Z");
    expect(todayInZone("Asia/Tokyo", now)).toBe("2026-07-16");
    expect(todayInZone("UTC", now)).toBe("2026-07-15");
  });

  it("zero-pads to the YYYY-MM-DD an <input type=\"date\"> min accepts", () => {
    expect(todayInZone("UTC", new Date("2026-01-05T12:00:00Z"))).toBe("2026-01-05");
  });

  it("falls back to the browser's calendar date when the zone is not recognised", () => {
    const now = new Date(2026, 0, 5, 12, 0, 0);
    expect(todayInZone("Not/AZone", now)).toBe("2026-01-05");
  });
});

describe("zoneAbbreviation", () => {
  it("reflects DST vs standard time for the same zone", () => {
    const summer = zoneAbbreviation("2026-07-01T12:00:00Z", "America/New_York");
    const winter = zoneAbbreviation("2026-01-01T12:00:00Z", "America/New_York");
    expect(summer).toBe("EDT");
    expect(winter).toBe("EST");
  });
});
