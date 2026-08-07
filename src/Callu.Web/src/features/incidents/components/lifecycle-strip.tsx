import { t } from "@/shared/locales/i18n";
import { useReducedMotion } from "@/shared/hooks/use-reduced-motion";
import { getTimeAgo } from "@/shared/utils/time";
import { dateLocale } from "@/shared/utils/datetime";

const STATES = ["Open", "Acknowledged", "Investigating", "Mitigated", "Resolved", "Closed"] as const;

type State = (typeof STATES)[number];

/** When the incident entered a state, for the three the API actually records. */
// Investigating and Mitigated are reachable but have no stored timestamp — the timeline carries
// them only as prose, and parsing a title to draw a clock is the kind of guess that later reads
// as fact. They render without a time rather than with an invented one.
type Stamps = Partial<Record<State, string | undefined>>;

function Marker({ reached, current, animate }: { reached: boolean; current: boolean; animate: boolean }) {
  if (current) {
    return (
      <span className="relative flex h-7 w-7 shrink-0 items-center justify-center">
        {animate && (
          <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-brand-500/40" />
        )}
        <span className="relative h-3.5 w-3.5 rounded-full bg-brand-500 ring-4 ring-brand-500/25" />
      </span>
    );
  }
  return (
    <span className="flex h-7 w-7 shrink-0 items-center justify-center">
      <span
        className={`h-2.5 w-2.5 rounded-full ${reached ? "bg-brand-500/60" : "border border-border"}`}
      />
    </span>
  );
}

export function LifecycleStrip({
  status,
  startedAt,
  acknowledgedAt,
  resolvedAt,
}: {
  status: string;
  startedAt?: string;
  acknowledgedAt?: string;
  resolvedAt?: string;
}) {
  const reduced = useReducedMotion();

  const currentIndex = STATES.indexOf(status as State);
  if (currentIndex < 0) return null;

  const stamps: Stamps = {
    Open: startedAt,
    Acknowledged: acknowledgedAt,
    Resolved: resolvedAt,
  };

  // Only when the current state is one whose entry time is recorded. Anywhere else the honest
  // answer is nothing, not the age of the incident dressed up as the age of the state.
  const enteredCurrent = stamps[STATES[currentIndex]];

  return (
    <div className="rounded-xl border border-border bg-card/60 px-4 py-3">
      <ol className="flex items-start justify-between gap-1">
        {STATES.map((state, index) => {
          const reached = index < currentIndex;
          const current = index === currentIndex;
          const stamp = stamps[state];

          return (
            <li key={state} className="relative flex min-w-0 flex-1 flex-col items-center">
              {index > 0 && (
                <span
                  aria-hidden
                  className={`absolute right-1/2 top-3.5 h-px w-full ${
                    index <= currentIndex ? "bg-brand-500/50" : "bg-border"
                  }`}
                />
              )}
              <span className="relative z-10">
                <Marker reached={reached} current={current} animate={current && !reduced} />
              </span>
              <span
                className={`mt-0.5 truncate text-center text-[11px] ${
                  current
                    ? "font-semibold text-foreground"
                    : reached
                      ? "text-muted-foreground"
                      : "text-muted-foreground/50"
                }`}
                title={t(`lifecycle.${state}`)}
              >
                {t(`lifecycle.${state}`)}
              </span>
              <span className="h-4 text-[10px] tabular-nums text-muted-foreground/70">
                {stamp ? new Date(stamp).toLocaleTimeString(dateLocale(), { hour: "2-digit", minute: "2-digit" }) : ""}
              </span>
            </li>
          );
        })}
      </ol>

      {/* The line that makes this more than decoration: how long it has been sitting here. */}
      {enteredCurrent && (
        <p className="mt-1 text-center text-xs text-muted-foreground">
          {t("lifecycle.inStateFor", {
            state: t(`lifecycle.${STATES[currentIndex]}`),
            duration: getTimeAgo(enteredCurrent),
          })}
        </p>
      )}
    </div>
  );
}
