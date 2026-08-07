import "@testing-library/jest-dom/vitest";
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen } from "@testing-library/react";

import { LifecycleStrip } from "./lifecycle-strip";

const NOW = new Date("2026-07-29T14:30:00Z");

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true });
  vi.setSystemTime(NOW);
});

afterEach(() => vi.useRealTimers());

describe("the lifecycle strip", () => {
  it("shows every state the incident can pass through", () => {
    render(<LifecycleStrip status="Open" startedAt="2026-07-29T14:00:00Z" />);

    for (const state of ["Open", "Acknowledged", "Investigating", "Mitigated", "Resolved", "Closed"]) {
      expect(screen.getByTitle(state)).toBeInTheDocument();
    }
  });

  // The one line that makes this more than decoration: a badge already says "Acknowledged", it
  // just never said for how long.
  it("says how long the incident has been sitting in its current state", () => {
    render(
      <LifecycleStrip
        status="Acknowledged"
        startedAt="2026-07-29T14:00:00Z"
        acknowledgedAt="2026-07-29T14:07:00Z"
      />,
    );

    expect(screen.getByText(/in Acknowledged/i)).toBeInTheDocument();
  });

  // Investigating and Mitigated have no stored entry time. Falling back to the incident's own age
  // would put a number on screen that looks like the state's age and is not.
  it("says nothing about duration when the state's entry time is not recorded", () => {
    render(
      <LifecycleStrip
        status="Investigating"
        startedAt="2026-07-29T14:00:00Z"
        acknowledgedAt="2026-07-29T14:07:00Z"
      />,
    );

    expect(screen.queryByText(/in Investigating/i)).not.toBeInTheDocument();
  });

  it("stamps only the states whose time the API records", () => {
    render(
      <LifecycleStrip
        status="Resolved"
        startedAt="2026-07-29T14:00:00Z"
        acknowledgedAt="2026-07-29T14:07:00Z"
        resolvedAt="2026-07-29T14:25:00Z"
      />,
    );

    const items = screen.getAllByRole("listitem");
    // Open, Acknowledged and Resolved carry a clock; Investigating, Mitigated and Closed do not.
    const stamped = items.filter((li) => /\d{1,2}[:.]\d{2}/.test(li.textContent ?? ""));
    expect(stamped).toHaveLength(3);
  });

  it("renders nothing for a status outside the lifecycle", () => {
    const { container } = render(<LifecycleStrip status="Nonsense" startedAt="2026-07-29T14:00:00Z" />);

    expect(container).toBeEmptyDOMElement();
  });

  it("survives an incident that was never acknowledged", () => {
    render(<LifecycleStrip status="Closed" startedAt="2026-07-29T14:00:00Z" />);

    expect(screen.getByTitle("Closed")).toBeInTheDocument();
    expect(screen.queryByText(/in Closed/i)).not.toBeInTheDocument();
  });
});
