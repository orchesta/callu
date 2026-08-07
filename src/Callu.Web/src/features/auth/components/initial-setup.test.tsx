import "@testing-library/jest-dom/vitest";
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

/** A fresh install has no other way to create its first admin, so these pin the already-set-up
 * redirect, the password rules, the submit payload, and that a rejection is not shown as success. */

// Fresh per test: the success path leaves a live 2s setTimeout(navigate) a shared spy would record.
let navigate = vi.fn();
vi.mock("react-router", async (importOriginal) => ({
  ...(await importOriginal<typeof import("react-router")>()),
  useNavigate: () => navigate,
}));

const login = vi.fn();
vi.mock("@/shared/auth/auth.service", () => ({
  authService: { login: (...a: unknown[]) => login(...a) },
}));

vi.mock("@/shared/config", () => ({ API_URL: "http://test" }));

const { InitialSetup } = await import("./initial-setup");

/** Records every fetch call and lets a test script the response per URL suffix. */
function mockFetch(handlers: Record<string, () => Response | Promise<Response>>) {
  const calls: { url: string; body: unknown }[] = [];
  const fn = vi.fn(async (url: string, init?: RequestInit) => {
    calls.push({ url, body: init?.body ? JSON.parse(init.body as string) : undefined });
    for (const [suffix, handler] of Object.entries(handlers)) {
      if (url.endsWith(suffix)) return handler();
    }
    return new Response("{}", { status: 200 });
  });
  vi.stubGlobal("fetch", fn);
  return calls;
}

const json = (data: unknown, status = 200) =>
  new Response(JSON.stringify(data), { status, headers: { "Content-Type": "application/json" } });

/** Default: setup is required (no admin yet) and one timezone is offered. */
function defaultHandlers() {
  return {
    "/setup/status": () => json({ data: { setupRequired: true } }),
    "/settings/localization/timezones": () => json({ data: [{ id: "Europe/Istanbul", displayName: "(UTC+03:00) Istanbul" }] }),
    "/setup/initial": () => json({ data: { ok: true } }),
  };
}

const STRONG_PW = "Abcdef123!@#";

beforeEach(() => {
  vi.clearAllMocks();
  navigate = vi.fn();
  login.mockResolvedValue({ id: "admin-1" });
});

afterEach(() => {
  vi.unstubAllGlobals();
});

async function fillStep1(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText(/Full Name/i), "Ada Admin");
  await user.type(screen.getByLabelText(/Email Address/i), "ada@acme.io");
  await user.click(screen.getByRole("button", { name: /Continue/i }));
}

async function fillStep2(user: ReturnType<typeof userEvent.setup>, pw = STRONG_PW, confirm = STRONG_PW) {
  await user.type(screen.getByLabelText(/^Password/i), pw);
  await user.type(screen.getByLabelText(/Confirm Password/i), confirm);
}

describe("InitialSetup — already-set-up guard", () => {
  it("redirects to login when an admin already exists", async () => {
    mockFetch({ ...defaultHandlers(), "/setup/status": () => json({ data: { setupRequired: false } }) });
    render(<InitialSetup />);
    await waitFor(() => expect(navigate).toHaveBeenCalledWith("/login"));
  });

  it("stays on the wizard when setup is still required", async () => {
    mockFetch(defaultHandlers());
    render(<InitialSetup />);
    await waitFor(() => expect(screen.getByLabelText(/Full Name/i)).toBeInTheDocument());
    expect(navigate).not.toHaveBeenCalled();
  });
});

describe("InitialSetup — step 1 validation", () => {
  it("will not advance without a name", async () => {
    mockFetch(defaultHandlers());
    const user = userEvent.setup();
    render(<InitialSetup />);
    await waitFor(() => screen.getByLabelText(/Email Address/i));

    await user.type(screen.getByLabelText(/Email Address/i), "ada@acme.io");
    await user.click(screen.getByRole("button", { name: /Continue/i }));

    expect(screen.getByText(/Full name is required/i)).toBeInTheDocument();
    expect(screen.queryByLabelText(/^Password/i)).not.toBeInTheDocument();
  });

  it("will not advance with a malformed email", async () => {
    mockFetch(defaultHandlers());
    const user = userEvent.setup();
    render(<InitialSetup />);
    await waitFor(() => screen.getByLabelText(/Full Name/i));

    await user.type(screen.getByLabelText(/Full Name/i), "Ada");
    await user.type(screen.getByLabelText(/Email Address/i), "not-an-email");
    await user.click(screen.getByRole("button", { name: /Continue/i }));

    expect(screen.getByText(/valid email address is required/i)).toBeInTheDocument();
  });
});

describe("InitialSetup — step 2 password rules", () => {
  it("will not submit a password that misses a requirement", async () => {
    const calls = mockFetch(defaultHandlers());
    const user = userEvent.setup();
    render(<InitialSetup />);
    await waitFor(() => screen.getByLabelText(/Full Name/i));
    await fillStep1(user);

    // 11 chars, no uppercase, no special → fails length, uppercase, special.
    await fillStep2(user, "abcdef12345", "abcdef12345");
    await user.click(screen.getByRole("button", { name: /Complete Setup/i }));

    expect(screen.getByText(/meet all password requirements/i)).toBeInTheDocument();
    expect(calls.some((c) => c.url.endsWith("/setup/initial"))).toBe(false);
  });

  it("will not submit when the confirmation does not match", async () => {
    const calls = mockFetch(defaultHandlers());
    const user = userEvent.setup();
    render(<InitialSetup />);
    await waitFor(() => screen.getByLabelText(/Full Name/i));
    await fillStep1(user);

    await fillStep2(user, STRONG_PW, STRONG_PW + "x");
    await user.click(screen.getByRole("button", { name: /Complete Setup/i }));

    expect(screen.getByText(/Passwords do not match/i)).toBeInTheDocument();
    expect(calls.some((c) => c.url.endsWith("/setup/initial"))).toBe(false);
  });
});

describe("InitialSetup — submit", () => {
  it("POSTs the admin payload, then logs the new admin in", async () => {
    const calls = mockFetch(defaultHandlers());
    const user = userEvent.setup();
    render(<InitialSetup />);
    await waitFor(() => screen.getByLabelText(/Full Name/i));
    await fillStep1(user);
    await fillStep2(user);
    await user.click(screen.getByRole("button", { name: /Complete Setup/i }));

    await waitFor(() => expect(login).toHaveBeenCalledWith("ada@acme.io", STRONG_PW));

    const post = calls.find((c) => c.url.endsWith("/setup/initial"));
    expect(post?.body).toEqual({
      email: "ada@acme.io",
      password: STRONG_PW,
      name: "Ada Admin",
      defaultTimezone: "Europe/Istanbul",
    });
  });

  // A failed setup that logged the user in or advanced to the success step would strand a fresh
  // install: no admin created, but the UI says done.
  it("surfaces the server's reason and does not log in when setup is rejected", async () => {
    mockFetch({
      ...defaultHandlers(),
      "/setup/initial": () => json({ message: "Email already registered" }, 400),
    });
    const user = userEvent.setup();
    render(<InitialSetup />);
    await waitFor(() => screen.getByLabelText(/Full Name/i));
    await fillStep1(user);
    await fillStep2(user);
    await user.click(screen.getByRole("button", { name: /Complete Setup/i }));

    await waitFor(() => expect(screen.getByText("Email already registered")).toBeInTheDocument());
    expect(login).not.toHaveBeenCalled();
    expect(navigate).not.toHaveBeenCalledWith("/dashboard");
  });
});
