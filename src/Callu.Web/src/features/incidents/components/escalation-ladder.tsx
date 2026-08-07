import { Link } from "react-router";
import { ExternalLink } from "lucide-react";
import { t } from "@/shared/locales/i18n";
import { Badge } from "@/shared/components/ui/badge";
import { Button } from "@/shared/components/ui/button";
import { Card } from "@/shared/components/ui/card";
import { useNow } from "@/shared/hooks/use-now";
import { useReducedMotion } from "@/shared/hooks/use-reduced-motion";
import { useIncidentEscalation } from "../hooks/use-incidents";
import type {
  IncidentEscalation,
  IncidentEscalationRunState,
  IncidentEscalationStep,
} from "../types/incident.types";
import { dateLocale } from "@/shared/utils/datetime";

// The server sweep advances a step about every ten seconds, so a per-second countdown would be
// showing precision the schedule behind it does not have.
const TICK_MS = 10_000;

const RUN_STATE_TONE: Record<IncidentEscalationRunState, string> = {
  NotConfigured: "bg-muted text-muted-foreground",
  Waiting: "bg-muted text-muted-foreground",
  Running: "bg-brand-500/10 text-brand-400 border-brand-500/20",
  Stopped: "bg-success-500/10 text-success-400 border-success-500/20",
  Exhausted: "bg-error-500/10 text-error-400 border-error-500/20",
};

/** How long until the next page, in the words an operator needs at 3am. */
// Whole minutes above a minute, then "under a minute": anything finer would jitter without telling
// anyone anything they can act on.
function untilLabel(dueAt: string, now: number): string {
  const remainingMs = new Date(dueAt).getTime() - now;
  if (Number.isNaN(remainingMs)) return "";
  if (remainingMs <= 0) return t("escalationLadder.dueNow");

  const minutes = Math.floor(remainingMs / 60_000);
  return minutes < 1
    ? t("escalationLadder.dueUnderAMinute")
    : t("escalationLadder.dueInMinutes", { count: String(minutes) });
}

/** Fraction of the wait already elapsed, clamped to the bar. */
function elapsedFraction(from: string | null | undefined, dueAt: string, now: number): number {
  if (!from) return 0;
  const start = new Date(from).getTime();
  const end = new Date(dueAt).getTime();
  if (Number.isNaN(start) || Number.isNaN(end) || end <= start) return 1;
  return Math.min(1, Math.max(0, (now - start) / (end - start)));
}

function targetLabel(step: IncidentEscalationStep): string {
  if (step.notifyUserNames.length > 0) return step.notifyUserNames.join(", ");
  if (step.scheduleName) {
    return step.notifyBothOnCall
      ? t("escalationLadder.targetScheduleBoth", { name: step.scheduleName })
      : t("escalationLadder.targetSchedule", { name: step.scheduleName });
  }
  if (step.teamName) {
    return step.notifyAllTeamMembers
      ? t("escalationLadder.targetTeamAll", { name: step.teamName })
      : t("escalationLadder.targetTeamOnCall", { name: step.teamName });
  }
  return t("escalationLadder.targetNobody");
}

function timeOfDay(iso: string): string {
  return new Date(iso).toLocaleTimeString(dateLocale(), { hour: "2-digit", minute: "2-digit" });
}

function StepMarker({ step, animate }: { step: IncidentEscalationStep; animate: boolean }) {
  if (step.state === "Passed") {
    return <span className="mt-1.5 h-2.5 w-2.5 shrink-0 rounded-full bg-brand-500/60" />;
  }
  if (step.state === "Current") {
    return (
      <span className="relative mt-1 flex h-3.5 w-3.5 shrink-0 items-center justify-center">
        {animate && (
          <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-brand-500/50" />
        )}
        <span className="relative inline-flex h-3 w-3 rounded-full bg-brand-500" />
      </span>
    );
  }
  return <span className="mt-1.5 h-2.5 w-2.5 shrink-0 rounded-full border border-border bg-transparent" />;
}

