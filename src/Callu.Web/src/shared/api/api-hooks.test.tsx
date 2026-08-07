import type { ReactNode } from "react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

const toastMock = vi.hoisted(() => ({
  success: vi.fn(),
  error: vi.fn(),
}));

vi.mock("../utils/toast", () => ({ toast: toastMock }));
vi.mock("../locales/i18n", () => ({ t: (key: string) => key }));

import { useApiQuery, useApiMutation, apiQueryOptions } from "./api-hooks";
import { ApiError } from "./api-errors";
import type { ApiResponse } from "../types/common.types";

function ok<T>(data: T): ApiResponse<T> {
  return { success: true, data } as ApiResponse<T>;
}

function fail<T>(message: string): ApiResponse<T> {
  return { success: false, message } as ApiResponse<T>;
}

function wrapper() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
}

describe("useApiQuery", () => {
  it("unwraps the ApiResponse envelope so components receive T directly", async () => {
    const { result } = renderHook(
      () => useApiQuery(["incidents"], async () => ok([{ id: 1 }])),
      { wrapper: wrapper() },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual([{ id: 1 }]);
  });

  it("turns an unsuccessful envelope into an ApiError instead of resolving with junk", async () => {
    const { result } = renderHook(
      () => useApiQuery(["incidents"], async () => fail<unknown[]>("Backend said no")),
      { wrapper: wrapper() },
    );

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(result.current.error).toBeInstanceOf(ApiError);
    expect(result.current.error?.message).toBe("Backend said no");
    expect(result.current.data).toBeUndefined();
  });
});

describe("apiQueryOptions", () => {
  it("shares the query key and unwrapping contract with useApiQuery", async () => {
    const options = apiQueryOptions(["incidents", 1], async () => ok({ id: 1 }));

    expect(options.queryKey).toEqual(["incidents", 1]);
    await expect(
      (options.queryFn as () => Promise<{ id: number }>)(),
    ).resolves.toEqual({ id: 1 });
  });
});

describe("useApiMutation", () => {
  beforeEach(() => {
    toastMock.success.mockReset();
    toastMock.error.mockReset();
  });

  it("unwraps the envelope and toasts the supplied success message", async () => {
    const { result } = renderHook(
      () =>
        useApiMutation(async (name: string) => ok({ id: 7, name }), {
          successMessage: "Incident created",
        }),
      { wrapper: wrapper() },
    );

    result.current.mutate("db down");

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual({ id: 7, name: "db down" });
    expect(toastMock.success).toHaveBeenCalledWith("Incident created");
  });

  it("falls back to the default success message", async () => {
    const { result } = renderHook(
      () => useApiMutation(async () => ok({ id: 1 })),
      { wrapper: wrapper() },
    );

    result.current.mutate();

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(toastMock.success).toHaveBeenCalledWith("toast.operationSuccess");
  });

  it("stays silent when successMessage is false", async () => {
    const { result } = renderHook(
      () => useApiMutation(async () => ok({ id: 1 }), { successMessage: false }),
      { wrapper: wrapper() },
    );

    result.current.mutate();

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(toastMock.success).not.toHaveBeenCalled();
  });

  it("rejects an unsuccessful envelope and toasts the error", async () => {
    const { result } = renderHook(
      () => useApiMutation(async () => fail<{ id: number }>("Conflict")),
      { wrapper: wrapper() },
    );

    result.current.mutate();

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(result.current.error).toBeInstanceOf(ApiError);
    expect(toastMock.error).toHaveBeenCalledWith("common.error", "Conflict");
    expect(toastMock.success).not.toHaveBeenCalled();
  });

  it("still runs the caller's onSuccess / onError alongside the toast", async () => {
    const onSuccess = vi.fn();
    const { result } = renderHook(
      () =>
        useApiMutation<{ id: number }, void>(async () => ok({ id: 1 }), {
          successMessage: false,
          onSuccess,
        }),
      { wrapper: wrapper() },
    );

    result.current.mutate();

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(onSuccess).toHaveBeenCalledTimes(1);
  });
});
