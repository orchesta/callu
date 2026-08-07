import "@testing-library/jest-dom/vitest";
import type { ReactNode } from "react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, act, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

// AuthProvider decides once per page load whether this browser still has a session, and everything
// routed behind it waits on that answer. authService is mocked: this covers the decisions only.

const getCurrentUser = vi.fn();
const getAccessToken = vi.fn();
const isAuthenticated = vi.fn();
const refreshAccessToken = vi.fn();
const login = vi.fn();
const logout = vi.fn();
const fetchStoredIdentity = vi.fn();

vi.mock("./auth.service", () => ({
  authService: {
    getCurrentUser: () => getCurrentUser(),
    getAccessToken: () => getAccessToken(),
    isAuthenticated: () => isAuthenticated(),
    refreshAccessToken: () => refreshAccessToken(),
    login: (email: string, password: string) => login(email, password),
    logout: () => logout(),
    fetchStoredIdentity: () => fetchStoredIdentity(),
  },
}));

const { AuthProvider, useAuth } = await import("./auth.context");
const { AUTH_TOKEN_KEY } = await import("@/shared/config");

const ADA = { id: "u1", email: "ada@x.io", name: "Ada", role: "Admin" };

let queryClient: QueryClient;

function wrapper({ children }: { children: ReactNode }) {
  return (
    <QueryClientProvider client={queryClient}>
      <AuthProvider>{children}</AuthProvider>
    </QueryClientProvider>
  );
}

function renderAuth() {
  return renderHook(() => useAuth(), { wrapper });
}

function seedCachedIncidents() {
  queryClient.setQueryData(["incidents", "list"], [{ id: "i1", title: "Ada's incident" }]);
}

beforeEach(() => {
  vi.clearAllMocks();
  queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  getCurrentUser.mockReturnValue(null);
  getAccessToken.mockReturnValue(null);
  isAuthenticated.mockReturnValue(false);
  fetchStoredIdentity.mockResolvedValue(null);
  refreshAccessToken.mockResolvedValue(undefined);
});

describe("AuthProvider — deciding the session on boot", () => {
  it("silently refreshes a stale token rather than treating it as a logout", async () => {
    // A token exists but has expired: exactly the overnight-tab case the refresh cookie is for.
    getAccessToken.mockReturnValue("stale.jwt");
    isAuthenticated.mockReturnValue(false);
    refreshAccessToken.mockImplementation(async () => {
      // After a successful refresh the service has a fresh token and a readable user.
      getCurrentUser.mockReturnValue(ADA);
    });

    const { result } = renderAuth();

    await waitFor(() => expect(result.current.isLoading).toBe(false));
    expect(refreshAccessToken).toHaveBeenCalledTimes(1);
    // The session survived the reload.
    expect(result.current.isAuthenticated).toBe(true);
    expect(result.current.user).toEqual(ADA);
  });

  it("does not finish boot pinned on the splash screen when the server is unreachable", async () => {
    // THE deadlock: the catch is empty on purpose, but isLoading must still clear.
    getAccessToken.mockReturnValue("stale.jwt");
    isAuthenticated.mockReturnValue(false);
    refreshAccessToken.mockRejectedValue(new Error("ECONNREFUSED"));

    const { result } = renderAuth();

    await waitFor(() => expect(result.current.isLoading).toBe(false));
    // Nothing could be decided, so the app falls back to logged-out — but it does finish loading.
    expect(result.current.isAuthenticated).toBe(false);
  });

  it("spends no refresh on a token that is still good", async () => {
    getAccessToken.mockReturnValue("fresh.jwt");
    isAuthenticated.mockReturnValue(true);
    getCurrentUser.mockReturnValue(ADA);

    const { result } = renderAuth();

    await waitFor(() => expect(result.current.isLoading).toBe(false));
    expect(refreshAccessToken).not.toHaveBeenCalled();
    expect(result.current.user).toEqual(ADA);
  });

  it("does not try to refresh when there is no token at all", async () => {
    // A first-time visitor has no refresh cookie either; asking would just be a wasted 401.
    const { result } = renderAuth();

    await waitFor(() => expect(result.current.isLoading).toBe(false));
    expect(refreshAccessToken).not.toHaveBeenCalled();
    expect(result.current.isAuthenticated).toBe(false);
    expect(result.current.user).toBeNull();
  });
});

