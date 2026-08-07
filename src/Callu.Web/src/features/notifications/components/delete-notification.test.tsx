import "@testing-library/jest-dom/vitest";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";

const deleteMutate = vi.fn();

const UNREAD = {
  id: "n-1",
  title: "Payment API is down",
  message: "Severity 1",
  type: "incident",
  isRead: false,
  createdAt: "2026-07-20T12:00:00Z",
  timeAgo: "2 min ago",
};

const READ = { ...UNREAD, id: "n-2", title: "Checkout recovered", type: "resolved", isRead: true };

const items = { value: [UNREAD, READ] as unknown[] };

vi.mock("../hooks/use-notifications", () => ({
  useRecentNotifications: () => ({ data: items.value, isLoading: false, error: null }),
  useUnreadCount: () => ({ data: { count: 1 } }),
  useMarkAsRead: () => ({ mutate: vi.fn(), isPending: false }),
  useMarkAllAsRead: () => ({ mutate: vi.fn(), isPending: false }),
  useDeleteNotification: () => ({ mutate: deleteMutate, isPending: false }),
}));

const { NotificationsPage } = await import("./notifications-page");

function renderPage() {
  render(
    <MemoryRouter>
      <NotificationsPage />
    </MemoryRouter>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  items.value = [UNREAD, READ];
});

describe("deleting a notification", () => {
  it("is offered on every notification, read or not", () => {
    renderPage();

    expect(screen.getAllByRole("button", { name: /delete notification/i })).toHaveLength(2);
  });

  it("asks before removing it", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getAllByRole("button", { name: /delete notification/i })[0]);

    expect(deleteMutate).not.toHaveBeenCalled();
    expect(await screen.findByRole("dialog")).toBeInTheDocument();
  });

  it("names the notification in the confirmation, so the wrong one is not deleted", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getAllByRole("button", { name: /delete notification/i })[1]);
    const dialog = await screen.findByRole("dialog");

    expect(within(dialog).getByText(/Checkout recovered/)).toBeInTheDocument();
    expect(within(dialog).queryByText(/Payment API is down/)).not.toBeInTheDocument();
  });

  it("deletes the one the operator picked, once they confirm", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getAllByRole("button", { name: /delete notification/i })[1]);
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: /^delete$/i }));

    await waitFor(() => expect(deleteMutate).toHaveBeenCalled());
    expect(deleteMutate.mock.calls[0][0]).toBe("n-2");
  });

  it("does not delete anything if the operator backs out", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getAllByRole("button", { name: /delete notification/i })[0]);
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: /cancel/i }));

    expect(deleteMutate).not.toHaveBeenCalled();
  });
});
