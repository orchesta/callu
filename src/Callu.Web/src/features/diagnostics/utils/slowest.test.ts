import { describe, it, expect } from "vitest";
import type { OperationStats } from "../types/tracing.types";
import { isLongLivedConnection, slowestOperation } from "./slowest";

/** The panel reported "Slowest 655.85s — GET /hubs/notifications", which is how long the browser
 * tab had been open, not how long anything took. */

function op(operation: string, p95DurationMs: number): OperationStats {
  return {
    operation,
    service: "callu-api",
    traceCount: 10,
    avgDurationMs: p95DurationMs / 2,
    p95DurationMs,
    maxDurationMs: p95DurationMs,
    errorCount: 0,
    lastSeen: "2026-08-05T09:00:00Z",
  };
}

describe("the slowest operation", () => {
  it("ignores a hub connection, however long it has been open", () => {
    const slowest = slowestOperation([
      op("GET /hubs/notifications", 655_850),
      op("GET /api/v1/incidents", 420),
      op("POST /api/v1/incidents", 180),
    ]);

    expect(slowest?.operation).toBe("GET /api/v1/incidents");
  });

  it("reports nothing rather than a connection when that is all there is", () => {
    expect(slowestOperation([op("GET /hubs/notifications", 655_850)])).toBeNull();
    expect(slowestOperation([])).toBeNull();
  });

  it("still reports a genuinely slow request", () => {
    const slowest = slowestOperation([op("GET /api/v1/reports", 9_400), op("GET /api/v1/teams", 30)]);

    expect(slowest?.operation).toBe("GET /api/v1/reports");
  });

  it.each([
    ["GET /hubs/notifications", true],
    ["/HUBS/Notifications", true],
    ["WebSocket GET /hubs/notifications", true],
    ["GET /api/v1/incidents", false],
    ["GET /api/v1/status-pages/hub-city", false],
  ])("classifies %s", (operation, expected) => {
    expect(isLongLivedConnection(operation)).toBe(expected);
  });
});
