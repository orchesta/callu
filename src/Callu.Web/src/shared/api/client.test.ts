import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { ApiErrorCategory } from "./api-errors";

const authMock = vi.hoisted(() => ({
  getAccessToken: vi.fn<() => string | null>(() => "tok"),
  refreshAccessToken: vi.fn<() => Promise<boolean>>(),
  logout: vi.fn<() => Promise<void>>(() => Promise.resolve()),
}));

vi.mock("../auth/auth.service", () => ({ authService: authMock }));

import { apiClient } from "./client";
import { ApiError } from "./api-errors";

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

describe("apiClient", () => {
  beforeEach(() => {
    authMock.getAccessToken.mockReturnValue("tok");
    authMock.refreshAccessToken.mockReset();
    authMock.logout.mockReset().mockResolvedValue(undefined);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("unwraps the ApiResponse envelope", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse({ success: true, data: { id: 1 } })));
    const res = await apiClient.get<{ id: number }>("/things");
    expect(res.success).toBe(true);
    expect(res.data).toEqual({ id: 1 });
  });

  it("returns null data for 204", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status: 204 })));
    const res = await apiClient.get("/things");
    expect(res).toEqual({ success: true, data: null });
  });

  it("on 401 refreshes, retries once, and succeeds", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(jsonResponse({ success: false, message: "unauth" }, 401))
      .mockResolvedValueOnce(jsonResponse({ success: true, data: "ok" }));
    vi.stubGlobal("fetch", fetchMock);
    authMock.refreshAccessToken.mockResolvedValue(true);

    const res = await apiClient.get<string>("/secure");

    expect(res.data).toBe("ok");
    expect(authMock.refreshAccessToken).toHaveBeenCalledTimes(1);
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(authMock.logout).not.toHaveBeenCalled();
  });

  it("on 401 with a failed refresh logs out and throws", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse({ success: false, message: "unauth" }, 401)));
    authMock.refreshAccessToken.mockResolvedValue(false);

    await expect(apiClient.get("/secure")).rejects.toBeInstanceOf(ApiError);
    expect(authMock.logout).toHaveBeenCalledTimes(1);
  });

  it("does not refresh twice when the post-refresh retry also 401s", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(jsonResponse({ success: false }, 401))
      .mockResolvedValueOnce(jsonResponse({ success: false }, 401));
    vi.stubGlobal("fetch", fetchMock);
    authMock.refreshAccessToken.mockResolvedValue(true);

    await expect(apiClient.get("/secure")).rejects.toBeInstanceOf(ApiError);
    expect(authMock.refreshAccessToken).toHaveBeenCalledTimes(1);
  });

  it("does not retry a 5xx on a non-idempotent POST", async () => {
    const postFetch = vi.fn().mockResolvedValue(jsonResponse({ success: false }, 500));
    vi.stubGlobal("fetch", postFetch);

    await expect(apiClient.post("/x", {}, { retry: 1 })).rejects.toBeInstanceOf(ApiError);
    expect(postFetch).toHaveBeenCalledTimes(1);
  });

  describe("success envelope shapes", () => {
    it("passes an enveloped body through unchanged", async () => {
      vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse({ success: false, data: null, message: "soft" })));
      const res = await apiClient.get("/things");
      // A 200 whose body already carries `success` is returned verbatim, even success:false.
      expect(res).toMatchObject({ success: false, message: "soft" });
    });

    it("wraps a bare JSON body that has no success field", async () => {
      vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse([1, 2, 3])));
      const res = await apiClient.get<number[]>("/list");
      expect(res).toEqual({ success: true, data: [1, 2, 3] });
    });
  });

  describe("error envelope parsing", () => {
    it("carries the message and field errors from a 400 body into the ApiError", async () => {
      vi.stubGlobal("fetch", vi.fn().mockResolvedValue(
        jsonResponse({ success: false, message: "Bad input", errors: { name: ["required"] } }, 400),
      ));

      await expect(apiClient.get("/x")).rejects.toMatchObject({
        statusCode: 400,
        message: "Bad input",
        errors: { name: ["required"] },
      });
    });

    // Pinned quirk: parseErrorResponse calls response.json() first, which consumes the body even
    // when parsing fails, so the response.text() fallback is unreachable for a non-JSON body — the
    // message stays the HTTP status line rather than the raw text. Recorded, not endorsed.
    it("keeps the status line when an error body is not JSON (text fallback is unreachable)", async () => {
      vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response("upstream exploded", { status: 502 })));

      const err = await apiClient.get("/x").catch((e) => e);
      expect(err.statusCode).toBe(502);
      expect(err.message).not.toContain("upstream exploded");
      expect(err.message).toContain("502");
    });

    it("does not attempt a refresh on a 403", async () => {
      vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse({ success: false, message: "forbidden" }, 403)));

      await expect(apiClient.get("/x")).rejects.toMatchObject({ statusCode: 403 });
      expect(authMock.refreshAccessToken).not.toHaveBeenCalled();
    });
  });

  describe("transport errors", () => {
    it("normalizes a fetch TypeError into a network ApiError and does not retry it", async () => {
      const netFetch = vi.fn().mockRejectedValue(new TypeError("Failed to fetch"));
      vi.stubGlobal("fetch", netFetch);

      await expect(apiClient.get("/x", { retry: 3 })).rejects.toMatchObject({
        category: ApiErrorCategory.Network,
      });
      // A network failure is terminal — the retry budget must not be spent on it.
      expect(netFetch).toHaveBeenCalledTimes(1);
    });

    it("normalizes an aborted request into a timeout ApiError", async () => {
      vi.stubGlobal("fetch", vi.fn().mockRejectedValue(
        Object.assign(new DOMException("aborted", "AbortError")),
      ));

      await expect(apiClient.get("/x")).rejects.toMatchObject({
        category: ApiErrorCategory.Timeout,
      });
    });
  });

  describe("auth header", () => {
    it("attaches the bearer token by default", async () => {
      const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ success: true, data: 1 }));
      vi.stubGlobal("fetch", fetchMock);

      await apiClient.get("/x");

      const headers = fetchMock.mock.calls[0][1].headers as Record<string, string>;
      expect(headers["Authorization"]).toBe("Bearer tok");
    });

    it("omits the bearer token when skipAuth is set", async () => {
      const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ success: true, data: 1 }));
      vi.stubGlobal("fetch", fetchMock);

      await apiClient.get("/x", { skipAuth: true });

      const headers = fetchMock.mock.calls[0][1].headers as Record<string, string>;
      expect(headers["Authorization"]).toBeUndefined();
    });

    it("appends query params, skipping undefined ones", async () => {
      const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ success: true, data: 1 }));
      vi.stubGlobal("fetch", fetchMock);

      await apiClient.get("/x", { params: { page: 2, q: "hi", skip: undefined } });

      const url = fetchMock.mock.calls[0][0] as string;
      expect(url).toContain("page=2");
      expect(url).toContain("q=hi");
      expect(url).not.toContain("skip");
    });
  });

  describe("array query params", () => {
    it("repeats the key once per element", async () => {
      const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ success: true, data: 1 }));
      vi.stubGlobal("fetch", fetchMock);

      await apiClient.get("/x", { params: { entityTypes: ["Incident", "Escalation"] } });

      const url = new URL(fetchMock.mock.calls[0][0] as string);
      expect(url.searchParams.getAll("entityTypes")).toEqual(["Incident", "Escalation"]);
    });

    it("emits nothing for an empty array", async () => {
      const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ success: true, data: 1 }));
      vi.stubGlobal("fetch", fetchMock);

      await apiClient.get("/x", { params: { entityTypes: [] } });

      expect(new URL(fetchMock.mock.calls[0][0] as string).search).toBe("");
    });

    it("skips an undefined element", async () => {
      const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ success: true, data: 1 }));
      vi.stubGlobal("fetch", fetchMock);

      await apiClient.get("/x", { params: { entityTypes: ["Incident", undefined, "Escalation"] } });

      const url = new URL(fetchMock.mock.calls[0][0] as string);
      expect(url.searchParams.getAll("entityTypes")).toEqual(["Incident", "Escalation"]);
    });

    it("leaves scalar serialisation unchanged", async () => {
      const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ success: true, data: 1 }));
      vi.stubGlobal("fetch", fetchMock);

      await apiClient.get("/x", { params: { page: 2, pageSize: 25, intact: false, q: "a b", skip: undefined } });

      const url = new URL(fetchMock.mock.calls[0][0] as string);
      expect(url.search).toBe("?page=2&pageSize=25&intact=false&q=a+b");
    });

    it("serialises a scalar and an array in one call", async () => {
      const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ success: true, data: 1 }));
      vi.stubGlobal("fetch", fetchMock);

      await apiClient.get("/x", { params: { page: 1, entityTypes: ["Incident", "Escalation"] } });

      const url = new URL(fetchMock.mock.calls[0][0] as string);
      expect(url.search).toBe("?page=1&entityTypes=Incident&entityTypes=Escalation");
    });
  });

  it("retries an idempotent GET on a 5xx, then gives up", async () => {
    vi.useFakeTimers();
    try {
      const getFetch = vi.fn().mockResolvedValue(jsonResponse({ success: false }, 503));
      vi.stubGlobal("fetch", getFetch);

      const pending = apiClient.get("/x", { retry: 2 });
      const assertion = expect(pending).rejects.toBeInstanceOf(ApiError);
      // Drive the backoff delays between attempts.
      await vi.runAllTimersAsync();
      await assertion;

      // Original attempt + 2 retries.
      expect(getFetch).toHaveBeenCalledTimes(3);
    } finally {
      vi.useRealTimers();
    }
  });
});
