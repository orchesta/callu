import { describe, it, expect } from "vitest";
import rotationTemplateSource from "./rotation-template.ts?raw";
import {
  buildRotationTemplate,
  computeShiftLengthMinutes,
  isRotationBlockSupported,
  resolveCycleAnchor,
  MAX_OWNERSHIP_DAYS,
  MAX_247_BLOCK_DAYS,
  MAX_SHIFT_LENGTH_MINUTES,
} from "./rotation-template";

describe("computeShiftLengthMinutes", () => {
  it("same-day shift returns the direct span", () => {
    expect(
      computeShiftLengthMinutes({ shiftStart: "09:00", shiftEnd: "17:00", is247: false, daysPerMember: 1 })
    ).toBe(480);
  });

  it("overnight shift wraps to the next day (regression: previously clamped to 1)", () => {
    expect(
      computeShiftLengthMinutes({ shiftStart: "22:00", shiftEnd: "06:00", is247: false, daysPerMember: 1 })
    ).toBe(480);
  });

  it("overnight shift crossing midnight by a small margin", () => {
    expect(
      computeShiftLengthMinutes({ shiftStart: "23:30", shiftEnd: "00:30", is247: false, daysPerMember: 1 })
    ).toBe(60);
  });

  it("equal start and end defaults to a full 24h rather than a 1-minute shift", () => {
    expect(
      computeShiftLengthMinutes({ shiftStart: "09:00", shiftEnd: "09:00", is247: false, daysPerMember: 1 })
    ).toBe(1440);
  });

  it("24/7 shift spans daysPerMember full days", () => {
    expect(
      computeShiftLengthMinutes({ shiftStart: "00:00", shiftEnd: "00:00", is247: true, daysPerMember: 1 })
    ).toBe(1440);
    expect(
      computeShiftLengthMinutes({ shiftStart: "00:00", shiftEnd: "00:00", is247: true, daysPerMember: 3 })
    ).toBe(4320);
  });
});

describe("resolveCycleAnchor", () => {
  it("picks the earliest handover and keeps its clock time", () => {
    const anchor = resolveCycleAnchor([
      "2026-07-20T09:00:00",
      "2026-07-06T09:00:00",
      "2026-07-13T09:00:00",
    ]);
    expect(anchor.getFullYear()).toBe(2026);
    expect(anchor.getMonth()).toBe(6);
    expect(anchor.getDate()).toBe(6);
    expect(anchor.getHours()).toBe(9);
    expect(anchor.getMinutes()).toBe(0);
  });

  it("does not drag a non-midnight 24/7 handover to midnight", () => {
    // Regression: a 24/7 rotation handing over at 06:30 kept losing its time-of-day.
    const anchor = resolveCycleAnchor(["2026-07-06T06:30:00"]);
    expect(anchor.getHours()).toBe(6);
    expect(anchor.getMinutes()).toBe(30);
  });

  it("falls back to the schedule creation date when no rotation has a handover", () => {
    const anchor = resolveCycleAnchor([undefined, null], "2026-03-02T22:15:00");
    expect(anchor.getMonth()).toBe(2);
    expect(anchor.getDate()).toBe(2);
    expect(anchor.getHours()).toBe(0);
  });

  it("falls back to today when nothing is stored", () => {
    const today = new Date();
    today.setHours(0, 0, 0, 0);
    expect(resolveCycleAnchor([]).getTime()).toBe(today.getTime());
  });
});

describe("isRotationBlockSupported", () => {
  it("accepts 24/7 blocks up to the API's shiftLengthMinutes cap", () => {
    expect(isRotationBlockSupported(1, true)).toBe(true);
    expect(isRotationBlockSupported(MAX_247_BLOCK_DAYS, true)).toBe(true);
  });

  it("rejects 24/7 blocks the API's 30-day shiftLengthMinutes cap would reject", () => {
    // Regression: 45 days of 24/7 coverage means shiftLengthMinutes = 64800 > 43200 → 400.
    expect(isRotationBlockSupported(MAX_247_BLOCK_DAYS + 1, true)).toBe(false);
    expect(isRotationBlockSupported(45, true)).toBe(false);
    expect(isRotationBlockSupported(365, true)).toBe(false);
  });

  it("keeps every accepted block inside the API's shiftLengthMinutes cap", () => {
    for (const is247 of [true, false]) {
      for (let days = 1; days <= 60; days++) {
        if (!isRotationBlockSupported(days, is247)) continue;
        const minutes = computeShiftLengthMinutes({
          shiftStart: is247 ? "00:00" : "09:00",
          shiftEnd: is247 ? "23:59" : "17:00",
          is247,
          daysPerMember: days,
        });
        expect(minutes).toBeLessThanOrEqual(MAX_SHIFT_LENGTH_MINUTES);
      }
    }
  });

  it("accepts partial-day blocks up to the API's ownershipDays limit", () => {
    expect(isRotationBlockSupported(1, false)).toBe(true);
    expect(isRotationBlockSupported(MAX_OWNERSHIP_DAYS, false)).toBe(true);
  });

  it("rejects partial-day blocks the API cannot express", () => {
    expect(isRotationBlockSupported(MAX_OWNERSHIP_DAYS + 1, false)).toBe(false);
    expect(isRotationBlockSupported(45, false)).toBe(false);
  });

  it("rejects nonsense block lengths", () => {
    expect(isRotationBlockSupported(0, false)).toBe(false);
    expect(isRotationBlockSupported(Number.NaN, true)).toBe(false);
  });
});

