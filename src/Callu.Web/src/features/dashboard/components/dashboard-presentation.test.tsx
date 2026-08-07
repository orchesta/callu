import "@testing-library/jest-dom/vitest";
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import type { ComponentProps } from "react";

import { DashboardPresentation } from "./dashboard-presentation";
import type {
  OnCallOverrideDto,
  ScheduleDto,
  ScheduleRotationDto,
} from "@/features/schedules/types/schedule.types";

/** The dashboard's answer to "who is on call right now", read off the same bands the strip draws. */

const NOW = new Date("2026-07-29T09:00:00Z");

type OnCall = ComponentProps<typeof DashboardPresentation>["onCall"];

function schedule(over: Partial<ScheduleDto> = {}): ScheduleDto {
  return {
    id: "sch-1",
    name: "Primary on-call",
    teamId: "team-1",
    timezone: "UTC",
    rotationCount: 1,
    createdAt: "2026-06-01T00:00:00Z",
    ...over,
  };
}

function shift(user: string, startIso: string, endIso: string): ScheduleRotationDto {
  return {
    id: `${user}-${startIso}`,
    scheduleId: "sch-1",
    userId: user,
    userName: user,
    isPrimary: true,
    order: 0,
    shiftLengthMinutes: 1440,
    startUtc: startIso,
    endUtc: endIso,
  };
}

function override(user: string, startIso: string, endIso: string): OnCallOverrideDto {
  return {
    id: `o-${user}`,
    scheduleId: "sch-1",
    scheduleName: "Primary on-call",
    overrideUserId: user,
    overrideUserName: user,
    startUtc: startIso,
    endUtc: endIso,
    isActive: true,
  };
}

function renderDashboard(onCall: Partial<OnCall> = {}) {
  return render(
    <MemoryRouter>
      <DashboardPresentation
        recentIncidents={[]}
        severityCounts={{}}
        services={[]}
        isLoading={false}
        timeRange={0}
        onTimeRangeChange={() => {}}
        onCall={{
          schedules: [schedule()],
          selectedScheduleId: "sch-1",
          onSelectSchedule: () => {},
          occurrences: [],
          overrides: [],
          days: 2,
          isLoading: false,
          hasError: false,
          ...onCall,
        }}
      />
    </MemoryRouter>,
  );
}

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true });
  vi.setSystemTime(NOW);
});

afterEach(() => vi.useRealTimers());

describe("the on-call card on the dashboard", () => {
  it("names who is on call and draws the shift behind them", () => {
    renderDashboard({
      occurrences: [shift("Ada Lovelace", "2026-07-29T00:00:00Z", "2026-07-30T00:00:00Z")],
    });

    expect(screen.getByText("AL")).toBeInTheDocument();
    expect(screen.getAllByText("Ada Lovelace").length).toBeGreaterThan(0);
    expect(screen.getByTitle(/^Ada Lovelace —/)).toBeInTheDocument();
  });

  // An incident opening now would page nobody, which is the one thing the card exists to surface.
  it("says nobody is on call when the rota does not cover now", () => {
    renderDashboard({
      occurrences: [shift("Ada Lovelace", "2026-07-30T00:00:00Z", "2026-07-31T00:00:00Z")],
    });

    expect(screen.getByText(/nobody is on call right now/i)).toBeInTheDocument();
    expect(screen.queryByText("AL")).not.toBeInTheDocument();
  });

  it("says no schedule exists rather than showing a card with nothing in it", () => {
    renderDashboard({ schedules: [], selectedScheduleId: "" });

    expect(screen.getByText(/no on-call schedule yet/i)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /create schedule/i })).toHaveAttribute(
      "href",
      "/schedules/new",
    );
  });

  it("waits rather than claiming no schedule exists while the rota is still loading", () => {
    renderDashboard({ schedules: [], selectedScheduleId: "", isLoading: true });

    expect(screen.getByRole("status")).toBeInTheDocument();
    expect(screen.queryByText(/no on-call schedule yet/i)).not.toBeInTheDocument();
  });

  // A read that failed knows nothing about cover, and reporting that as "nobody" is a false alarm.
  it("does not claim nobody is on call when the rota could not be read", () => {
    renderDashboard({ schedules: [], selectedScheduleId: "", hasError: true });

    expect(screen.getByText(/could not load who is on call/i)).toBeInTheDocument();
    expect(screen.queryByText(/nobody is on call right now/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/no on-call schedule yet/i)).not.toBeInTheDocument();
  });

  // An override takes the primary slot without standing the rotation holder down: a step set to
  // page both reaches both, and the strip keeps drawing the holder's block either way. Naming only
  // the override would leave the headline disagreeing with the strip right under it.
  it("names the override first and the rotation holder it displaced second", () => {
    renderDashboard({
      occurrences: [shift("Ada Lovelace", "2026-07-29T00:00:00Z", "2026-07-30T00:00:00Z")],
      overrides: [override("Efe Kaya", "2026-07-29T08:00:00Z", "2026-07-29T12:00:00Z")],
    });

    expect(screen.getByText("Efe Kaya, Ada Lovelace")).toBeInTheDocument();
    expect(screen.getByText("EK")).toBeInTheDocument();
  });

  it("offers no picker when there is only one schedule to pick", () => {
    renderDashboard();

    expect(screen.queryByRole("group", { name: /choose a schedule/i })).not.toBeInTheDocument();
  });

  it("offers every schedule once there is more than one", () => {
    renderDashboard({ schedules: [schedule(), schedule({ id: "sch-2", name: "Database" })] });

    const picker = screen.getByRole("group", { name: /choose a schedule/i });
    expect(picker).toHaveTextContent("Primary on-call");
    expect(picker).toHaveTextContent("Database");
  });
});

