/** Floor the runner puts between two consecutive pages of one incident. */
// The first step is exempt: the floor exists to space one page from the previous one, and there
// is no previous page. A 0/1/1 policy therefore fires at 0, 2 and 4 minutes, not 0, 1 and 2.
export const MIN_DELAY_MINUTES_BETWEEN_STEPS = 2;

/** Smallest share of the track any gap is drawn at. */
// Strict proportionality lets one four-hour step flatten every shorter wait to a hairline, so a
// gap is never drawn below a sixth of the longest. Below a 6:1 ratio the spacing is exact.
export const MIN_GAP_SHARE = 1 / 6;

export interface PolicyStepTiming {
  id: string;
  level: number;
  /** The delay as configured on the step. */
  configuredDelayMinutes: number;
  /** The wait the runner actually applies before this step. */
  effectiveDelayMinutes: number;
  /** Whether the floor raised the configured delay. */
  floored: boolean;
  /** Minutes between escalation starting and this step paging. */
  offsetMinutes: number;
  /** 0..1 share of the drawing track for the wait leading into this step. */
  gapShare: number;
}

export interface PolicyTiming {
  steps: PolicyStepTiming[];
  /** Offset of the last page — after it the policy has nobody left. Zero when there are no steps. */
  totalMinutes: number;
  hasFlooredStep: boolean;
}

/** A delay a number input can hand over, reduced to whole non-negative minutes. */
function wholeMinutes(value: number): number {
  return Number.isFinite(value) ? Math.max(0, Math.trunc(value)) : 0;
}

export function buildPolicyTiming(
  steps: readonly { id: string; level: number; delayMinutes: number }[],
): PolicyTiming {
  let offset = 0;

  const timed: PolicyStepTiming[] = steps.map((step, index) => {
    const configured = wholeMinutes(step.delayMinutes);
    const effective =
      index === 0 ? configured : Math.max(configured, MIN_DELAY_MINUTES_BETWEEN_STEPS);
    offset += effective;

    return {
      id: step.id,
      level: step.level,
      configuredDelayMinutes: configured,
      effectiveDelayMinutes: effective,
      floored: effective > configured,
      offsetMinutes: offset,
      gapShare: 0,
    };
  });

  const longest = timed.reduce((max, s) => Math.max(max, s.effectiveDelayMinutes), 0);
  for (const step of timed) {
    step.gapShare =
      longest === 0 || step.effectiveDelayMinutes === 0
        ? 0
        : Math.max(step.effectiveDelayMinutes / longest, MIN_GAP_SHARE);
  }

  return {
    steps: timed,
    totalMinutes: timed.length === 0 ? 0 : timed[timed.length - 1].offsetMinutes,
    hasFlooredStep: timed.some((s) => s.floored),
  };
}

/** Whole hours and the remaining minutes of a duration given in minutes. */
export function splitMinutes(total: number): { hours: number; minutes: number } {
  const safe = wholeMinutes(total);
  return { hours: Math.floor(safe / 60), minutes: safe % 60 };
}
