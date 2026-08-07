import { describe, it, expect } from "vitest";
import { orderComponents } from "./component-order";

const c = (id: string, displayOrder: number) => ({ id, displayOrder });

describe("orderComponents — no drag in progress", () => {
  it("sorts by displayOrder, not by array position", () => {
    const out = orderComponents([c("b", 2), c("a", 1), c("c", 3)], null);
    expect(out.map((x) => x.id)).toEqual(["a", "b", "c"]);
  });

  it("returns a new array and leaves the input untouched", () => {
    const input = [c("b", 2), c("a", 1)];
    const out = orderComponents(input, null);
    expect(out).not.toBe(input);
    expect(input.map((x) => x.id)).toEqual(["b", "a"]);
  });

  it("handles an empty list", () => {
    expect(orderComponents([], null)).toEqual([]);
  });
});

describe("orderComponents — drag in progress", () => {
  const stored = [c("a", 1), c("b", 2), c("c", 3)];

  it("follows the drag order instead of displayOrder", () => {
    const out = orderComponents(stored, ["c", "a", "b"]);
    expect(out.map((x) => x.id)).toEqual(["c", "a", "b"]);
  });

  it("keeps the stored order when the drag order matches it", () => {
    expect(orderComponents(stored, ["a", "b", "c"]).map((x) => x.id)).toEqual(["a", "b", "c"]);
  });

  // A refetch mid-drag can introduce a component the drag order has never seen. It must not vanish.
  it("puts a component missing from the drag order at the end rather than dropping it", () => {
    const out = orderComponents([...stored, c("new", 4)], ["c", "a", "b"]);
    expect(out.map((x) => x.id)).toEqual(["c", "a", "b", "new"]);
  });

  it("orders several unknown components among themselves by displayOrder", () => {
    const out = orderComponents([c("a", 1), c("y", 9), c("x", 5)], ["a"]);
    expect(out.map((x) => x.id)).toEqual(["a", "x", "y"]);
  });

  it("ignores drag-order ids that no longer exist", () => {
    const out = orderComponents([c("a", 1), c("b", 2)], ["b", "deleted", "a"]);
    expect(out.map((x) => x.id)).toEqual(["b", "a"]);
  });

  it("does not sort the input in place while applying a drag order", () => {
    const input = [c("a", 1), c("b", 2), c("c", 3)];
    orderComponents(input, ["c", "b", "a"]);
    expect(input.map((x) => x.id)).toEqual(["a", "b", "c"]);
  });

  it("treats an empty drag order as ordering nothing, falling back to displayOrder", () => {
    expect(orderComponents(stored, []).map((x) => x.id)).toEqual(["a", "b", "c"]);
  });
});
