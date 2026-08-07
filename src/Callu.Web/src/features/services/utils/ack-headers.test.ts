import { describe, it, expect } from "vitest";
import { parseAckHeaders, serializeAckHeaders } from "./ack-headers";

describe("parseAckHeaders", () => {
  it("turns a stored object into an ordered key/value list", () => {
    expect(parseAckHeaders('{"X-Token":"abc","X-Env":"prod"}')).toEqual([
      { key: "X-Token", value: "abc" },
      { key: "X-Env", value: "prod" },
    ]);
  });

  it("treats an absent or empty value as no headers", () => {
    expect(parseAckHeaders(undefined)).toEqual([]);
    expect(parseAckHeaders("")).toEqual([]);
    expect(parseAckHeaders("{}")).toEqual([]);
  });

  // A stored value that will not parse must not take the settings tab down with it.
  it("falls back to an empty list for malformed JSON instead of throwing", () => {
    expect(parseAckHeaders("{not json")).toEqual([]);
    expect(parseAckHeaders("null")).toEqual([]);
  });

  it("stringifies non-string values", () => {
    expect(parseAckHeaders('{"X-Retry":3,"X-Debug":true}')).toEqual([
      { key: "X-Retry", value: "3" },
      { key: "X-Debug", value: "true" },
    ]);
  });
});

describe("serializeAckHeaders", () => {
  it("writes the list back out as an object", () => {
    expect(serializeAckHeaders([{ key: "X-Token", value: "abc" }])).toBe('{"X-Token":"abc"}');
  });

  it("returns undefined rather than an empty object when nothing is set", () => {
    expect(serializeAckHeaders([])).toBeUndefined();
  });

  // The editor seeds a new row blank; an untouched one must not travel as an empty header.
  it("drops rows with a blank or whitespace-only key", () => {
    expect(serializeAckHeaders([{ key: "  ", value: "v" }, { key: "", value: "v" }])).toBeUndefined();
    expect(serializeAckHeaders([{ key: "X-Real", value: "1" }, { key: "", value: "v" }])).toBe('{"X-Real":"1"}');
  });

  it("trims the key", () => {
    expect(serializeAckHeaders([{ key: "  X-Token  ", value: "abc" }])).toBe('{"X-Token":"abc"}');
  });

  // Pinned quirk: the key is trimmed but the value is not — a trailing space in a token survives.
  it("does not trim the value", () => {
    expect(serializeAckHeaders([{ key: "X-Token", value: " abc " }])).toBe('{"X-Token":" abc "}');
  });

  it("keeps a blank value against a real key", () => {
    expect(serializeAckHeaders([{ key: "X-Token", value: "" }])).toBe('{"X-Token":""}');
  });

  // Pinned quirk: rows are collapsed into an object, so a duplicated key silently loses the first.
  it("lets a later duplicate key win", () => {
    expect(serializeAckHeaders([{ key: "X-T", value: "first" }, { key: "X-T", value: "second" }])).toBe('{"X-T":"second"}');
  });
});

describe("ack header round-trip", () => {
  it("survives parse → serialize unchanged", () => {
    const stored = '{"X-Token":"abc","X-Env":"prod"}';
    expect(serializeAckHeaders(parseAckHeaders(stored))).toBe(stored);
  });

  it("survives serialize → parse unchanged", () => {
    const headers = [{ key: "X-Token", value: "abc" }];
    expect(parseAckHeaders(serializeAckHeaders(headers))).toEqual(headers);
  });
});
