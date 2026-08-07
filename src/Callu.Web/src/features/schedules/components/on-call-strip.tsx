import { t } from "@/shared/locales/i18n";
import { useNow } from "@/shared/hooks/use-now";
import { useReducedMotion } from "@/shared/hooks/use-reduced-motion";
import type { OnCallOverrideDto, ScheduleRotationDto } from "../types/schedule.types";
import { findGaps, toBands, zonedMidnight } from "../utils/on-call-window";

const NOW_TICK_MS = 60_000;

function hours(ms: number): number {
  return Math.round(ms / 3_600_000);
}

export function OnCallStrip({
  occurrences,
  overrides,
  days,
  timeZone,
  dateLocale,
  windowStart,
}: {
  occurrences: ScheduleRotationDto[];
  overrides: OnCallOverrideDto[];
  days: number;
  timeZone: string;
  dateLocale: string;
  /** Where the drawn window opens. Defaults to midnight today in `timeZone`. */
  // A caller that only fetches shifts still to come has to start at that instant, or every shift
  // that ended earlier today reads as a hole in cover.
  windowStart?: number;
}) {
  const reduced = useReducedMotion();
  const now = useNow(NOW_TICK_MS);

  const from = windowStart ?? zonedMidnight(timeZone, 0);
  const to = zonedMidnight(timeZone, days);
  const span = to - from;

  const bands = toBands(occurrences, overrides).filter((b) => b.end > from && b.start < to);
  const gaps = findGaps(bands, from, to);

  if (bands.length === 0 && gaps.length === 0) return null;

  const pct = (value: number) => `${((value - from) / span) * 100}%`;
  const width = (start: number, end: number) =>
    `${((Math.min(end, to) - Math.max(start, from)) / span) * 100}%`;

  // Shifts meet exactly, so without a gutter the blocks render as one continuous bar and a
  // handover is invisible. Taken off the width rather than added as a margin: the blocks are
  // positioned by percentage and a margin would push each one off its own start time.
  const blockWidth = (start: number, end: number) => `calc(${width(start, end)} - 3px)`;

  // One tick per day is unreadable past a fortnight; thin them out rather than overlap the labels.
  const tickEvery = days <= 14 ? 1 : days <= 30 ? 3 : 7;
  const ticks = Array.from({ length: Math.floor(days / tickEvery) + 1 }, (_, i) =>
    zonedMidnight(timeZone, i * tickEvery),
  ).filter((tick) => tick >= from && tick <= to);

  const people = [...new Map(bands.filter((b) => !b.isOverride).map((b) => [b.label, b])).values()];
  const nowInWindow = now >= from && now <= to;

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center gap-x-4 gap-y-1.5 text-xs">
        {people.map((p) => (
          <span key={p.label} className="flex items-center gap-1.5">
            <span className="h-2.5 w-2.5 rounded-sm" style={{ background: p.colour }} />
            <span className="text-muted-foreground">{p.label}</span>
          </span>
        ))}
        {bands.some((b) => b.isOverride) && (
          <span className="flex items-center gap-1.5">
            <span className="h-2.5 w-2.5 rounded-sm border border-white/60 bg-white/20" />
            <span className="text-muted-foreground">{t("onCallStrip.override")}</span>
          </span>
        )}
        {gaps.length > 0 && (
          <span className="flex items-center gap-1.5">
            <span className="h-2.5 w-2.5 rounded-sm border border-error-500/50 bg-error-500/20" />
            <span className="text-error-400">{t("onCallStrip.gap")}</span>
          </span>
        )}
      </div>

      <div className="relative">
        {nowInWindow && (
          <div
            aria-hidden
            className="pointer-events-none absolute -top-1 bottom-6 z-20 w-px bg-success-400"
            style={{ left: pct(now) }}
          >
            <span
              className={`absolute -left-[3px] -top-1 h-1.5 w-1.5 rounded-full bg-success-400 ${
                reduced ? "" : "animate-pulse"
              }`}
            />
          </div>
        )}

        <div className="relative h-11 overflow-hidden rounded-lg border border-border bg-surface-light/10">
          {gaps.map((gap) => (
            <div
              key={`gap-${gap.start}`}
              className="absolute inset-y-0 border-x border-error-500/40 bg-error-500/15"
              style={{ left: pct(gap.start), width: width(gap.start, gap.end) }}
              title={t("onCallStrip.gapOf", { hours: String(hours(gap.end - gap.start)) })}
            />
          ))}

          {bands.map((band) => (
            <div
              key={band.key}
              className={`absolute flex items-center overflow-hidden rounded-md px-2 ${
                band.isOverride ? "inset-y-1 z-10 ring-1 ring-inset ring-white/70" : "inset-y-1.5"
              }`}
              style={{
                left: pct(Math.max(band.start, from)),
                width: blockWidth(band.start, band.end),
                minWidth: "2px",
                background: band.colour,
                opacity: band.isOverride ? 1 : 0.85,
              }}
              title={`${band.label} — ${new Date(band.start).toLocaleString(dateLocale, {
                timeZone,
              })} → ${new Date(band.end).toLocaleString(dateLocale, { timeZone })}`}
            >
              <span className="truncate text-[11px] font-medium text-black/80">{band.label}</span>
            </div>
          ))}
        </div>

        <div className="relative mt-1 h-4">
          {ticks.map((tick, index) => {
            // The first and last labels are anchored to their edge; centring them puts half the
            // text outside the strip, where it wraps.
            const first = index === 0;
            const last = index === ticks.length - 1;
            return (
              <span
                key={tick}
                className={`absolute whitespace-nowrap text-[10px] text-muted-foreground ${
                  first || last ? "" : "-translate-x-1/2"
                }`}
                style={last ? { right: 0 } : { left: pct(tick) }}
              >
                {new Date(tick).toLocaleDateString(dateLocale, {
                  day: "numeric",
                  month: "short",
                  timeZone,
                })}
              </span>
            );
          })}
        </div>
      </div>

      {/* The point of the strip. A hole in cover is invisible in a list of shifts. */}
      {gaps.length > 0 && (
        <p className="text-xs text-error-400">
          {t("onCallStrip.gapSummary", {
            count: String(gaps.length),
            hours: String(hours(gaps.reduce((sum, g) => sum + (g.end - g.start), 0))),
          })}
        </p>
      )}
    </div>
  );
}
