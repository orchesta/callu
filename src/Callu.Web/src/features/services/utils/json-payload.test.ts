import { describe, it, expect } from "vitest";
import { extractFields, resolveJsonPath } from "./json-payload";

/** Characterization tests: they pin the webhook template editor's behaviour quirks included, so a
 * disagreement reports a refactor bug rather than a fix. Arguably-wrong cases say so. */

describe("extractFields", () => {
  it("emits one entry per leaf, keyed by path from the $ root", () => {
    expect(extractFields({ status: "firing" })).toEqual([
      { path: "$.status", value: "firing", type: "string" },
    ]);
  });

  it("descends into nested objects rather than listing the object itself", () => {
    expect(extractFields({ labels: { severity: "critical" } })).toEqual([
      { path: "$.labels.severity", value: "critical", type: "string" },
    ]);
  });

  it("indexes array members into the path", () => {
    const fields = extractFields({ alerts: [{ name: "a" }, { name: "b" }] });
    expect(fields.map((f) => f.path)).toEqual(["$.alerts[0].name", "$.alerts[1].name"]);
  });

  // Pinned quirk: only an object inside an array produces leaves. An array of scalars is walked
  // into, hits the scalar branch, and pushes nothing — so `$.tags[0]` is never offered as a
  // mappable field even though resolveJsonPath would happily resolve it.
  it("drops scalar array members, offering no field for them", () => {
    expect(extractFields({ tags: ["x"] })).toEqual([]);
    expect(resolveJsonPath({ tags: ["x"] }, "$.tags[0]")).toBe("x");
  });

  it("stringifies leaf values but reports the original runtime type", () => {
    const fields = extractFields({ count: 5, enabled: true });
    expect(fields).toEqual([
      { path: "$.count", value: "5", type: "number" },
      { path: "$.enabled", value: "true", type: "boolean" },
    ]);
  });

  it("honours a caller-supplied prefix", () => {
    expect(extractFields({ a: 1 }, "$.root")[0].path).toBe("$.root.a");
  });

  it("returns nothing for null or undefined input", () => {
    expect(extractFields(null)).toEqual([]);
    expect(extractFields(undefined)).toEqual([]);
  });

  it("returns nothing for a scalar at the root — only objects and arrays are traversed", () => {
    expect(extractFields("firing")).toEqual([]);
  });

  // Pinned quirk: `typeof null === "object"`, but the null check sends it to the leaf branch,
  // so a null leaf is reported with type "object" and the literal value "null".
  it("reports a null leaf as type object with the string \"null\"", () => {
    expect(extractFields({ note: null })).toEqual([
      { path: "$.note", value: "null", type: "object" },
    ]);
  });

  // Pinned quirk: an empty object/array is traversed, yields no leaves, and so disappears from
  // the field list entirely — the user cannot map it.
  it("drops empty objects and arrays instead of listing them", () => {
    expect(extractFields({ meta: {}, alerts: [] })).toEqual([]);
  });
});

describe("resolveJsonPath", () => {
  const payload = {
    status: "firing",
    alerts: [{ labels: { severity: "critical" } }],
  };

  it("resolves a top-level key", () => {
    expect(resolveJsonPath(payload, "$.status")).toBe("firing");
  });

  it("resolves through an array index into a nested object", () => {
    expect(resolveJsonPath(payload, "$.alerts[0].labels.severity")).toBe("critical");
  });

  it("returns undefined for a key that is not there", () => {
    expect(resolveJsonPath(payload, "$.missing")).toBeUndefined();
  });

  it("returns undefined for an out-of-range array index", () => {
    expect(resolveJsonPath(payload, "$.alerts[9].labels")).toBeUndefined();
  });

  it("returns undefined when an index is applied to something that is not an array", () => {
    expect(resolveJsonPath(payload, "$.status[0]")).toBeUndefined();
  });

  it("returns undefined when a scalar is walked through", () => {
    expect(resolveJsonPath(payload, "$.status.deeper")).toBeUndefined();
  });

  it("round-trips every path extractFields produced", () => {
    for (const field of extractFields(payload)) {
      expect(String(resolveJsonPath(payload, field.path))).toBe(field.value);
    }
  });

  // Pinned quirk: the "$." prefix is stripped with a plain replace, which is neither anchored
  // nor global. A bare "$" is left intact and then looked up as a key.
  it("does not resolve a bare $ root", () => {
    expect(resolveJsonPath(payload, "$")).toBeUndefined();
  });
});
