import { describe, it, expect } from "vitest";
import { deriveFormFromSchedule } from "./schedule-form";
import type { ScheduleDetailDto, ScheduleRotationDto } from "../types/schedule.types";

/** The dirty gate compares live fields against what this derives, so a change here changes what
 * counts as an edit — and an edit is what rewrites live handover times. */

function rotation(over: Partial<ScheduleRotationDto> = {}): ScheduleRotationDto {
  return {
    id: "r1",
    scheduleId: "sch-1",
    userId: "u1",
    isPrimary: true,
    order: 0,
    shiftLengthMinutes: 1440,
    ...over,
  };
}

function schedule(rotations: ScheduleRotationDto[], over: Partial<ScheduleDetailDto> = {}): ScheduleDetailDto {
  return {
    id: "sch-1",
    name: "Primary on-call",
    teamId: "team-1",
    timezone: "Europe/Istanbul",
    rotationCount: rotations.length,
    createdAt: "2026-01-01T00:00:00Z",
    rotations,
    overrides: [],
    ...over,
  };
}

describe("deriveFormFromSchedule — plain fields", () => {
  it("carries name, description, team and timezone across", () => {
    const form = deriveFormFromSchedule(
      schedule([], { name: "Weekend", description: "cover", teamId: "t9", timezone: "UTC" })
    );
    expect(form).toMatchObject({
      scheduleName: "Weekend",
      description: "cover",
      teamId: "t9",
      scheduleTimezone: "UTC",
    });
  });

  it("defaults a missing description to empty and a missing timezone to UTC", () => {
    const form = deriveFormFromSchedule(
      schedule([], { description: undefined, timezone: undefined as unknown as string })
    );
    expect(form.description).toBe("");
    expect(form.scheduleTimezone).toBe("UTC");
  });
});

describe("deriveFormFromSchedule — members", () => {
  it("orders members by rotation order, not by array position", () => {
    const form = deriveFormFromSchedule(
      schedule([
        rotation({ id: "r2", userId: "bob", order: 1 }),
        rotation({ id: "r1", userId: "alice", order: 0 }),
      ])
    );
    expect(form.memberIds).toEqual(["alice", "bob"]);
  });

  it("de-duplicates a member holding several rotations, keeping first appearance", () => {
    const form = deriveFormFromSchedule(
      schedule([
        rotation({ id: "r1", userId: "alice", order: 0 }),
        rotation({ id: "r2", userId: "bob", order: 1 }),
        rotation({ id: "r3", userId: "alice", order: 2 }),
      ])
    );
    expect(form.memberIds).toEqual(["alice", "bob"]);
  });

  it("does not mutate the schedule's rotation array while sorting", () => {
    const rotations = [rotation({ id: "r2", userId: "bob", order: 1 }), rotation({ id: "r1", userId: "alice", order: 0 })];
    deriveFormFromSchedule(schedule(rotations));
    expect(rotations.map((r) => r.id)).toEqual(["r2", "r1"]);
  });

  it("falls back to the 24/7 weekly default for a schedule with no rotations", () => {
    const form = deriveFormFromSchedule(schedule([]));
    expect(form).toMatchObject({
      memberIds: [],
      rotationType: "weekly",
      rotationInterval: "7",
      shiftStart: "00:00",
      shiftEnd: "23:59",
    });
  });
});