describe("buildRotationTemplate", () => {
  const anchor = new Date(2026, 6, 6); // Mon 2026-07-06, local midnight

  it("phases each member off the shared anchor by its position", () => {
    const params = {
      anchor,
      daysPerMember: 7,
      shiftStart: "09:00",
      shiftEnd: "17:00",
      is247: false,
    };
    expect(buildRotationTemplate({ ...params, memberIndex: 0 }).handoverStartLocal).toBe(
      "2026-07-06T09:00:00"
    );
    expect(buildRotationTemplate({ ...params, memberIndex: 1 }).handoverStartLocal).toBe(
      "2026-07-13T09:00:00"
    );
    expect(buildRotationTemplate({ ...params, memberIndex: 2 }).handoverStartLocal).toBe(
      "2026-07-20T09:00:00"
    );
  });

  it("partial-day shift owns every day of its block, with the daily window as shift length", () => {
    const tpl = buildRotationTemplate({
      anchor,
      memberIndex: 0,
      daysPerMember: 7,
      shiftStart: "09:00",
      shiftEnd: "17:00",
      is247: false,
    });
    expect(tpl.shiftLengthMinutes).toBe(480);
    expect(tpl.ownershipDays).toBe(7);
  });

  /**
   * Asserted across the spring-forward hours so the result cannot depend on the zone the suite
   * runs in: whatever the local rules are, the wall clock written must be the one asked for.
   */
  it("never normalises a handover through a local DST gap", () => {
    const marchAnchor = new Date(2026, 2, 1); // Sun 2026-03-01, local midnight
    const springForward = 7; // 2026-03-08, the US spring-forward day

    for (const hh of ["01:30", "02:00", "02:30", "03:00"]) {
      const tpl = buildRotationTemplate({
        anchor: marchAnchor,
        memberIndex: springForward,
        daysPerMember: 1,
        shiftStart: hh,
        shiftEnd: "23:59",
        is247: false,
      });
      expect(tpl.handoverStartLocal).toBe(`2026-03-08T${hh}:00`);
    }
  });

  /**
   * The behavioural test above cannot fail on a runner whose zone has no DST gap, and Node on
   * Windows ignores TZ, so the shape is guarded directly as well.
   */
  it("does no local-zone arithmetic", () => {
    const start = rotationTemplateSource.indexOf("export function buildRotationTemplate");
    expect(start).toBeGreaterThan(-1);
    // buildRotationTemplate is the last declaration in the file, so this is its body. Slicing
    // to the next "\n}" would stop at the closing brace of the destructured parameter list.
    const body = rotationTemplateSource.slice(start);

    // Scoped to this function: resolveCycleAnchor normalises its anchor to local midnight on
    // purpose, which is a separate decision.
    expect(body).not.toContain("setHours(");
    expect(body).not.toContain("setDate(");
    expect(body).toContain("Date.UTC(");
  });

  it("keeps the calendar honest across a month boundary", () => {
    expect(
      buildRotationTemplate({
        anchor: new Date(2026, 0, 30), // 2026-01-30
        memberIndex: 1,
        daysPerMember: 3,
        shiftStart: "08:00",
        shiftEnd: "16:00",
        is247: false,
      }).handoverStartLocal
    ).toBe("2026-02-02T08:00:00");
  });

  it("24/7 keeps the single-block model: no ownershipDays", () => {
    const tpl = buildRotationTemplate({
      anchor,
      memberIndex: 1,
      daysPerMember: 7,
      shiftStart: "00:00",
      shiftEnd: "23:59",
      is247: true,
    });
    expect(tpl.ownershipDays).toBeUndefined();
    expect(tpl.shiftLengthMinutes).toBe(7 * 1440);
    expect(tpl.handoverStartLocal).toBe("2026-07-13T00:00:00");
  });

  it("24/7 preserves the anchor's clock time instead of forcing midnight", () => {
    // Regression: a live 24/7 rotation handing over at 06:30 was silently moved to 00:00.
    const tpl = buildRotationTemplate({
      anchor: new Date(2026, 6, 6, 6, 30),
      memberIndex: 1,
      daysPerMember: 7,
      shiftStart: "00:00",
      shiftEnd: "23:59",
      is247: true,
    });
    expect(tpl.handoverStartLocal).toBe("2026-07-13T06:30:00");
  });

  it("always reports ownershipDays for partial-day blocks, even past the API's limit", () => {
    // Regression: >31-day blocks used to drop ownershipDays, which silently collapsed the
    // member's coverage to a single short window per cycle. The UI now refuses to save this
    // (isRotationBlockSupported), and the value is sent as-is so the API rejects it loudly
    // rather than materializing something the preview never promised.
    const tpl = buildRotationTemplate({
      anchor,
      memberIndex: 0,
      daysPerMember: 45,
      shiftStart: "09:00",
      shiftEnd: "17:00",
      is247: false,
    });
    expect(tpl.ownershipDays).toBe(45);
    expect(isRotationBlockSupported(45, false)).toBe(false);
  });

  it("is idempotent — the same anchor and index always produce the same handover", () => {
    const params = {
      anchor,
      memberIndex: 2,
      daysPerMember: 1,
      shiftStart: "09:00",
      shiftEnd: "17:00",
      is247: false,
    };
    expect(buildRotationTemplate(params)).toEqual(buildRotationTemplate(params));
  });

  it("re-deriving from its own output does not drift the grid", () => {
    const base = { daysPerMember: 7, shiftStart: "09:00", shiftEnd: "17:00", is247: false };
    const first = [0, 1, 2].map((i) => buildRotationTemplate({ ...base, anchor, memberIndex: i }));
    const reanchored = resolveCycleAnchor(first.map((t) => t.handoverStartLocal));
    const second = [0, 1, 2].map((i) =>
      buildRotationTemplate({ ...base, anchor: reanchored, memberIndex: i })
    );
    expect(second.map((t) => t.handoverStartLocal)).toEqual(
      first.map((t) => t.handoverStartLocal)
    );
  });
});