function Rung({
  step,
  isLast,
  animate,
  countdown,
}: {
  step: IncidentEscalationStep;
  isLast: boolean;
  animate: boolean;
  countdown?: { label: string; fraction: number };
}) {
  return (
    <li className="relative flex gap-3 pb-4 last:pb-0">
      {!isLast && <span aria-hidden className="absolute left-[5px] top-4 h-full w-px bg-border" />}
      <StepMarker step={step} animate={animate} />

      <div className="min-w-0 flex-1">
        <div className="flex items-baseline gap-2">
          <span
            className={`text-sm ${step.state === "Current" ? "font-semibold" : "font-medium"} ${
              step.state === "Pending" ? "text-muted-foreground" : ""
            }`}
          >
            {step.level}. {step.title}
          </span>
          {step.pagedAt && (
            <span className="shrink-0 text-xs tabular-nums text-muted-foreground">
              {timeOfDay(step.pagedAt)}
            </span>
          )}
        </div>

        <p className="mt-0.5 truncate text-xs text-muted-foreground" title={targetLabel(step)}>
          {targetLabel(step)}
        </p>

        {countdown && (
          <div className="mt-1.5">
            <p className="text-xs text-brand-400">{countdown.label}</p>
            <div
              className="mt-1 h-1 w-full overflow-hidden rounded-full bg-muted"
              role="progressbar"
              aria-valuemin={0}
              aria-valuemax={100}
              aria-valuenow={Math.round(countdown.fraction * 100)}
              aria-label={countdown.label}
            >
              <div
                className="h-full rounded-full bg-brand-500"
                style={{ width: `${Math.round(countdown.fraction * 100)}%` }}
              />
            </div>
          </div>
        )}
      </div>
    </li>
  );
}

export function EscalationLadder({ escalation }: { escalation: IncidentEscalation }) {
  const reduced = useReducedMotion();
  const running = escalation.runState === "Running";
  const now = useNow(TICK_MS, running && !!escalation.nextStepDueAt);

  // A repeating policy only stops once it has run its last pass, so "last step" is not "last page".
  const repeats =
    escalation.exhaustionBehavior === "Repeat" &&
    escalation.cyclesCompleted + 1 < escalation.maxRepeatCycles;
  const nextCycle = escalation.cyclesCompleted + 2;

  const currentIndex = escalation.steps.findIndex((s) => s.state === "Current");
  const nextIndex = currentIndex + 1;
  const windowStart = currentIndex >= 0 ? escalation.steps[currentIndex].pagedAt : escalation.startedAt;

  return (
    <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
      <div className="mb-4 flex flex-wrap items-center justify-between gap-2">
        <h3 className="text-lg font-semibold">{t("incidentDetail.escalation")}</h3>
        <Badge className={RUN_STATE_TONE[escalation.runState]}>
          {t(`escalationLadder.state.${escalation.runState}`)}
        </Badge>
      </div>

      {escalation.policyName && (
        <p className="mb-4 truncate text-sm text-muted-foreground" title={escalation.policyName}>
          {escalation.policyName}
        </p>
      )}

      {escalation.steps.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t("escalationLadder.noPolicy")}</p>
      ) : (
        <ol className="mb-1">
          {escalation.steps.map((step, index) => (
            <Rung
              key={step.id}
              step={step}
              isLast={index === escalation.steps.length - 1}
              animate={step.state === "Current" && running && !reduced}
              countdown={
                index === nextIndex && running && escalation.nextStepDueAt
                  ? {
                      label: untilLabel(escalation.nextStepDueAt, now),
                      fraction: elapsedFraction(windowStart, escalation.nextStepDueAt, now),
                    }
                  : undefined
              }
            />
          ))}
        </ol>
      )}

      {/* Whether anyone else is coming depends on the policy, not on the step pointer alone. */}
      {escalation.steps.length > 0 && running && nextIndex >= escalation.steps.length && (
        <p className="mt-2 text-xs text-muted-foreground">
          {repeats
            ? t("escalationLadder.lastStepRepeatsNote", { cycle: nextCycle, max: escalation.maxRepeatCycles })
            : t("escalationLadder.lastStepNote")}
        </p>
      )}

      {escalation.runState === "Exhausted" && (
        <p className={`mt-2 text-xs ${repeats ? "text-muted-foreground" : "text-error-400"}`}>
          {repeats
            ? t("escalationLadder.exhaustedRepeatsNote", { cycle: nextCycle, max: escalation.maxRepeatCycles })
            : t("escalationLadder.exhaustedNote")}
        </p>
      )}

      <Link to="/escalations">
        <Button variant="outline" className="mt-4 w-full bg-input-background">
          {t("incidentDetail.viewEscalationPolicies")}
          <ExternalLink className="ml-2 h-4 w-4" />
        </Button>
      </Link>
    </Card>
  );
}

/** Renders the ladder for an incident, or nothing when there is no escalation to show. */
export function IncidentEscalationCard({ incidentId }: { incidentId: string }) {
  const { data } = useIncidentEscalation(incidentId);

  if (!data) return null;
  return <EscalationLadder escalation={data} />;
}
