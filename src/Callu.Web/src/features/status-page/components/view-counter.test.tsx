import "@testing-library/jest-dom/vitest";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { StrictMode } from "react";
import { renderHook } from "@testing-library/react";

const recordView = vi.fn();

vi.mock("../api/status-page.api", () => ({
  statusPageApi: { recordView: (id: string) => recordView(id) },
}));

const { useRecordStatusPageView } = await import("../hooks/use-status-pages");

beforeEach(() => {
  vi.clearAllMocks();
  recordView.mockResolvedValue(undefined);
});

describe("counting a visit to a public status page", () => {
  it("counts the page once it is known", () => {
    renderHook(() => useRecordStatusPageView("page-1"));

    expect(recordView).toHaveBeenCalledExactlyOnceWith("page-1");
  });

  /** The page refetches every 30 seconds; each of those must not read as another visitor. */
  it("counts once per page, not once per render", () => {
    const { rerender } = renderHook(({ id }) => useRecordStatusPageView(id), {
      initialProps: { id: "page-1" },
    });

    rerender({ id: "page-1" });
    rerender({ id: "page-1" });

    expect(recordView).toHaveBeenCalledTimes(1);
  });

  /** StrictMode runs the effect twice on mount, which would otherwise double every visit. */
  it("counts once even though the effect is invoked twice", () => {
    renderHook(() => useRecordStatusPageView("page-1"), { wrapper: StrictMode });

    expect(recordView).toHaveBeenCalledTimes(1);
  });

  it("counts nothing until the page has loaded", () => {
    renderHook(() => useRecordStatusPageView(undefined));

    expect(recordView).not.toHaveBeenCalled();
  });

  it("counts the new page when the visitor moves to another one", () => {
    const { rerender } = renderHook(({ id }) => useRecordStatusPageView(id), {
      initialProps: { id: "page-1" },
    });

    rerender({ id: "page-2" });

    expect(recordView.mock.calls.map((c) => c[0])).toEqual(["page-1", "page-2"]);
  });

  /** A visitor must never see the counter fail — the page is what they came for. */
  it("swallows a failure to count", () => {
    recordView.mockRejectedValue(new Error("rate limited"));

    expect(() => renderHook(() => useRecordStatusPageView("page-1"))).not.toThrow();
  });
});
