import { existsSync, readFileSync } from "node:fs";
import path from "node:path";
import { describe, it, expect } from "vitest";
import {
  ROLES,
  PERMISSIONS,
  ROLE_PERMISSIONS,
  ROUTE_PERMISSIONS,
  isRole,
  hasPermission,
  canAccessRoute,
  canAcknowledgeIncidents,
  canManageIncidents,
  canManageSchedules,
  canManageTeams,
  canResolveIncidents,
  type Role,
} from "./roles";

// The role → permission table claims to mirror the backend seeder, so read the seeder and compare
// rather than take it on trust.
const SEEDER_SUFFIX = path.join("Callu.Infrastructure", "Persistence", "Seeding", "DbSeeder.cs");

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
    `Cannot find ${suffix} above ${process.cwd()}. This table is only trustworthy while it is ` +
      `checked against the backend — if the file moved, repoint this rather than drop the check.`,
  );
}

const readSeeder = () => readSolutionFile(SEEDER_SUFFIX);

/** The policy on the controller a gated page depends on. `Policies.X` is `nameof(X)`, so the name is the claim. */
function classLevelPolicy(controller: string): string {
  const source = readSolutionFile(path.join("Callu.Api", "Controllers", controller));
  const match = /\[Authorize\(Policy = Policies\.(\w+)\)\]/.exec(source);

  if (!match) throw new Error(`${controller} has no class-level [Authorize(Policy = ...)]`);
  return match[1];
}

/** `private static readonly string[] Roles = { "Admin", ... };` */
function parseSeededRoles(source: string): string[] {
  const match = /string\[\]\s+Roles\s*=\s*[[{]([^\]}]*)[\]}]/.exec(source);
  if (!match) throw new Error("Could not find the Roles array in DbSeeder.cs");

  return (match[1].match(/"\w+"/g) ?? []).map((s) => s.slice(1, -1));
}

/** The RoleClaims dictionary. Entries use either a `[...]` collection expression or `new[] { ... }`. */
function parseSeededRoleClaims(source: string): Record<string, string[]> {
  const start = source.indexOf("RoleClaims = new()");
  if (start === -1) throw new Error("Could not find the RoleClaims dictionary in DbSeeder.cs");

  const end = source.indexOf("\n    };", start);
  const region = source.slice(start, end === -1 ? source.length : end);

  // split() on a capturing pattern yields [preamble, role, body, role, body, ...]
  const parts = region.split(/\["(\w+)"\]\s*=/);
  const seeded: Record<string, string[]> = {};

  for (let i = 1; i < parts.length; i += 2) {
    const body = parts[i + 1] ?? "";
    seeded[parts[i]] = (body.match(/"Can\w+"/g) ?? []).map((s) => s.slice(1, -1));
  }

  if (Object.keys(seeded).length === 0) {
    throw new Error("Parsed no roles out of RoleClaims in DbSeeder.cs");
  }

  return seeded;
}

describe("the table the backend actually seeds", () => {
  const source = readSeeder();
  const seededRoles = parseSeededRoles(source);
  const seededClaims = parseSeededRoleClaims(source);

  it("names the same roles the seeder creates", () => {
    expect([...seededRoles].sort()).toEqual([...ROLES].sort());
  });

  it("grants each role exactly the claims the seeder grants it", () => {
    expect(Object.keys(seededClaims).sort()).toEqual([...ROLES].sort());

    for (const role of ROLES) {
      expect([...(seededClaims[role] ?? [])].sort()).toEqual([...ROLE_PERMISSIONS[role]].sort());
    }
  });

  it("knows every claim the seeder hands out, and invents none", () => {
    const seeded = new Set(Object.values(seededClaims).flat());
    expect([...new Set(Object.values(PERMISSIONS))].sort()).toEqual([...seeded].sort());
  });
});

