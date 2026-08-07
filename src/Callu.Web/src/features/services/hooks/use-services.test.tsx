import type { ReactNode } from "react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

/** The server clamps pageSize to 100. A caller that asks for more gets 100 back and no hint that
 * anything was left behind, so "all services" has to be assembled page by page. */

const getAll = vi.fn();

vi.mock("../api/service.api", () => ({
  serviceApi: {
    getAll: (page: number, pageSize: number) => getAll(page, pageSize),
  },
}));

const { useServices } = await import("./use-services");

function service(id: string) {
  return { id, name: `svc-${id}` };
}

function page(items: unknown[], totalCount: number, pageNo: number) {
  return Promise.resolve({
    success: true,
    data: {
      items,
      totalCount,
      page: pageNo,
      pageSize: 100,
      totalPages: Math.ceil(totalCount / 100),
    },
  });
}

let client: QueryClient;

function wrapper({ children }: { children: ReactNode }) {
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}

beforeEach(() => {
  vi.clearAllMocks();
  client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
});

describe("useServices", () => {
  it("never asks for a page larger than the server allows", async () => {
    getAll.mockImplementation(() => page([service("a")], 1, 1));

    const { result } = renderHook(() => useServices(), { wrapper });
    await waitFor(() => expect(result.current.data).toHaveLength(1));

    for (const [, pageSize] of getAll.mock.calls) {
      expect(pageSize).toBeLessThanOrEqual(100);
    }
  });

  it("returns every service in a catalog larger than one page", async () => {
    const first = Array.from({ length: 100 }, (_, i) => service(`a${i}`));
    const second = Array.from({ length: 34 }, (_, i) => service(`b${i}`));
    getAll.mockImplementation((pageNo: number) =>
      page(pageNo === 1 ? first : second, 134, pageNo),
    );

    const { result } = renderHook(() => useServices(), { wrapper });

    // 134 services on a 100-row page means the second page is not optional.
    await waitFor(() => expect(result.current.data).toHaveLength(134));
    expect(getAll).toHaveBeenCalledTimes(2);
    expect(result.current.data[result.current.data.length - 1]).toMatchObject({ id: "b33" });
  });

  it("makes exactly one request when everything fits on one page", async () => {
    getAll.mockImplementation(() => page([service("a"), service("b")], 2, 1));

    const { result } = renderHook(() => useServices(), { wrapper });
    await waitFor(() => expect(result.current.data).toHaveLength(2));

    expect(getAll).toHaveBeenCalledTimes(1);
  });

  it("surfaces a failed page instead of returning a short list", async () => {
    getAll.mockImplementation((pageNo: number) =>
      pageNo === 1
        ? page(Array.from({ length: 100 }, (_, i) => service(`a${i}`)), 134, 1)
        : Promise.resolve({ success: false, message: "Service list unavailable" }),
    );

    const { result } = renderHook(() => useServices(), { wrapper });

    // A half-read catalog must not look like a complete one — the caller has to see the error.
    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(result.current.data).toEqual([]);
  });
});
