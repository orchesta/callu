import "@testing-library/jest-dom/vitest";
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";

/** Two load-bearing behaviours: the first-run probe that redirects a fresh install to initial setup
 * (and falls through to the form when unreachable), and a rejected login that does not navigate. */

const navigate = vi.fn();
vi.mock("react-router", async (importOriginal) => ({
  ...(await importOriginal<typeof import("react-router")>()),
  useNavigate: () => navigate,
}));

const login = vi.fn();
vi.mock("@/shared/auth/auth.context", () => ({
  useAuth: () => ({ login }),
}));

const { Login } = await import("./login");

/** Whatever GET /api/v1/setup/status returns for this test. */
function mockSetupStatus(body: unknown, ok = true) {
  vi.stubGlobal(
    "fetch",
    vi.fn().mockResolvedValue({ ok, json: () => Promise.resolve(body) }),
  );
}

function renderLogin() {
  return render(
    <MemoryRouter>
      <Login />
    </MemoryRouter>,
  );
}

/** The form only renders once the setup probe has settled. */
async function loginForm() {
  return screen.findByPlaceholderText("you@company.com");
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.useRealTimers();
  mockSetupStatus({ data: { setupRequired: false } });
  login.mockResolvedValue(undefined);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("Login — first-run detection", () => {
  it("sends a fresh install to the admin-creation screen instead of an unusable form", async () => {
    mockSetupStatus({ data: { setupRequired: true } });
    renderLogin();

    await waitFor(() => expect(navigate).toHaveBeenCalledWith("/auth/initial-setup"));
  });

  it("reads setupRequired whether or not the API wraps it in the ApiResponse envelope", async () => {
    // /setup/status is reachable before auth and has shipped both shapes; the screen reads
    // `json.data?.setupRequired ?? json.setupRequired`. Unwrapped must work too.
    mockSetupStatus({ setupRequired: true });
    renderLogin();

    await waitFor(() => expect(navigate).toHaveBeenCalledWith("/auth/initial-setup"));
  });

  it("shows the login form, not a redirect, once an admin exists", async () => {
    mockSetupStatus({ data: { setupRequired: false } });
    renderLogin();

    await loginForm();
    expect(navigate).not.toHaveBeenCalled();
  });

  it("falls through to the form when the setup probe is unreachable, rather than spinning", async () => {
    // The catch is empty by design, but the finally must still clear `checkingSetup` — otherwise
    // a flaky probe leaves an install with a permanent spinner and no way in.
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new Error("ECONNREFUSED")));
    renderLogin();

    expect(await loginForm()).toBeInTheDocument();
    expect(navigate).not.toHaveBeenCalled();
  });

  it("falls through to the form when the probe answers non-OK", async () => {
    mockSetupStatus({}, false);
    renderLogin();

    expect(await loginForm()).toBeInTheDocument();
    expect(navigate).not.toHaveBeenCalled();
  });
});

describe("Login — signing in", () => {
  it("passes the typed credentials through and lands on the dashboard", async () => {
    const user = userEvent.setup();
    renderLogin();
    await loginForm();

    await user.type(screen.getByPlaceholderText("you@company.com"), "ada@x.io");
    await user.type(screen.getByPlaceholderText("••••••••"), "hunter2");
    await user.click(screen.getByRole("button", { name: "Sign In" }));

    await waitFor(() => expect(login).toHaveBeenCalledWith("ada@x.io", "hunter2"));
    expect(await screen.findByText(/Login successful/i)).toBeInTheDocument();
    // The redirect is deferred behind a 500ms timer so the success line is readable.
    await waitFor(() => expect(navigate).toHaveBeenCalledWith("/dashboard"), { timeout: 2000 });
  });

  it("shows the reason a rejected sign-in gave and does NOT navigate", async () => {
    const user = userEvent.setup();
    login.mockRejectedValue(new Error("Invalid email or password. Please try again."));
    renderLogin();
    await loginForm();

    await user.type(screen.getByPlaceholderText("you@company.com"), "ada@x.io");
    await user.type(screen.getByPlaceholderText("••••••••"), "wrong");
    await user.click(screen.getByRole("button", { name: "Sign In" }));

    expect(await screen.findByText(/Invalid email or password/i)).toBeInTheDocument();
    // Never past the door on a failed login.
    expect(navigate).not.toHaveBeenCalled();
    expect(screen.queryByText(/Login successful/i)).toBeNull();
  });

  it("re-enables the button after a failure, so the user can correct a typo and retry", async () => {
    const user = userEvent.setup();
    login.mockRejectedValueOnce(new Error("Invalid email or password. Please try again."));
    renderLogin();
    await loginForm();

    await user.type(screen.getByPlaceholderText("you@company.com"), "ada@x.io");
    await user.type(screen.getByPlaceholderText("••••••••"), "wrong");
    await user.click(screen.getByRole("button", { name: "Sign In" }));
    await screen.findByText(/Invalid email or password/i);

    const submit = screen.getByRole("button", { name: "Sign In" });
    expect(submit).toBeEnabled();

    // The retry succeeds and gets through.
    await user.click(submit);
    await waitFor(() => expect(login).toHaveBeenCalledTimes(2));
    expect(await screen.findByText(/Login successful/i)).toBeInTheDocument();
  });
});

describe("Login — password visibility", () => {
  it("keeps the password masked until the reveal is pressed", async () => {
    const user = userEvent.setup();
    renderLogin();
    await loginForm();

    const password = screen.getByPlaceholderText("••••••••");
    expect(password).toHaveAttribute("type", "password");

    // The reveal is the only unlabelled button inside the form's password field.
    const reveal = screen
      .getAllByRole("button")
      .find((b) => b.getAttribute("type") === "button" && b.textContent === "");
    if (!reveal) throw new Error("password reveal button not found");

    await user.click(reveal);
    expect(password).toHaveAttribute("type", "text");

    await user.click(reveal);
    expect(password).toHaveAttribute("type", "password");
  });
});