describe("AuthProvider — cross-tab session changes", () => {
  it("drops the session when another tab clears the token", async () => {
    getAccessToken.mockReturnValue("fresh.jwt");
    isAuthenticated.mockReturnValue(true);
    getCurrentUser.mockReturnValue(ADA);

    const { result } = renderAuth();
    await waitFor(() => expect(result.current.isAuthenticated).toBe(true));

    // The other tab logged out: the key is gone from storage.
    getCurrentUser.mockReturnValue(null);
    act(() => {
      window.dispatchEvent(new StorageEvent("storage", { key: AUTH_TOKEN_KEY }));
    });

    // This tab must not keep showing an authenticated shell.
    expect(result.current.isAuthenticated).toBe(false);
    expect(result.current.user).toBeNull();
  });

  it("re-reads the session when storage is cleared wholesale (key === null)", async () => {
    getAccessToken.mockReturnValue("fresh.jwt");
    isAuthenticated.mockReturnValue(true);
    getCurrentUser.mockReturnValue(ADA);

    const { result } = renderAuth();
    await waitFor(() => expect(result.current.isAuthenticated).toBe(true));

    getCurrentUser.mockReturnValue(null);
    act(() => {
      window.dispatchEvent(new StorageEvent("storage", { key: null }));
    });

    expect(result.current.isAuthenticated).toBe(false);
  });

  it("ignores storage writes that have nothing to do with the session", async () => {
    getAccessToken.mockReturnValue("fresh.jwt");
    isAuthenticated.mockReturnValue(true);
    getCurrentUser.mockReturnValue(ADA);

    const { result } = renderAuth();
    await waitFor(() => expect(result.current.isAuthenticated).toBe(true));

    // An unrelated key changing must not disturb a live session.
    getCurrentUser.mockReturnValue(null);
    act(() => {
      window.dispatchEvent(new StorageEvent("storage", { key: "theme" }));
    });

    expect(result.current.isAuthenticated).toBe(true);
    expect(result.current.user).toEqual(ADA);
  });

  it("stops listening once unmounted", async () => {
    getAccessToken.mockReturnValue("fresh.jwt");
    isAuthenticated.mockReturnValue(true);
    getCurrentUser.mockReturnValue(ADA);

    const { result, unmount } = renderAuth();
    await waitFor(() => expect(result.current.isAuthenticated).toBe(true));

    unmount();
    getCurrentUser.mockClear();
    window.dispatchEvent(new StorageEvent("storage", { key: AUTH_TOKEN_KEY }));

    // A detached listener re-reading auth state would be a leak, and would warn on setState.
    expect(getCurrentUser).not.toHaveBeenCalled();
  });
});

describe("AuthProvider — login", () => {
  it("reflects the signed-in user once the service accepts the credentials", async () => {
    const { result } = renderAuth();
    await waitFor(() => expect(result.current.isLoading).toBe(false));

    login.mockImplementation(async () => {
      getCurrentUser.mockReturnValue(ADA);
    });

    await act(async () => {
      await result.current.login("ada@x.io", "hunter2");
    });

    expect(login).toHaveBeenCalledWith("ada@x.io", "hunter2");
    expect(result.current.isAuthenticated).toBe(true);
    expect(result.current.user).toEqual(ADA);
  });

  it("propagates a rejected login and leaves the session untouched", async () => {
    const { result } = renderAuth();
    await waitFor(() => expect(result.current.isLoading).toBe(false));

    login.mockRejectedValue(new Error("Invalid email or password"));

    await expect(
      act(async () => {
        await result.current.login("ada@x.io", "wrong");
      }),
    ).rejects.toThrow("Invalid email or password");

    // The caller (the Login screen) is what renders the error; the provider must not swallow it
    // and must certainly not mark the tab authenticated.
    expect(result.current.isAuthenticated).toBe(false);
  });
});

