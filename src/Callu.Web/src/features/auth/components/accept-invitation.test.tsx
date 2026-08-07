import "@testing-library/jest-dom/vitest";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";

/** AcceptInvitation is the only path an invited teammate has into an install, so these pin the token
 * check, the password rules, the exact submit payload, and that a rejected activation is shown. */

const acceptInvitation = vi.fn();
vi.mock("../api/auth.api", () => ({
  authApi: { acceptInvitation: (...a: unknown[]) => acceptInvitation(...a) },
}));

const { AcceptInvitation } = await import("./accept-invitation");

/** Renders with the given query string on the URL, so the component reads token/email from it. */
function renderAt(query: string) {
  return render(
    <MemoryRouter initialEntries={[`/accept-invitation${query}`]}>
      <AcceptInvitation />
    </MemoryRouter>,
  );
}

const STRONG_PW = "Abcdef123!@#";

async function fillPasswords(user: ReturnType<typeof userEvent.setup>, pw = STRONG_PW, confirm = STRONG_PW) {
  await user.type(screen.getByLabelText(/Create Password/i), pw);
  await user.type(screen.getByLabelText(/Confirm Password/i), confirm);
}

beforeEach(() => {
  vi.clearAllMocks();
  acceptInvitation.mockResolvedValue({});
});

describe("AcceptInvitation — link validity", () => {
  it("rejects a link with no token instead of showing an unusable form", () => {
    renderAt("?email=ada@acme.io");
    expect(screen.getByText(/Invalid Invitation/i)).toBeInTheDocument();
    expect(screen.queryByLabelText(/Create Password/i)).not.toBeInTheDocument();
  });

  it("shows the form and the invited email when the token is present", () => {
    renderAt("?token=tok-1&email=ada@acme.io");
    expect(screen.getByLabelText(/Create Password/i)).toBeInTheDocument();
    expect(screen.getByText("ada@acme.io")).toBeInTheDocument();
  });
});

describe("AcceptInvitation — password gate", () => {
  it("keeps activate disabled until every rule passes", async () => {
    const user = userEvent.setup();
    renderAt("?token=tok-1&email=ada@acme.io");

    const activate = screen.getByRole("button", { name: /Activate Account/i });
    expect(activate).toBeDisabled();

    // Strong but mismatched — the match rule still fails, so it stays disabled.
    await fillPasswords(user, STRONG_PW, STRONG_PW + "x");
    expect(activate).toBeDisabled();

    await user.clear(screen.getByLabelText(/Confirm Password/i));
    await user.type(screen.getByLabelText(/Confirm Password/i), STRONG_PW);
    expect(activate).toBeEnabled();
  });

  it("does not call the API for a weak password", async () => {
    const user = userEvent.setup();
    renderAt("?token=tok-1&email=ada@acme.io");

    await fillPasswords(user, "weak", "weak");
    // Disabled, so a click cannot fire; assert both the state and that nothing was sent.
    expect(screen.getByRole("button", { name: /Activate Account/i })).toBeDisabled();
    expect(acceptInvitation).not.toHaveBeenCalled();
  });
});

describe("AcceptInvitation — submit", () => {
  it("activates with the email, token and password from the link", async () => {
    const user = userEvent.setup();
    renderAt("?token=tok-1&email=ada@acme.io");

    await fillPasswords(user);
    await user.click(screen.getByRole("button", { name: /Activate Account/i }));

    await waitFor(() => expect(screen.getByText(/Account Activated/i)).toBeInTheDocument());
    expect(acceptInvitation).toHaveBeenCalledWith("ada@acme.io", "tok-1", STRONG_PW);
  });

  // A rejected activation that showed the success screen would tell an invited user their account
  // is ready when it is not.
  it("shows the reason and not the success screen when activation is rejected", async () => {
    acceptInvitation.mockRejectedValue(new Error("This invitation has expired"));
    const user = userEvent.setup();
    renderAt("?token=tok-1&email=ada@acme.io");

    await fillPasswords(user);
    await user.click(screen.getByRole("button", { name: /Activate Account/i }));

    await waitFor(() => expect(screen.getByText("This invitation has expired")).toBeInTheDocument());
    expect(screen.queryByText(/Account Activated/i)).not.toBeInTheDocument();
  });
});