/** The card is handed the shifts that have not ended yet, so its window opens at now. Opening it
 * at midnight instead turns every shift already finished today into a red band on the landing
 * screen, on a rota with nothing wrong with it. */
describe("the window the on-call card draws", () => {
  const EVENING = new Date("2026-07-29T20:00:00Z");

  // Three eight-hour shifts a day, meeting exactly, as the server returns them at 20:00: the two
  // that ran earlier today are simply not in the answer.
  const GAPLESS = [
    shift("Ada Lovelace", "2026-07-29T16:00:00Z", "2026-07-30T00:00:00Z"),
    shift("Efe Kaya", "2026-07-30T00:00:00Z", "2026-07-30T08:00:00Z"),
    shift("Deniz Su", "2026-07-30T08:00:00Z", "2026-07-30T16:00:00Z"),
    shift("Ada Lovelace", "2026-07-30T16:00:00Z", "2026-07-31T00:00:00Z"),
  ];

  it("reports no hole in a rota that covers every hour from now on", () => {
    vi.setSystemTime(EVENING);
    renderDashboard({ occurrences: GAPLESS });

    expect(screen.queryByText(/gap\(s\) in cover/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/nobody is on call right now/i)).not.toBeInTheDocument();
  });

  it("closes the window at the end of the days the card asked for", () => {
    vi.setSystemTime(EVENING);
    renderDashboard({ occurrences: [GAPLESS[0]] });

    expect(screen.getByText(/1 gap\(s\) in cover, 24h in total/i)).toBeInTheDocument();
  });

  // The zone decides where the last day ends: Los Angeles midnight is seven hours after UTC's.
  it("measures the window in the schedule's zone, not in UTC", () => {
    vi.setSystemTime(EVENING);
    renderDashboard({
      schedules: [schedule({ timezone: "America/Los_Angeles" })],
      occurrences: [shift("Ada Lovelace", "2026-07-29T16:00:00Z", "2026-07-31T00:00:00Z")],
    });

    expect(screen.getByText(/1 gap\(s\) in cover, 7h in total/i)).toBeInTheDocument();
  });

  it("draws the override on the strip, not only in the headline", () => {
    renderDashboard({
      occurrences: [shift("Ada Lovelace", "2026-07-29T00:00:00Z", "2026-07-30T00:00:00Z")],
      overrides: [override("Efe Kaya", "2026-07-29T08:00:00Z", "2026-07-29T12:00:00Z")],
    });

    // Once as the headline badge, once as the strip's legend key.
    expect(screen.getAllByText("Override")).toHaveLength(2);
    expect(screen.getByTitle(/^Efe Kaya —/)).toBeInTheDocument();
  });
});

describe("severity impact", () => {
  function renderWithCounts(severityCounts: Record<string, number>) {
    return render(
      <MemoryRouter>
        <DashboardPresentation
          recentIncidents={[]}
          severityCounts={severityCounts}
          services={[]}
          isLoading={false}
          timeRange={0}
          onTimeRangeChange={() => {}}
          onCall={{
            schedules: [],
            selectedScheduleId: "",
            onSelectSchedule: () => {},
            occurrences: [],
            overrides: [],
            days: 7,
            isLoading: false,
            hasError: false,
          }}
        />
      </MemoryRouter>,
    );
  }

  it("counts the whole range, not the rows the recent list happens to show", () => {
    // recentIncidents is deliberately empty: the panel must still report the two criticals.
    renderWithCounts({ Critical: 2, High: 4, Medium: 4, Low: 2 });

    const row = (label: string) =>
      screen.getByText(label).closest("div")?.parentElement?.textContent ?? "";

    expect(row("Critical")).toContain("2");
    expect(row("High")).toContain("4");
    expect(row("Medium")).toContain("4");
    expect(row("Low")).toContain("2");
  });

  it("reads a missing severity as zero rather than blank", () => {
    renderWithCounts({});
    expect(screen.getByText("Critical")).toBeInTheDocument();
  });
});