describe("AuthProvider — the query cache does not cross a session boundary", () => {
  it("drops the previous operator's cached data when someone else signs in", async () => {
    const { result } = renderAuth();
    await waitFor(() => expect(result.current.isLoading).toBe(false));
    seedCachedIncidents();

    login.mockImplementation(async () => {
      getCurrentUser.mockReturnValue({ ...ADA, id: "u2", email: "bob@x.io", name: "Bob" });
    });

    await act(async () => {
      await result.current.login("bob@x.io", "hunter2");
    });

    expect(queryClient.getQueryData(["incidents", "list"])).toBeUndefined();
  });

  it("drops it on the way out too", async () => {
    getAccessToken.mockReturnValue("fresh.jwt");
    isAuthenticated.mockReturnValue(true);
    getCurrentUser.mockReturnValue(ADA);

    const { result } = renderAuth();
    await waitFor(() => expect(result.current.isAuthenticated).toBe(true));
    seedCachedIncidents();

    await act(async () => {
      await result.current.logout();
    });

    expect(logout).toHaveBeenCalledTimes(1);
    expect(queryClient.getQueryData(["incidents", "list"])).toBeUndefined();
  });

  it("keeps the cache across a silent token refresh — that is not a session change", async () => {
    getAccessToken.mockReturnValue("stale.jwt");
    isAuthenticated.mockReturnValue(false);
    refreshAccessToken.mockImplementation(async () => {
      getCurrentUser.mockReturnValue(ADA);
    });

    const { result } = renderAuth();
    await waitFor(() => expect(result.current.isAuthenticated).toBe(true));
    seedCachedIncidents();

    // What a refresh in another tab looks like from here: the token key changed, nothing else.
    act(() => {
      window.dispatchEvent(new StorageEvent("storage", { key: AUTH_TOKEN_KEY }));
    });

    expect(queryClient.getQueryData(["incidents", "list"])).toEqual([
      { id: "i1", title: "Ada's incident" },
    ]);
  });
});

describe("AuthProvider — the identity the token was issued with", () => {
  it("adopts the stored name and email once they arrive", async () => {
    getAccessToken.mockReturnValue("fresh.jwt");
    isAuthenticated.mockReturnValue(true);
    getCurrentUser.mockReturnValue(ADA);
    fetchStoredIdentity.mockResolvedValue({ name: "Ada Lovelace", email: "ada.l@x.io" });

    const { result } = renderAuth();

    await waitFor(() => expect(result.current.user?.name).toBe("Ada Lovelace"));
    expect(result.current.user?.email).toBe("ada.l@x.io");
  });

  it("never takes the role from there — the token is what the API authorizes on", async () => {
    getAccessToken.mockReturnValue("fresh.jwt");
    isAuthenticated.mockReturnValue(true);
    getCurrentUser.mockReturnValue(ADA);
    fetchStoredIdentity.mockResolvedValue({ name: "Ada Lovelace", email: "ada.l@x.io", role: "Viewer" });

    const { result } = renderAuth();

    await waitFor(() => expect(result.current.user?.name).toBe("Ada Lovelace"));
    expect(result.current.user?.role).toBe("Admin");
  });

  it("keeps the token's identity when the read comes back empty", async () => {
    getAccessToken.mockReturnValue("fresh.jwt");
    isAuthenticated.mockReturnValue(true);
    getCurrentUser.mockReturnValue(ADA);
    fetchStoredIdentity.mockResolvedValue(null);

    const { result } = renderAuth();

    await waitFor(() => expect(result.current.isLoading).toBe(false));
    expect(result.current.user).toEqual(ADA);
  });

  it("re-reads on demand, so a profile edit shows up without waiting for the token to rotate", async () => {
    getAccessToken.mockReturnValue("fresh.jwt");
    isAuthenticated.mockReturnValue(true);
    getCurrentUser.mockReturnValue(ADA);

    const { result } = renderAuth();
    await waitFor(() => expect(result.current.isLoading).toBe(false));
    expect(result.current.user?.name).toBe("Ada");

    fetchStoredIdentity.mockResolvedValue({ name: "Ada L.", email: ADA.email });
    await act(async () => { await result.current.refreshIdentity(); });

    expect(result.current.user?.name).toBe("Ada L.");
  });
});

describe("useAuth", () => {
  it("fails loudly when used outside an AuthProvider", () => {
    // A silent null here would surface as an unrelated crash deep in a screen.
    expect(() => renderHook(() => useAuth())).toThrow(
      "useAuth must be used within an AuthProvider",
    );
  });
});
