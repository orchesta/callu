import { describe, it, expect } from "vitest";
import { normalizeCreateResult } from "./incident.api";

/** The API returns the outcome envelope for both 201 and 202. An older API answered 201 with the
 * bare incident, and the SPA can meet one during a staggered container restart, so both are read. */
describe("normalizeCreateResult", () => {
  it("wraps the bare incident an older API returns on 201", () => {
    const incident = { id: "inc-1", title: "DB down", severity: "High" };

    const result = normalizeCreateResult(incident as never);

    expect(result.outcome).toBe("Created");
    expect(result.incident).toBe(incident);
  });

  it("passes the envelope through untouched, reason included", () => {
    const envelope = {
      outcome: "Suppressed" as const,
      incident: null,
      reason: "DB migration 02:00-04:00",
    };

    expect(normalizeCreateResult(envelope)).toBe(envelope);
  });

  it("does not mistake an incident for a suppressed one", () => {
    const result = normalizeCreateResult({ id: "inc-2", title: "API latency" } as never);

    expect(result.outcome).not.toBe("Suppressed");
    expect(result.incident).not.toBeNull();
  });
});