describe("deriveFormFromSchedule — shift window", () => {
  it("leaves a full-day block as 24/7 rather than deriving a window", () => {
    const form = deriveFormFromSchedule(
      schedule([rotation({ shiftLengthMinutes: 1440, handoverStartLocal: "2026-03-02T09:00:00" })])
    );
    expect(form.shiftStart).toBe("00:00");
    expect(form.shiftEnd).toBe("23:59");
  });

  it("treats a multi-day block as 24/7 too", () => {
    const form = deriveFormFromSchedule(schedule([rotation({ shiftLengthMinutes: 1440 * 7 })]));
    expect(form.shiftStart).toBe("00:00");
    expect(form.shiftEnd).toBe("23:59");
  });

  it("recovers a sub-24h window from the handover clock time and the block length", () => {
    const form = deriveFormFromSchedule(
      schedule([rotation({ shiftLengthMinutes: 480, handoverStartLocal: "2026-03-02T09:00:00" })])
    );
    expect(form.shiftStart).toBe("09:00");
    expect(form.shiftEnd).toBe("17:00");
  });

  it("wraps a window that runs past midnight back into clock time", () => {
    const form = deriveFormFromSchedule(
      schedule([rotation({ shiftLengthMinutes: 480, handoverStartLocal: "2026-03-02T22:00:00" })])
    );
    expect(form.shiftStart).toBe("22:00");
    expect(form.shiftEnd).toBe("06:00");
  });

  it("zero-pads single-digit clock times", () => {
    const form = deriveFormFromSchedule(
      schedule([rotation({ shiftLengthMinutes: 65, handoverStartLocal: "2026-03-02T07:05:00" })])
    );
    expect(form.shiftStart).toBe("07:05");
    expect(form.shiftEnd).toBe("08:10");
  });
});

describe("deriveFormFromSchedule — cadence", () => {
  it("divides recurrenceIntervalDays by the member count to get days per member", () => {
    const form = deriveFormFromSchedule(
      schedule([
        rotation({ id: "r1", userId: "alice", order: 0, recurrenceIntervalDays: 14 }),
        rotation({ id: "r2", userId: "bob", order: 1, recurrenceIntervalDays: 14 }),
      ])
    );
    expect(form).toMatchObject({ rotationType: "weekly", rotationInterval: "7" });
  });

  it("reads a 2-member 2-day cycle back as daily", () => {
    const form = deriveFormFromSchedule(
      schedule([
        rotation({ id: "r1", userId: "alice", order: 0, recurrenceIntervalDays: 2 }),
        rotation({ id: "r2", userId: "bob", order: 1, recurrenceIntervalDays: 2 }),
      ])
    );
    expect(form).toMatchObject({ rotationType: "daily", rotationInterval: "1" });
  });

  it("falls back to custom for a cadence that is neither daily nor weekly", () => {
    const form = deriveFormFromSchedule(schedule([rotation({ recurrenceIntervalDays: 3 })]));
    expect(form).toMatchObject({ rotationType: "custom", rotationInterval: "3" });
  });

  it("never derives a zero-day cadence, however short the cycle", () => {
    const form = deriveFormFromSchedule(
      schedule([
        rotation({ id: "r1", userId: "alice", order: 0, recurrenceIntervalDays: 1 }),
        rotation({ id: "r2", userId: "bob", order: 1, recurrenceIntervalDays: 1 }),
        rotation({ id: "r3", userId: "cara", order: 2, recurrenceIntervalDays: 1 }),
      ])
    );
    expect(form).toMatchObject({ rotationType: "daily", rotationInterval: "1" });
  });

  it("prefers recurrenceIntervalDays over recurrenceType when both are set", () => {
    const form = deriveFormFromSchedule(
      schedule([rotation({ recurrenceIntervalDays: 1, recurrenceType: "Weekly" })])
    );
    expect(form).toMatchObject({ rotationType: "daily", rotationInterval: "1" });
  });
});

describe("deriveFormFromSchedule — recurrenceType fallback", () => {
  it.each([
    ["Daily", "daily", "1"],
    ["Weekly", "weekly", "7"],
    ["None", "weekly", "7"],
    ["Biweekly", "custom", "14"],
    ["Monthly", "custom", "30"],
  ])("maps %s to %s/%s when no interval is stored", (recType, expectedType, expectedInterval) => {
    const form = deriveFormFromSchedule(
      schedule([rotation({ recurrenceType: recType as ScheduleRotationDto["recurrenceType"] })])
    );
    expect(form.rotationType).toBe(expectedType);
    expect(form.rotationInterval).toBe(expectedInterval);
  });

  it("treats a rotation with neither field as weekly", () => {
    const form = deriveFormFromSchedule(schedule([rotation()]));
    expect(form).toMatchObject({ rotationType: "weekly", rotationInterval: "7" });
  });
});
