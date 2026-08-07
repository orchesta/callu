import { describe, it, expect } from "vitest";
import { isUnreachableBaseUrl } from "./base-url";

/** The value goes into the links in outgoing emails. Left at the default it produces notifications
 * that open nothing for anyone but the person running the server, and nothing reports an error. */

describe("an unreachable public base URL", () => {
  it.each([
    ["", true],
    ["   ", true],
    [null, true],
    [undefined, true],
    ["http://localhost:3000", true],
    ["https://localhost", true],
    ["http://127.0.0.1:3000", true],
    ["http://0.0.0.0:8080", true],
    ["callu.example.com", true],
    ["https://callu.example.com", false],
    ["https://callu.example.com:8443/", false],
    ["http://10.0.5.20:3000", false],
  ])("classifies %s", (value, expected) => {
    expect(isUnreachableBaseUrl(value)).toBe(expected);
  });

  // A host merely containing "localhost" is a real address someone can reach.
  it("does not flag a hostname that only looks local", () => {
    expect(isUnreachableBaseUrl("https://localhost.callu.example.com")).toBe(false);
  });
});
