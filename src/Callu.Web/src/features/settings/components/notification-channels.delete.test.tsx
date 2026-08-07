import "@testing-library/jest-dom/vitest";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

/** Deleting a channel stops incidents reaching that destination and takes its delivery history
 * with it. It used to fire on one click of an icon that had no accessible name at all. */

const deleteMutate = vi.fn();
const channels = [
  {
    id: "ch-1",
    name: "#incidents — Slack",
    channelType: "Slack",
    isEnabled: true,
    configuration: {},
    serviceFilter: [],
    eventCreated: true,
    eventAcknowledged: false,
    eventResolved: false,
    eventClosed: false,
    eventReopened: false,
    sentCount: 0,
  },
];

vi.mock("../hooks/use-notification-channels", () => ({
  useNotificationChannels: () => ({ data: channels, isLoading: false, isError: false }),
  useChannelTypes: () => ({ data: [{ value: "Slack", label: "Slack", icon: "💬", fields: [] }] }),
  useSeverityOptions: () => ({ data: [] }),
  useCreateNotificationChannel: () => ({ mutate: vi.fn(), isPending: false }),
  useUpdateNotificationChannel: () => ({ mutate: vi.fn(), isPending: false }),
  useDeleteNotificationChannel: () => ({ mutate: deleteMutate, isPending: false }),
  useTestNotificationChannel: () => ({ mutate: vi.fn(), isPending: false }),
  useToggleNotificationChannel: () => ({ mutate: vi.fn(), isPending: false }),
  useChannelDeliveries: () => ({ data: { items: [], totalCount: 0 }, isLoading: false }),
}));

vi.mock("@/features/services/hooks/use-services", () => ({
  useServices: () => ({ data: [] }),
}));

const { NotificationChannelsSettings } = await import("./notification-channels");

beforeEach(() => vi.clearAllMocks());

describe("deleting a notification channel", () => {
  it("names every icon-only action, so none of them is an anonymous button", async () => {
    render(<NotificationChannelsSettings />);

    // Each has to say which channel it acts on — three identical unnamed buttons is what shipped.
    expect(screen.getByRole("button", { name: /test message to .*Slack/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /edit .*Slack/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /delete .*Slack/i })).toBeInTheDocument();
  });

  it("asks before deleting instead of deleting on the click", async () => {
    const user = userEvent.setup();
    render(<NotificationChannelsSettings />);

    await user.click(screen.getByRole("button", { name: /delete .*Slack/i }));

    // The click opens a question; nothing is deleted yet.
    expect(deleteMutate).not.toHaveBeenCalled();
    expect(await screen.findByText(/delete channel/i)).toBeInTheDocument();
  });

  it("deletes only after the confirmation is accepted", async () => {
    const user = userEvent.setup();
    render(<NotificationChannelsSettings />);

    await user.click(screen.getByRole("button", { name: /delete .*Slack/i }));
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: /^delete$/i }));

    await waitFor(() => expect(deleteMutate).toHaveBeenCalledTimes(1));
    expect(deleteMutate.mock.calls[0][0]).toBe("ch-1");
  });

  it("deletes nothing when the confirmation is dismissed", async () => {
    const user = userEvent.setup();
    render(<NotificationChannelsSettings />);

    await user.click(screen.getByRole("button", { name: /delete .*Slack/i }));
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: /cancel/i }));

    expect(deleteMutate).not.toHaveBeenCalled();
  });
});
