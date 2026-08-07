import { describe, it, expect } from "vitest";
import { getAvatarColor, getUserInitials, getUserFullName } from "./user-display";
import type { UserDto } from "../types/user.types";

/**
 * These pin the behaviour these helpers had inside the users page before they were lifted out.
 * Both name helpers are fallback chains, so each case names the rung it lands on.
 */

function user(over: Partial<UserDto> = {}): UserDto {
  return {
    id: "u1",
    email: "ada@example.com",
    role: "Member",
    isActive: true,
    emailConfirmed: true,
    createdAt: "2026-01-01T00:00:00Z",
    ...over,
  };
}

describe("getAvatarColor", () => {
  it("is stable for the same id", () => {
    expect(getAvatarColor("abc")).toBe(getAvatarColor("abc"));
  });

  it("always lands on a real palette colour", () => {
    for (const id of ["", "a", "user-42", "ZZZ", "9f8e7d6c-1234-5678"]) {
      expect(getAvatarColor(id)).toMatch(/^bg-[a-z]+-500$/);
    }
  });

  it("separates at least some different ids", () => {
    const seen = new Set(["a", "b", "c", "d", "e", "f", "g", "h"].map(getAvatarColor));
    expect(seen.size).toBeGreaterThan(1);
  });
});

describe("getUserInitials", () => {
  it("prefers the server-supplied initials", () => {
    expect(getUserInitials(user({ initials: "AL", firstName: "Ada", lastName: "Lovelace" }))).toBe("AL");
  });

  it("falls back to first+last initials, uppercased", () => {
    expect(getUserInitials(user({ firstName: "ada", lastName: "lovelace" }))).toBe("AL");
  });

  it("uses whichever name half exists", () => {
    expect(getUserInitials(user({ firstName: "Ada" }))).toBe("A");
    expect(getUserInitials(user({ lastName: "Lovelace" }))).toBe("L");
  });

  it("falls back to the email's first letter when no name is known", () => {
    expect(getUserInitials(user())).toBe("A");
  });

  // Pinned quirk: empty strings are falsy, so they fall through to email rather than yielding "".
  it("treats empty name fields as absent", () => {
    expect(getUserInitials(user({ firstName: "", lastName: "" }))).toBe("A");
  });
});

describe("getUserFullName", () => {
  it("prefers displayName", () => {
    expect(getUserFullName(user({ displayName: "Ada L.", firstName: "Ada", lastName: "Lovelace" }))).toBe("Ada L.");
  });

  it("joins first and last name", () => {
    expect(getUserFullName(user({ firstName: "Ada", lastName: "Lovelace" }))).toBe("Ada Lovelace");
  });

  it("trims when only one name half exists", () => {
    expect(getUserFullName(user({ firstName: "Ada" }))).toBe("Ada");
    expect(getUserFullName(user({ lastName: "Lovelace" }))).toBe("Lovelace");
  });

  it("falls back to the email", () => {
    expect(getUserFullName(user())).toBe("ada@example.com");
  });
});
