import "@testing-library/jest-dom/vitest";
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { act, render, screen } from "@testing-library/react";

/** Two cards on one screen read this clock. If they turn over at different moments, one of them is
 * naming last minute's on-call while the other draws this minute's. */

import { useNow } from "./use-now";

const MINUTE = 60_000;

function Clock({
  testId,
  tickMs = MINUTE,
  active = true,
}: {
  testId: string;
  tickMs?: number;
  active?: boolean;
}) {
  const now = useNow(tickMs, active);
  return <span data-testid={testId}>{now}</span>;
}

function read(testId: string): number {
  return Number(screen.getByTestId(testId).textContent);
}

beforeEach(() => vi.useFakeTimers());
afterEach(() => vi.useRealTimers());

describe("useNow", () => {
  it("reads the clock as it is at mount", () => {
    vi.setSystemTime(new Date("2026-07-29T09:00:10Z"));
    render(<Clock testId="a" />);

    expect(read("a")).toBe(Date.parse("2026-07-29T09:00:10Z"));
  });

  it("turns over for two readers mounted a moment apart at the same instant", () => {
    vi.setSystemTime(new Date("2026-07-29T09:00:10Z"));
    render(<Clock testId="a" />);

    act(() => vi.advanceTimersByTime(20_000));
    render(<Clock testId="b" />);

    act(() => vi.advanceTimersByTime(30_000));

    expect(read("a")).toBe(Date.parse("2026-07-29T09:01:00Z"));
    expect(read("b")).toBe(Date.parse("2026-07-29T09:01:00Z"));
  });

  it("keeps ticking on the minute after the first turn", () => {
    vi.setSystemTime(new Date("2026-07-29T09:00:10Z"));
    render(<Clock testId="a" />);

    act(() => vi.advanceTimersByTime(50_000 + MINUTE));

    expect(read("a")).toBe(Date.parse("2026-07-29T09:02:00Z"));
  });

  it("stops reading the clock while it is switched off", () => {
    vi.setSystemTime(new Date("2026-07-29T09:00:10Z"));
    render(<Clock testId="a" active={false} />);

    act(() => vi.advanceTimersByTime(5 * MINUTE));

    expect(read("a")).toBe(Date.parse("2026-07-29T09:00:10Z"));
  });
});
