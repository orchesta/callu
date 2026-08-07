import { describe, it, expect } from "vitest";
import { createQueryClient } from "./query-client";
import { ApiError, ApiErrorCategory } from "./api-errors";
import { RefreshUnavailableError } from "../auth/auth.service";

/** The retry predicate as TanStack Query will call it. */
function willRetry(failureCount: number, error: unknown): boolean {
  const retry = createQueryClient().getDefaultOptions().queries?.retry;
  if (typeof retry !== "function") throw new Error("queries.retry must be a predicate");

  return (retry as (count: number, err: unknown) => boolean)(failureCount, error);
}

describe("queryClient retry policy", () => {
  it("retries server errors", () => {
    expect(willRetry(0, new ApiError(500, "boom"))).toBe(true);
  });

  it("retries a plain network failure", () => {
    const offline = new ApiError(0, "Network connection failed", {
      category: ApiErrorCategory.Network,
    });

    expect(willRetry(0, offline)).toBe(true);
  });

  it("does not retry 4xx", () => {
    expect(willRetry(0, new ApiError(404, "gone"))).toBe(false);
  });

  it("gives up after two failures", () => {
    expect(willRetry(2, new ApiError(500, "boom"))).toBe(false);
  });

  // RefreshUnavailableError carries the Network category, which is otherwise retryable, so it has
  // to be excluded by name or a retry re-presents a single-use refresh cookie the server rotated.
  it("never retries a refresh that could not be completed", () => {
    expect(willRetry(0, new RefreshUnavailableError(new Error("timed out")))).toBe(false);
  });
});
