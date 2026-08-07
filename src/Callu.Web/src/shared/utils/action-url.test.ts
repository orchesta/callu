import { describe, it, expect } from "vitest";
import { resolveActionUrl } from "./action-url";

// actionUrl is server-supplied and goes straight into the router, so anything unknown or off-origin
// must land on the notifications list.
describe("resolveActionUrl", () => {
  it("passes through a known route", () => {
    expect(resolveActionUrl("/incidents/8f1c")).toBe("/incidents/8f1c");
  });

  it("passes through a known route with a query string", () => {
    expect(resolveActionUrl("/incidents?status=open")).toBe("/incidents?status=open");
  });

  it("passes through a bare known prefix", () => {
    expect(resolveActionUrl("/dashboard")).toBe("/dashboard");
  });

  it("passes through a prefix that is itself trailing-slashed", () => {
    expect(resolveActionUrl("/conference/room-1")).toBe("/conference/room-1");
  });

  it("trims surrounding whitespace before matching", () => {
    expect(resolveActionUrl("  /teams/42  ")).toBe("/teams/42");
  });

  it("strips a same-path absolute URL down to the in-app route", () => {
    expect(resolveActionUrl("http://localhost:3000/incidents/8f1c")).toBe("/incidents/8f1c");
    expect(resolveActionUrl("https://status.example.com/incidents/8f1c?tab=timeline")).toBe(
      "/incidents/8f1c?tab=timeline",
    );
  });

  it.each([
    ["https://evil.example/steal", "absolute URL with unknown path"],
    ["//evil.example/steal", "protocol-relative URL"],
    ["javascript:alert(1)", "javascript: URI"],
    ["incidents/8f1c", "relative path with no leading slash"],
  ])("falls back to /notifications for %s (%s)", (input) => {
    expect(resolveActionUrl(input)).toBe("/notifications");
  });

  it("falls back to /notifications for an unknown in-app route", () => {
    expect(resolveActionUrl("/admin/secret")).toBe("/notifications");
  });

  it("does not treat a prefix as matched on a partial segment", () => {
    expect(resolveActionUrl("/incidents-archive")).toBe("/notifications");
  });

  it("falls back for an empty string", () => {
    expect(resolveActionUrl("")).toBe("/notifications");
  });
});
