import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { withRefreshLock } from "./refresh-lock";

// The Web Lock API needs a secure context, so on a plain-HTTP install (and in jsdom) the
// localStorage lease exercised below is the path every tab actually takes.

const LEASE_KEY = "callu:auth:refresh:lock";

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

describe("withRefreshLock — the localStorage lease (no Web Locks)", () => {
  beforeEach(() => {
    localStorage.clear();
    vi.stubGlobal("navigator", { locks: undefined });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it("runs the work and hands the lease back", async () => {
    const result = await withRefreshLock(async () => "refreshed");

    expect(result).toBe("refreshed");
    expect(localStorage.getItem(LEASE_KEY)).toBeNull();
  });

  it("serialises two concurrent callers — they never overlap", async () => {
    let inside = 0;
    let overlapped = false;

    const work = async () => {
      inside += 1;
      if (inside > 1) overlapped = true;
      await delay(30);
      inside -= 1;
    };

    await Promise.all([withRefreshLock(work), withRefreshLock(work)]);

    expect(overlapped).toBe(false);
    expect(localStorage.getItem(LEASE_KEY)).toBeNull();
  });

  it("releases the lease when the work throws, so the next tab is not locked out", async () => {
    await expect(withRefreshLock(async () => Promise.reject(new Error("network down")))).rejects.toThrow(
      "network down",
    );

    expect(localStorage.getItem(LEASE_KEY)).toBeNull();

    // The lease is genuinely free again.
    await expect(withRefreshLock(async () => "next")).resolves.toBe("next");
  });

  it("does not retry the work — a second refresh is the reuse the lock exists to prevent", async () => {
    const work = vi.fn().mockRejectedValue(new Error("boom"));

    await expect(withRefreshLock(work)).rejects.toThrow("boom");

    expect(work).toHaveBeenCalledTimes(1);
  });

  it("takes over a lease whose holder died", async () => {
    localStorage.setItem(
      LEASE_KEY,
      JSON.stringify({ owner: "a-tab-that-crashed", expiresAt: Date.now() - 1 }),
    );

    await expect(withRefreshLock(async () => "taken over")).resolves.toBe("taken over");
  });

  it("waits for a live lease instead of refreshing alongside it", async () => {
    const holder = { owner: "another-tab", expiresAt: Date.now() + 200 };
    localStorage.setItem(LEASE_KEY, JSON.stringify(holder));

    const work = vi.fn().mockResolvedValue("mine");
    const pending = withRefreshLock(work);

    await delay(60);
    expect(work).not.toHaveBeenCalled();

    // The holder finishes and drops its lease.
    localStorage.removeItem(LEASE_KEY);

    await expect(pending).resolves.toBe("mine");
    expect(work).toHaveBeenCalledTimes(1);
  });

  it("treats a corrupt lease as no lease rather than blocking on it forever", async () => {
    localStorage.setItem(LEASE_KEY, "not json at all");

    await expect(withRefreshLock(async () => "ran anyway")).resolves.toBe("ran anyway");
  });

  it("treats a lease of the wrong shape as no lease", async () => {
    localStorage.setItem(LEASE_KEY, JSON.stringify({ owner: 42 }));

    await expect(withRefreshLock(async () => "ran anyway")).resolves.toBe("ran anyway");
  });

  it("still refreshes when storage is unavailable — there is just nothing to serialise on", async () => {
    vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => {
      throw new DOMException("storage disabled", "SecurityError");
    });
    vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
      throw new DOMException("storage disabled", "SecurityError");
    });

    await expect(withRefreshLock(async () => "unserialised")).resolves.toBe("unserialised");
  });
});

describe("withRefreshLock — the BroadcastChannel election (no Web Locks)", () => {
  /**
   * The lease and the election fail in different ways, so each is tested with the other taken out.
   * Here storage is dead: whatever serialisation survives is the election's doing.
   */
  beforeEach(() => {
    localStorage.clear();
    vi.stubGlobal("navigator", { locks: undefined });

    vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => {
      throw new DOMException("storage disabled", "SecurityError");
    });
    vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
      throw new DOMException("storage disabled", "SecurityError");
    });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it("serialises two concurrent callers with no lease to fall back on", async () => {
    let inside = 0;
    let overlapped = false;

    const work = async () => {
      inside += 1;
      if (inside > 1) overlapped = true;
      await delay(40);
      inside -= 1;
    };

    await Promise.all([withRefreshLock(work), withRefreshLock(work)]);

    expect(overlapped).toBe(false);
  });

  it("serialises three concurrent callers — the losers queue rather than pile on", async () => {
    let inside = 0;
    let peak = 0;
    const order: number[] = [];

    const work = (n: number) => async () => {
      inside += 1;
      peak = Math.max(peak, inside);
      order.push(n);
      await delay(30);
      inside -= 1;
    };

    await Promise.all([withRefreshLock(work(1)), withRefreshLock(work(2)), withRefreshLock(work(3))]);

    expect(peak).toBe(1);
    expect(order).toHaveLength(3);
  });

  it("lets a caller through once the tab ahead of it has finished", async () => {
    const seen: string[] = [];

    const first = withRefreshLock(async () => {
      seen.push("first-in");
      await delay(50);
      seen.push("first-out");
    });

    // Arrives while the first is still working.
    await delay(20);
    const second = withRefreshLock(async () => {
      seen.push("second-in");
    });

    await Promise.all([first, second]);

    expect(seen).toEqual(["first-in", "first-out", "second-in"]);
  });

  it("still runs the work when it is the only tab", async () => {
    await expect(withRefreshLock(async () => "alone")).resolves.toBe("alone");
  });

  it("a failing leader does not strand the tab behind it", async () => {
    const second = vi.fn().mockResolvedValue("ran");

    const [, result] = await Promise.allSettled([
      withRefreshLock(async () => {
        await delay(30);
        throw new Error("refresh blew up");
      }),
      withRefreshLock(second),
    ]);

    expect(result.status).toBe("fulfilled");
    expect(second).toHaveBeenCalledTimes(1);
  });
});

describe("withRefreshLock — Web Locks (secure context)", () => {
  beforeEach(() => {
    localStorage.clear();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("uses the lock manager when it is there, and runs the work once", async () => {
    const request = vi.fn(async (_name: string, fn: () => Promise<unknown>) => fn());
    vi.stubGlobal("navigator", { locks: { request } });

    const work = vi.fn().mockResolvedValue("locked");

    await expect(withRefreshLock(work)).resolves.toBe("locked");
    expect(request).toHaveBeenCalledTimes(1);
    expect(request.mock.calls[0][0]).toBe("callu:auth:refresh");
    expect(work).toHaveBeenCalledTimes(1);
  });

  it("propagates a failure from inside the lock without re-running the work", async () => {
    const request = vi.fn(async (_name: string, fn: () => Promise<unknown>) => fn());
    vi.stubGlobal("navigator", { locks: { request } });

    const work = vi.fn().mockRejectedValue(new Error("network down"));

    await expect(withRefreshLock(work)).rejects.toThrow("network down");
    expect(work).toHaveBeenCalledTimes(1);
  });

  it("falls back to the lease when the lock manager itself refuses, rather than running unguarded", async () => {
    const request = vi.fn().mockRejectedValue(new DOMException("insecure", "SecurityError"));
    vi.stubGlobal("navigator", { locks: { request } });

    const work = vi.fn().mockResolvedValue("leased");

    await expect(withRefreshLock(work)).resolves.toBe("leased");
    expect(work).toHaveBeenCalledTimes(1);
    expect(localStorage.getItem(LEASE_KEY)).toBeNull();
  });
});
