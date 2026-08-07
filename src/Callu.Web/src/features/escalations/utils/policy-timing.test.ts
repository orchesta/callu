import { describe, it, expect } from "vitest";

/** An admin reads these numbers as a promise about when a phone rings, so the floor, the running
 * total and the clamp are pinned here rather than inferred from the drawing. */

import {
  MIN_DELAY_MINUTES_BETWEEN_STEPS,
  MIN_GAP_SHARE,
  buildPolicyTiming,
  splitMinutes,
} from "./policy-timing";

function policy(...delays: number[]) {
  return delays.map((delayMinutes, index) => ({
    id: `step-${index + 1}`,
    level: index + 1,
    delayMinutes,
  }));
}

function offsets(...delays: number[]) {
  return buildPolicyTiming(policy(...delays)).steps.map((s) => s.offsetMinutes);
}

describe("buildPolicyTiming — the floor", () => {
  it("takes the first step's delay as written, however short", () => {
    expect(offsets(0)).toEqual([0]);
    expect(offsets(1)).toEqual([1]);
    expect(buildPolicyTiming(policy(1)).steps[0].floored).toBe(false);
  });

  it("raises a later step below the floor, and says which one it raised", () => {
    const timing = buildPolicyTiming(policy(0, 1, 1));

    expect(timing.steps.map((s) => s.floored)).toEqual([false, true, true]);
    expect(timing.steps[1].configuredDelayMinutes).toBe(1);
    expect(timing.steps[1].effectiveDelayMinutes).toBe(MIN_DELAY_MINUTES_BETWEEN_STEPS);
    expect(timing.hasFlooredStep).toBe(true);
  });

  it("fires a 0/1/1 policy at 0, 2 and 4 — not at 0, 1 and 2", () => {
    expect(offsets(0, 1, 1)).toEqual([0, 2, 4]);
  });

  it("leaves a delay at or above the floor alone", () => {
    const timing = buildPolicyTiming(policy(0, 2, 30));

    expect(timing.steps.map((s) => s.floored)).toEqual([false, false, false]);
    expect(timing.hasFlooredStep).toBe(false);
    expect(timing.steps.map((s) => s.effectiveDelayMinutes)).toEqual([0, 2, 30]);
  });

  it("floors a later step written as 0, where the first step written as 0 stays immediate", () => {
    const timing = buildPolicyTiming(policy(0, 0));

    expect(offsets(0, 0)).toEqual([0, MIN_DELAY_MINUTES_BETWEEN_STEPS]);
    expect(timing.steps.map((s) => s.floored)).toEqual([false, true]);
  });
});

describe("buildPolicyTiming — cumulative offsets and the total", () => {
  it("puts each step at the sum of every wait before it", () => {
    expect(offsets(0, 5, 15)).toEqual([0, 5, 20]);
  });

  it("reports the last page as the moment the policy runs out", () => {
    expect(buildPolicyTiming(policy(0, 5, 15)).totalMinutes).toBe(20);
    expect(buildPolicyTiming(policy(0, 1, 1)).totalMinutes).toBe(4);
  });

  it("counts a first step that is not immediate into every later offset", () => {
    expect(offsets(3, 5)).toEqual([3, 8]);
    expect(buildPolicyTiming(policy(3, 5)).totalMinutes).toBe(8);
  });
});

describe("buildPolicyTiming — edge cases", () => {
  it("survives a policy with no steps", () => {
    const timing = buildPolicyTiming([]);

    expect(timing.steps).toEqual([]);
    expect(timing.totalMinutes).toBe(0);
    expect(timing.hasFlooredStep).toBe(false);
  });

  it("survives a single immediate step", () => {
    const timing = buildPolicyTiming(policy(0));

    expect(timing.totalMinutes).toBe(0);
    expect(timing.steps[0].gapShare).toBe(0);
    expect(timing.hasFlooredStep).toBe(false);
  });

  it("reduces a negative or fractional delay to whole non-negative minutes", () => {
    const timing = buildPolicyTiming(policy(0, -5, 7.9));

    expect(timing.steps[1].configuredDelayMinutes).toBe(0);
    expect(timing.steps[1].effectiveDelayMinutes).toBe(MIN_DELAY_MINUTES_BETWEEN_STEPS);
    expect(timing.steps[2].configuredDelayMinutes).toBe(7);
    expect(timing.totalMinutes).toBe(9);
  });

  it("carries a delay of several hours without losing the shorter waits", () => {
    const timing = buildPolicyTiming(policy(0, 5, 600));

    expect(timing.totalMinutes).toBe(605);
    expect(splitMinutes(timing.totalMinutes)).toEqual({ hours: 10, minutes: 5 });
  });
});

describe("buildPolicyTiming — the drawing scale", () => {
  it("makes the gap proportional to the wait while the waits are comparable", () => {
    const shares = buildPolicyTiming(policy(0, 5, 10)).steps.map((s) => s.gapShare);

    expect(shares[0]).toBe(0);
    expect(shares[1]).toBeCloseTo(0.5);
    expect(shares[2]).toBe(1);
  });

  it("clamps a short wait next to a very long one instead of drawing it as nothing", () => {
    const shares = buildPolicyTiming(policy(0, 2, 240)).steps.map((s) => s.gapShare);

    // 2/240 would be a hairline; the clamp holds it at a sixth of the longest gap.
    expect(shares[1]).toBe(MIN_GAP_SHARE);
    expect(shares[2]).toBe(1);
  });

  it("keeps every share inside the track", () => {
    const shares = buildPolicyTiming(policy(0, 1, 45, 600)).steps.map((s) => s.gapShare);

    for (const share of shares) {
      expect(share).toBeLessThanOrEqual(1);
      expect(share === 0 || share >= MIN_GAP_SHARE).toBe(true);
    }
  });

  it("gives a step with no wait no gap at all", () => {
    expect(buildPolicyTiming(policy(0, 5)).steps[0].gapShare).toBe(0);
  });
});

describe("splitMinutes", () => {
  it("splits a duration into whole hours and the rest", () => {
    expect(splitMinutes(0)).toEqual({ hours: 0, minutes: 0 });
    expect(splitMinutes(59)).toEqual({ hours: 0, minutes: 59 });
    expect(splitMinutes(60)).toEqual({ hours: 1, minutes: 0 });
    expect(splitMinutes(150)).toEqual({ hours: 2, minutes: 30 });
  });
});
