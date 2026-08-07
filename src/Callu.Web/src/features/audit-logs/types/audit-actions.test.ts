import { existsSync, readFileSync } from "node:fs";
import path from "node:path";
import { describe, it, expect } from "vitest";
import { AUDIT_ACTIONS } from "./audit-log.types";

// The action list is a hand-kept copy of the backend enum and it drifted: IntegrityVerified and
// IntegrityBroken were added to the enum, rows were written with them, and an auditor could not
// filter for either — the two actions that exist to surface tampering.
const ENUM_SUFFIX = path.join("Callu.Domain", "Enums", "AuditAction.cs");

/** Walk up from the working directory to the solution's `src/`, wherever vitest was started. */
function readSolutionFile(suffix: string): string {
  let dir = process.cwd();

  for (;;) {
    const candidate = path.join(dir, suffix);
    if (existsSync(candidate)) return readFileSync(candidate, "utf8");

    const parent = path.dirname(dir);
    if (parent === dir) break;
    dir = parent;
  }

  throw new Error(
    `Cannot find ${suffix} above ${process.cwd()}. This list is only trustworthy while it is ` +
      `checked against the enum — if the file moved, repoint this rather than drop the check.`,
  );
}

/** Every `Name = 0,` member of the enum, in declaration order. */
function backendActions(): string[] {
  const source = readSolutionFile(ENUM_SUFFIX);
  const body = source.slice(source.indexOf("{"), source.lastIndexOf("}"));

  const members = [...body.matchAll(/^\s*([A-Z][A-Za-z]*)\s*=\s*\d+\s*,?\s*$/gm)].map((m) => m[1]);

  if (members.length === 0) throw new Error("Parsed no members out of AuditAction.cs");
  return members;
}

describe("the audit action list the filter offers", () => {
  it("offers every action the backend can write", () => {
    const missing = backendActions().filter((a) => !(AUDIT_ACTIONS as readonly string[]).includes(a));

    expect(missing).toEqual([]);
  });

  /** An action offered here that the backend cannot write filters to a permanently empty screen. */
  it("offers nothing the backend cannot write", () => {
    const backend = backendActions();
    const extra = AUDIT_ACTIONS.filter((a) => !backend.includes(a));

    expect(extra).toEqual([]);
  });

  it("reads a real enum rather than passing vacuously", () => {
    expect(backendActions().length).toBeGreaterThan(30);
    expect(backendActions()).toContain("Login");
  });
});
