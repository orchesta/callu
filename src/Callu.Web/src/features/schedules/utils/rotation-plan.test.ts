import { describe, it, expect } from "vitest";
import { buildSchedulePlanRotations, cadenceForCycle } from "./rotation-plan";
import type { ScheduleRotationDto } from "../types/schedule.types";

const anchor = new Date(2026, 6, 6); // Mon 2026-07-06, local midnight

function rotation(overrides: Partial<ScheduleRotationDto> & { id: string; userId: string }): ScheduleRotationDto {
  return {
    scheduleId: "sched-1",
    isPrimary: false,
    order: 1,
    shiftLengthMinutes: 7 * 1440,
    handoverStartLocal: "2026-07-06T00:00:00",
    recurrenceType: "Biweekly",
    recurrenceIntervalDays: 14,
    ...overrides,
  };
}

// Two members, weekly 24/7: alice hands over 07-06, bob 07-13, 14-day cycle.
const storedTwoMember: ScheduleRotationDto[] = [
  rotation({ id: "rot-a", userId: "alice", isPrimary: true, order: 1, handoverStartLocal: "2026-07-06T00:00:00" }),
  rotation({ id: "rot-b", userId: "bob", isPrimary: false, order: 2, handoverStartLocal: "2026-07-13T00:00:00" }),
];

const baseInput = {
  rotations: storedTwoMember,
  selectedMemberIds: ["alice", "bob"],
  rotationLayoutChanged: true,
  anchor,
  daysPerMember: 7,
  shiftStart: "00:00",
  shiftEnd: "23:59",
  is247: true,
};

describe("cadenceForCycle", () => {
  it("maps a cycle length to the nearest enum cadence", () => {
    expect(cadenceForCycle(1)).toBe("Daily");
    expect(cadenceForCycle(7)).toBe("Weekly");
    expect(cadenceForCycle(14)).toBe("Biweekly");
    expect(cadenceForCycle(21)).toBe("Monthly");
  });
});

describe("buildSchedulePlanRotations", () => {
  it("writes nothing when the rotation layout is untouched", () => {
    // Regression: an operator editing only the description used to have handoverStartLocal,
    // shiftLengthMinutes, ownershipDays and the cadence rewritten from form fields that are
    // derived from rotation #1 alone. Null here means the request omits `rotations`, which the
    // API reads as "leave them exactly as they are".
    expect(buildSchedulePlanRotations({ ...baseInput, rotationLayoutChanged: false })).toBeNull();
  });

  it("leaves every timing field alone on an unrelated edit, even when the stored rotations disagree with the form", () => {
    const inconsistent = [
      storedTwoMember[0],
      rotation({
        id: "rot-b",
        userId: "bob",
        order: 2,
        handoverStartLocal: "2026-07-13T00:00:00",
        shiftLengthMinutes: 480,
        ownershipDays: 3,
        recurrenceType: "Monthly",
        recurrenceIntervalDays: 30,
      }),
    ];
    expect(
      buildSchedulePlanRotations({
        ...baseInput,
        rotations: inconsistent,
        rotationLayoutChanged: false,
      }),
    ).toBeNull();
  });

  it("re-phases the members when their order changes", () => {
    // H05: swapping the order must actually take effect — bob moves to the anchor slot.
    const plan = buildSchedulePlanRotations({ ...baseInput, selectedMemberIds: ["bob", "alice"] });
    expect(plan).not.toBeNull();
    expect(plan).toHaveLength(2);

    const [bob, alice] = plan!;
    expect(bob.id).toBe("rot-b");
    expect(bob.handoverStartLocal).toBe("2026-07-06T00:00:00");
    expect(bob.isPrimary).toBe(true);
    expect(bob.order).toBe(1);
    expect(alice.id).toBe("rot-a");
    expect(alice.handoverStartLocal).toBe("2026-07-13T00:00:00");
    expect(alice.isPrimary).toBe(false);
    expect(alice.order).toBe(2);
  });

  it("keeps a member's stored rotation row, so a re-order is an update and not a delete+create", () => {
    const plan = buildSchedulePlanRotations({ ...baseInput, selectedMemberIds: ["bob", "alice"] })!;
    expect(plan.map((r) => r.id)).toEqual(["rot-b", "rot-a"]);
  });

  it("drops a member by leaving them out of the list, which the API reads as a removal", () => {
    const plan = buildSchedulePlanRotations({ ...baseInput, selectedMemberIds: ["alice"] })!;
    expect(plan).toHaveLength(1);
    expect(plan[0]).toMatchObject({ id: "rot-a", userId: "alice", order: 1, isPrimary: true });
    expect(plan.some((r) => r.userId === "bob")).toBe(false);
  });

  it("adds a new member without an id and re-phases the rest", () => {
    const plan = buildSchedulePlanRotations({
      ...baseInput,
      selectedMemberIds: ["alice", "bob", "carol"],
    })!;

    const carol = plan.find((r) => r.userId === "carol")!;
    expect(carol.id).toBeUndefined();
    expect(carol).toMatchObject({
      handoverStartLocal: "2026-07-20T00:00:00",
      order: 3,
      isPrimary: false,
    });
    // The cycle grew to 21 days, so every member's cadence has to follow.
    expect(plan.every((r) => r.recurrenceIntervalDays === 21)).toBe(true);
    expect(plan.every((r) => r.recurrenceType === "Monthly")).toBe(true);
  });

  it("sends ownershipDays for partial-day shifts and none for 24/7", () => {
    const partial = buildSchedulePlanRotations({
      ...baseInput,
      selectedMemberIds: ["bob", "alice"],
      shiftStart: "09:00",
      shiftEnd: "17:00",
      is247: false,
    })!;
    expect(partial.every((r) => r.ownershipDays === 7)).toBe(true);

    // 24/7 is a single block whose shiftLengthMinutes already spans the whole window, so
    // ownershipDays is omitted — and because the plan replaces rather than patches, that also
    // clears a value left over from a partial-day configuration.
    const around = buildSchedulePlanRotations({ ...baseInput, selectedMemberIds: ["bob", "alice"] })!;
    expect(around.every((r) => r.ownershipDays === undefined)).toBe(true);
    expect(around.every((r) => r.shiftLengthMinutes === 7 * 1440)).toBe(true);
  });
});