describe("hasPermission", () => {
  it("gives Admin every permission it knows about", () => {
    for (const permission of Object.values(PERMISSIONS)) {
      expect(hasPermission("Admin", permission)).toBe(true);
    }
  });

  it("withholds user/settings/audit administration from everyone but Admin", () => {
    const adminOnly = [
      PERMISSIONS.ManageUsers,
      PERMISSIONS.ManageSettings,
      PERMISSIONS.ManageBilling,
      PERMISSIONS.ManageIntegrations,
      PERMISSIONS.ViewAuditLog,
    ];

    for (const permission of adminOnly) {
      expect(hasPermission("TeamLead", permission)).toBe(false);
      expect(hasPermission("Member", permission)).toBe(false);
      expect(hasPermission("Viewer", permission)).toBe(false);
    }
  });

  it("lets a Member act on incidents but never reshape them", () => {
    expect(canAcknowledgeIncidents("Member")).toBe(true);
    expect(canResolveIncidents("Member")).toBe(true);
    expect(canManageIncidents("Member")).toBe(false);
  });

  it("keeps a Viewer strictly read-only", () => {
    expect(canAcknowledgeIncidents("Viewer")).toBe(false);
    expect(canResolveIncidents("Viewer")).toBe(false);
    expect(canManageIncidents("Viewer")).toBe(false);
    expect(canManageSchedules("Viewer")).toBe(false);
    expect(canManageTeams("Viewer")).toBe(false);
    expect(hasPermission("Viewer", PERMISSIONS.ViewIncidents)).toBe(true);
  });

  it("lets a TeamLead manage the on-call surface", () => {
    expect(canManageIncidents("TeamLead")).toBe(true);
    expect(canManageSchedules("TeamLead")).toBe(true);
    expect(canManageTeams("TeamLead")).toBe(true);
  });

  it("denies everything to an absent, empty or unrecognised role", () => {
    for (const role of [null, undefined, "", "Root", "admin;drop"]) {
      expect(hasPermission(role, PERMISSIONS.ViewIncidents)).toBe(false);
    }
  });

  it("reads a comma-joined multi-role claim as the union of its roles", () => {
    expect(hasPermission("Viewer,Admin", PERMISSIONS.ManageUsers)).toBe(true);
    expect(hasPermission("Viewer, Member", PERMISSIONS.AcknowledgeIncidents)).toBe(true);
  });

  it("ignores unknown roles inside a multi-role claim rather than failing open", () => {
    expect(hasPermission("Root,Viewer", PERMISSIONS.ManageUsers)).toBe(false);
    expect(hasPermission("Root,Viewer", PERMISSIONS.ViewIncidents)).toBe(true);
  });

  it("matches role names case-insensitively", () => {
    expect(hasPermission("admin", PERMISSIONS.ManageUsers)).toBe(true);
    expect(hasPermission("ADMIN", PERMISSIONS.ManageUsers)).toBe(true);
  });
});

describe("isRole", () => {
  it("accepts exactly the seeded roles", () => {
    for (const role of ROLES) {
      expect(isRole(role)).toBe(true);
    }
  });

  it("rejects anything else", () => {
    expect(isRole("Root")).toBe(false);
    expect(isRole("admin")).toBe(false); // the claim is exact-case; only hasPermission is lenient
    expect(isRole(null)).toBe(false);
    expect(isRole(undefined)).toBe(false);
    expect(isRole("")).toBe(false);
  });
});

// Every gated route, paired with the controller whose class-level policy the gate mirrors.
const ROUTE_CONTROLLERS: ReadonlyArray<{ prefix: string; controller: string }> = [
  { prefix: "/users", controller: "UsersController.cs" },
  { prefix: "/audit-logs", controller: "AuditLogsController.cs" },
  { prefix: "/settings", controller: "SettingsController.cs" },
  { prefix: "/maintenance", controller: "MaintenanceWindowsController.cs" },
  { prefix: "/diagnostics", controller: "DiagnosticsController.cs" },
  { prefix: "/applications", controller: "IntegrationsController.cs" },
];

describe("the policies the API actually applies", () => {
  it("gates each route on the same claim its controller requires", () => {
    for (const { prefix, controller } of ROUTE_CONTROLLERS) {
      const rule = ROUTE_PERMISSIONS.find((r) => r.prefix === prefix);

      expect(rule, `${prefix} is not in ROUTE_PERMISSIONS`).toBeDefined();
      expect(rule?.permission, `${prefix} does not match ${controller}`).toBe(classLevelPolicy(controller));
    }
  });

  it("gates no route the controllers do not restrict", () => {
    expect([...ROUTE_PERMISSIONS].map((r) => r.prefix).sort()).toEqual(
      ROUTE_CONTROLLERS.map((r) => r.prefix).sort(),
    );
  });
});

describe("canAccessRoute", () => {
  it("opens unrestricted routes to every signed-in role", () => {
    for (const role of ROLES) {
      expect(canAccessRoute(role, "/incidents")).toBe(true);
      expect(canAccessRoute(role, "/schedules/abc")).toBe(true);
      expect(canAccessRoute(role, "/dashboard")).toBe(true);
    }
  });

  it("restricts the claim-gated routes to the roles that hold the claim", () => {
    const gated: Array<[string, Role[]]> = [
      ["/users", ["Admin"]],
      ["/audit-logs", ["Admin", "Auditor"]],
      ["/settings", ["Admin"]],
      ["/maintenance", ["Admin"]],
      ["/diagnostics", ["Admin"]],
      ["/applications", ["Admin", "TeamLead"]],
    ];

    for (const [path, allowed] of gated) {
      for (const role of ROLES) {
        expect(canAccessRoute(role, path)).toBe(allowed.includes(role));
      }
    }
  });

  it("gates child routes of a restricted prefix too", () => {
    expect(canAccessRoute("Viewer", "/settings/notifications")).toBe(false);
    expect(canAccessRoute("Admin", "/settings/notifications")).toBe(true);
    expect(canAccessRoute("Viewer", "/users/42")).toBe(false);
  });

  it("does not let a lookalike prefix inherit the gate", () => {
    // "/settings-export" is not under "/settings" — it must not be silently locked down (nor,
    // conversely, must "/settings" be matched by a bare startsWith).
    expect(canAccessRoute("Viewer", "/settings-export")).toBe(true);
    expect(canAccessRoute("Viewer", "/userscript")).toBe(true);
  });

  it("denies a restricted route to an unauthenticated or unknown role", () => {
    expect(canAccessRoute(null, "/settings")).toBe(false);
    expect(canAccessRoute("Root", "/users")).toBe(false);
  });
});
