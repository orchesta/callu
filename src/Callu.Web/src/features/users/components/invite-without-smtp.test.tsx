import "@testing-library/jest-dom/vitest";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

/** Inviting somebody on an install with no SMTP used to report success and reach nobody. */
// The account is created either way, so the invitee's address is taken from that moment on; the
// only recoverable outcome is handing the admin the link.

const invite = vi.fn();
const resend = vi.fn();
const success = vi.fn();

vi.mock("../hooks/use-users", () => ({
  useUsers: () => ({ data: [{ id: "u1", email: "ada@example.com", emailConfirmed: false, role: "Member" }], isLoading: false }),
  useInviteUser: () => ({ mutate: invite, isPending: false }),
  useResendInvitation: () => ({ mutate: resend, isPending: false }),
  useChangeRole: () => ({ mutate: vi.fn(), isPending: false }),
  useUpdateUser: () => ({ mutate: vi.fn(), isPending: false }),
  useRemoveUser: () => ({ mutate: vi.fn(), isPending: false }),
}));

vi.mock("@/shared/utils/toast", () => ({
  toast: { success, error: vi.fn(), info: vi.fn(), warning: vi.fn() },
}));

const { UsersPage } = await import("./index");

function answerWith(result: Record<string, unknown>) {
  return (_vars: unknown, opts: { onSuccess: (r: unknown) => void }) => opts.onSuccess(result);
}

async function inviteSomebody() {
  const user = userEvent.setup({ delay: null });
  render(<UsersPage />);

  await user.click(screen.getByRole("button", { name: /invite user/i }));
  await user.type(screen.getByPlaceholderText("user@company.com"), "ada@example.com");
  await user.click(screen.getByRole("button", { name: /^send invitation$/i }));
}

describe("inviting somebody when the email cannot go out", () => {
  beforeEach(() => {
    invite.mockReset();
    resend.mockReset();
    success.mockReset();
  });

  it("hands the admin the link instead of claiming the email was sent", async () => {
    invite.mockImplementation(answerWith({
      message: "not emailed",
      emailSent: false,
      inviteLink: "https://callu.example/auth/accept-invitation?email=ada&token=xyz",
    }));

    await inviteSomebody();

    await waitFor(() => expect(screen.getByText(/invitation email was not sent/i)).toBeInTheDocument());
    expect(screen.getByText(/token=xyz/)).toBeInTheDocument();
    expect(success).not.toHaveBeenCalled();
  });

  it("says nothing extra when the email did go out", async () => {
    invite.mockImplementation(answerWith({ message: "Invitation sent", emailSent: true, inviteLink: null }));

    await inviteSomebody();

    await waitFor(() => expect(success).toHaveBeenCalledWith("Invitation sent"));
    expect(screen.queryByText(/invitation email was not sent/i)).not.toBeInTheDocument();
  });

  it("does the same for a resend, naming the user it was for", async () => {
    resend.mockImplementation(answerWith({
      message: "not emailed",
      emailSent: false,
      inviteLink: "https://callu.example/auth/accept-invitation?email=ada&token=abc",
    }));

    const user = userEvent.setup({ delay: null });
    render(<UsersPage />);

    await user.click(screen.getByTitle(/resend invitation/i));

    await waitFor(() => expect(screen.getByText(/invitation email was not sent/i)).toBeInTheDocument());
    const dialog = screen.getByText(/invitation email was not sent/i).closest("div")!.parentElement!;
    expect(dialog).toHaveTextContent("ada@example.com");
  });
});
