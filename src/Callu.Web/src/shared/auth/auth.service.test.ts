import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { AuthService, RefreshUnavailableError } from "./auth.service";

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

function base64Url(value: string): string {
  return btoa(value).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

/** A token the service will accept as live — only the payload is ever read. */
function freshToken(sub = "user-1"): string {
  const payload = base64Url(
    JSON.stringify({ sub, exp: Math.floor(Date.now() / 1000) + 900, iat: Math.floor(Date.now() / 1000) })
  );
  return `header.${payload}.signature`;
}

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

describe("AuthService.refreshAccessToken", () => {
  beforeEach(() => {
    localStorage.clear();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("stores the new access token and returns true on success", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse({ data: { accessToken: "new-token" } })));
    const svc = new AuthService();

    const ok = await svc.refreshAccessToken();

    expect(ok).toBe(true);
    expect(svc.getAccessToken()).toBe("new-token");
  });

  it("clears tokens and returns false when the server rejects the refresh", async () => {
    const svc = new AuthService();
    svc.setAccessToken("stale");
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse({ message: "expired" }, 401)));

    const ok = await svc.refreshAccessToken();

    expect(ok).toBe(false);
    expect(svc.getAccessToken()).toBeNull();
  });

  // The three cases below are the ones that must never read as "signed out": returning false
  // would send the caller to logout(), so a slow or unreachable API would end the session.
  it("keeps the token and reports unavailable when the refresh request throws", async () => {
    const svc = new AuthService();
    svc.setAccessToken("still-valid");
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("network down")));

    await expect(svc.refreshAccessToken()).rejects.toBeInstanceOf(RefreshUnavailableError);
    expect(svc.getAccessToken()).toBe("still-valid");
  });

  it("keeps the token and reports unavailable when the server returns 5xx", async () => {
    const svc = new AuthService();
    svc.setAccessToken("still-valid");
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse({ message: "boom" }, 503)));

    await expect(svc.refreshAccessToken()).rejects.toBeInstanceOf(RefreshUnavailableError);
    expect(svc.getAccessToken()).toBe("still-valid");
  });

  it("keeps the token and reports unavailable when the refresh is aborted by its timeout", async () => {
    const svc = new AuthService();
    svc.setAccessToken("still-valid");
    vi.stubGlobal(
      "fetch",
      vi.fn().mockRejectedValue(new DOMException("The operation was aborted.", "AbortError"))
    );

    await expect(svc.refreshAccessToken()).rejects.toBeInstanceOf(RefreshUnavailableError);
    expect(svc.getAccessToken()).toBe("still-valid");
  });

  it("does not retry the refresh call — a second presentation of the cookie reads as reuse", async () => {
    const svc = new AuthService();
    svc.setAccessToken("still-valid");
    const fetchMock = vi.fn().mockRejectedValue(new TypeError("network down"));
    vi.stubGlobal("fetch", fetchMock);

    await expect(svc.refreshAccessToken()).rejects.toBeInstanceOf(RefreshUnavailableError);

    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  // The refresh cookie is single-use. Once a call may have been served without us reading the
  // answer, the copy the browser still holds may already be revoked server-side, and presenting it
  // again is the reuse the server revokes the whole token family for — every tab signed out.
  it("stops presenting the cookie after a refresh that may have reached the server", async () => {
    const svc = new AuthService();
    svc.setAccessToken("stale");
    const fetchMock = vi.fn().mockRejectedValue(new DOMException("aborted", "AbortError"));
    vi.stubGlobal("fetch", fetchMock);

    await expect(svc.refreshAccessToken()).rejects.toBeInstanceOf(RefreshUnavailableError);
    // Whatever asks next — a TanStack retry, apiClient, SignalR's reconnect — gets the same answer
    // without a second request going out.
    await expect(svc.refreshAccessToken()).rejects.toBeInstanceOf(RefreshUnavailableError);

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(svc.getAccessToken()).toBe("stale");
  });

  it("stops presenting the cookie when a gateway could not complete the call (504)", async () => {
    const svc = new AuthService();
    svc.setAccessToken("stale");
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ message: "gateway timeout" }, 504));
    vi.stubGlobal("fetch", fetchMock);

    await expect(svc.refreshAccessToken()).rejects.toBeInstanceOf(RefreshUnavailableError);
    await expect(svc.refreshAccessToken()).rejects.toBeInstanceOf(RefreshUnavailableError);

    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it("keeps refreshing after an offline failure — the request never left the machine", async () => {
    vi.stubGlobal("navigator", { onLine: false, locks: undefined });

    const svc = new AuthService();
    svc.setAccessToken("stale");
    const fetchMock = vi
      .fn()
      .mockRejectedValueOnce(new TypeError("Failed to fetch"))
      .mockResolvedValueOnce(jsonResponse({ data: { accessToken: "renewed" } }));
    vi.stubGlobal("fetch", fetchMock);

    await expect(svc.refreshAccessToken()).rejects.toBeInstanceOf(RefreshUnavailableError);
    await expect(svc.refreshAccessToken()).resolves.toBe(true);

    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(svc.getAccessToken()).toBe("renewed");
  });

  it("keeps refreshing after the API's own 5xx — the rotation is rolled back with it", async () => {
    const svc = new AuthService();
    svc.setAccessToken("stale");
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(jsonResponse({ message: "boom" }, 500))
      .mockResolvedValueOnce(jsonResponse({ data: { accessToken: "renewed" } }));
    vi.stubGlobal("fetch", fetchMock);

    await expect(svc.refreshAccessToken()).rejects.toBeInstanceOf(RefreshUnavailableError);
    await expect(svc.refreshAccessToken()).resolves.toBe(true);

    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it("uses the token another tab fetched instead of the cookie it gave up on", async () => {
    const svc = new AuthService();
    svc.setAccessToken("stale");
    const fetchMock = vi.fn().mockRejectedValue(new DOMException("aborted", "AbortError"));
    vi.stubGlobal("fetch", fetchMock);

    await expect(svc.refreshAccessToken()).rejects.toBeInstanceOf(RefreshUnavailableError);

    // Another tab renews the session; localStorage is shared, so this tab sees the new token.
    const renewed = freshToken();
    svc.setAccessToken(renewed);

    await expect(svc.refreshAccessToken()).resolves.toBe(true);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(svc.getAccessToken()).toBe(renewed);
  });

  it("refreshes again after a fresh sign-in — that issues a new cookie family", async () => {
    const svc = new AuthService();
    svc.setAccessToken("stale");
    const fetchMock = vi
      .fn()
      .mockRejectedValueOnce(new DOMException("aborted", "AbortError"))
      .mockResolvedValueOnce(jsonResponse({ data: { accessToken: "login-token", user: null } }))
      .mockResolvedValueOnce(jsonResponse({ data: { accessToken: "refreshed" } }));
    vi.stubGlobal("fetch", fetchMock);

    await expect(svc.refreshAccessToken()).rejects.toBeInstanceOf(RefreshUnavailableError);

    await svc.login("a@b.c", "pw").catch(() => undefined);

    await expect(svc.refreshAccessToken()).resolves.toBe(true);
    expect(svc.getAccessToken()).toBe("refreshed");
  });

  it("coalesces concurrent refreshes into a single request", async () => {
    const fetchMock = vi.fn(async () => {
      await delay(20);
      return jsonResponse({ data: { accessToken: "shared" } });
    });
    vi.stubGlobal("fetch", fetchMock);
    const svc = new AuthService();

    const [r1, r2] = await Promise.all([svc.refreshAccessToken(), svc.refreshAccessToken()]);

    expect(r1).toBe(true);
    expect(r2).toBe(true);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(svc.getAccessToken()).toBe("shared");
  });

  it("serialises refreshes across tabs without Web Locks and adopts the token the winner fetched", async () => {
    // Plain HTTP: navigator.locks is undefined because it needs a secure context. This is the
    // default Callu deployment. Two service instances sharing localStorage stand in for two tabs.
    vi.stubGlobal("navigator", { locks: undefined });

    const issued = freshToken();
    const fetchMock = vi.fn(async () => {
      await delay(30);
      return jsonResponse({ data: { accessToken: issued } });
    });
    vi.stubGlobal("fetch", fetchMock);

    const tabA = new AuthService();
    const tabB = new AuthService();

    const [a, b] = await Promise.all([tabA.refreshAccessToken(), tabB.refreshAccessToken()]);

    expect(a).toBe(true);
    expect(b).toBe(true);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(tabA.getAccessToken()).toBe(issued);
  });

  it("uses Web Locks when the origin is secure, and does not run the refresh twice", async () => {
    const request = vi.fn(async (_name: string, fn: () => Promise<unknown>) => fn());
    vi.stubGlobal("navigator", { ...navigator, locks: { request } });

    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ data: { accessToken: "locked" } }));
    vi.stubGlobal("fetch", fetchMock);

    const ok = await new AuthService().refreshAccessToken();

    expect(ok).toBe(true);
    expect(request).toHaveBeenCalledTimes(1);
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it("does not re-run the refresh when the Web Lock callback itself fails", async () => {
    const request = vi.fn(async (_name: string, fn: () => Promise<unknown>) => fn());
    vi.stubGlobal("navigator", { ...navigator, locks: { request } });

    const fetchMock = vi.fn().mockRejectedValue(new TypeError("network down"));
    vi.stubGlobal("fetch", fetchMock);
    const svc = new AuthService();
    svc.setAccessToken("still-valid");

    await expect(svc.refreshAccessToken()).rejects.toBeInstanceOf(RefreshUnavailableError);

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(svc.getAccessToken()).toBe("still-valid");
  });

  it("falls back to the storage lease when the lock manager refuses", async () => {
    const request = vi.fn().mockRejectedValue(new DOMException("insecure", "SecurityError"));
    vi.stubGlobal("navigator", { ...navigator, locks: { request } });

    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ data: { accessToken: "fallback" } }));
    vi.stubGlobal("fetch", fetchMock);

    const ok = await new AuthService().refreshAccessToken();

    expect(ok).toBe(true);
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });
});

describe("AuthService.logout", () => {
  beforeEach(() => {
    localStorage.clear();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("clears the local token and calls the server logout endpoint", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);
    const svc = new AuthService();
    svc.setAccessToken("live-token");

    await svc.logout();

    expect(svc.getAccessToken()).toBeNull();
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock.mock.calls[0][0]).toContain("/api/v1/auth/logout");
  });
});
