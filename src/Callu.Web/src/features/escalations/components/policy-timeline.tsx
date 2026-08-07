import { Badge } from "@/shared/components/ui/badge";
import { Card } from "@/shared/components/ui/card";
import { t } from "@/shared/locales/i18n";
import { buildPolicyTiming, splitMinutes } from "../utils/policy-timing";
import type { LocalStep } from "../utils/step-mapping";

// The longest wait in the policy is drawn at this height and every other gap is a share of it.
const TRACK_PX = 72;
// A step that pages the moment escalation starts still needs a stub to hang off the marker above.
const NO_WAIT_PX = 8;

function gapHeight(share: number): number {
  return share === 0 ? NO_WAIT_PX : Math.round(share * TRACK_PX);
}

function offsetLabel(minutes: number): string {
  const split = splitMinutes(minutes);
  return split.hours > 0
    ? t("escalations.timelineAtHours", {
        hours: String(split.hours),
        minutes: String(split.minutes),
      })
    : t("escalations.timelineAtMinutes", { minutes: String(split.minutes) });
}

export function PolicyTimeline({
  steps,
  describeTarget,
}: {
  steps: LocalStep[];
  /** Resolves a step's target to the name an admin recognises. */
  describeTarget: (step: LocalStep) => string;
}) {
  const timing = buildPolicyTiming(steps);
  // Only a schedule or team target can come up empty; a named user is always somebody to wait for.
  const canRunEarly = steps.some((s) => s.targetType === "schedule" || s.targetType === "team");

  return (
    <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
      <h3 style={{ fontSize: "1.125rem", fontWeight: 600 }}>{t("escalations.timelineTitle")}</h3>
      <p style={{ fontSize: "0.8125rem", color: "#94A3B8", marginTop: "0.25rem" }}>
        {t("escalations.timelineDesc")}
      </p>
      {canRunEarly && (
        <p className="mt-1.5 text-xs text-muted-foreground">
          {t("escalations.timelineEarlyNote")}
        </p>
      )}

      {timing.steps.length === 0 ? (
        <p className="mt-4 text-sm text-muted-foreground">{t("escalations.timelineNoSteps")}</p>
      ) : (
        <>
          <div className="mt-5 flex items-center gap-3">
            <span className="flex w-6 shrink-0 justify-center">
              <span className="h-2.5 w-2.5 rounded-full border border-brand-500/60" />
            </span>
            <span className="text-xs text-muted-foreground">{t("escalations.timelineStart")}</span>
          </div>

          <ol>
            {timing.steps.map((timed, index) => {
              const step = steps[index];
              const title = step.title || t("escalations.level", { level: String(timed.level) });
              const target = describeTarget(step);
              const rise = gapHeight(timed.gapShare);

              return (
                <li key={timed.id} className="flex gap-3">
                  <div className="flex w-6 shrink-0 flex-col items-center">
                    <span aria-hidden className="w-px bg-border" style={{ height: rise }} />
                    <span className="flex h-6 w-6 items-center justify-center rounded-full bg-brand-500 text-[11px] font-bold text-white">
                      {timed.level}
                    </span>
                  </div>

                  <div className="min-w-0 flex-1 pb-1" style={{ paddingTop: rise }}>
                    <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
                      <span className="truncate font-semibold" style={{ fontSize: "0.9375rem" }} title={title}>
                        {title}
                      </span>
                      <Badge className="border border-brand-500/20 bg-brand-500/10 text-brand-400 tabular-nums">
                        {offsetLabel(timed.offsetMinutes)}
                      </Badge>
                    </div>

                    {timed.floored ? (
                      <p className="mt-1 text-xs text-warning-500">
                        {t("escalations.timelineFloored", {
                          configured: String(timed.configuredDelayMinutes),
                          effective: String(timed.effectiveDelayMinutes),
                        })}
                      </p>
                    ) : (
                      timed.effectiveDelayMinutes > 0 && (
                        <p className="mt-1 text-xs text-muted-foreground">
                          {t("escalations.afterMinutes", {
                            minutes: String(timed.effectiveDelayMinutes),
                          })}
                        </p>
                      )
                    )}

                    <p className="mt-1 truncate" style={{ fontSize: "0.875rem", color: "#94A3B8" }} title={target}>
                      {t("escalations.notifyLabel")}{" "}
                      <span className="text-foreground font-medium">{target}</span>
                    </p>

                    {step.targetType === "team" && step.notifyAll && (
                      <p className="mt-0.5 text-xs text-muted-foreground">
                        {t("escalations.allTeamMembers")}
                      </p>
                    )}
                    {step.targetType === "schedule" && step.notifyBothOnCall && (
                      <p className="mt-0.5 text-xs text-muted-foreground">
                        {t("escalations.pageBothOnCall")}
                      </p>
                    )}
                  </div>
                </li>
              );
            })}
          </ol>

          <div className="mt-4 rounded-lg border border-border bg-surface-light/20 p-3">
            <p style={{ fontSize: "0.875rem", fontWeight: 600 }}>
              {t("escalations.timelineRunsOut", { at: offsetLabel(timing.totalMinutes) })}
            </p>
            <p className="mt-0.5 text-xs text-muted-foreground">
              {t("escalations.timelineRunsOutNote")}
            </p>
          </div>
        </>
      )}
    </Card>
  );
}
