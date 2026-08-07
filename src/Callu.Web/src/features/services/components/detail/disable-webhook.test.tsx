import "@testing-library/jest-dom/vitest";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { Tabs } from "@/shared/components/ui/tabs";

const disableMutate = vi.fn();
const idle = { mutate: vi.fn(), isPending: false, data: undefined };

vi.mock("@/features/settings/hooks/use-webhook-templates", () => ({
  useWebhookTemplates: () => ({ data: [], isLoading: false, error: null }),
}));

vi.mock("../../hooks/use-webhook-settings", () => ({
  useDisableWebhook: () => ({ mutate: disableMutate, isPending: false }),
  useSetWebhookTemplate: () => idle,
  useRegenerateToken: () => idle,
  useRegenerateApiKey: () => idle,
  useToggleListeningMode: () => idle,
  useSetSignature: () => idle,
  useClearSignature: () => idle,
}));

const { WebhooksTab } = await import("./webhooks-tab");

const ENABLED = {
  serviceId: "svc-1",
  webhookEnabled: true,
  webhookUrl: "/api/v1/webhooks/tok",
  hasApiKey: false,
  listeningMode: false,
  webhooksReceivedCount: 12,
  capturedCount: 0,
};

const settings = { value: ENABLED as unknown };

function renderTab() {
  render(
    <MemoryRouter>
      <Tabs defaultValue="webhooks">
        <WebhooksTab
          serviceId="svc-1"
          webhookSettings={settings.value as never}
          isWebhookLoading={false}
          incidentCount={0}
        />
      </Tabs>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  settings.value = ENABLED;
});

describe("turning a service webhook off", () => {
  it("is offered while the webhook is live", () => {
    renderTab();

    expect(screen.getByRole("button", { name: /turn off this webhook/i })).toBeInTheDocument();
  });

  it("is not offered when there is no webhook to turn off", () => {
    settings.value = { ...ENABLED, webhookEnabled: false, webhookUrl: undefined };
    renderTab();

    expect(screen.queryByRole("button", { name: /turn off this webhook/i })).not.toBeInTheDocument();
  });

  /** The URL is live and public; nothing about turning it off is reversible. */
  it("says the old URL never comes back", async () => {
    const user = userEvent.setup();
    renderTab();

    await user.click(screen.getByRole("button", { name: /turn off this webhook/i }));
    const dialog = await screen.findByRole("dialog");

    expect(within(dialog).getByText(/the old one never works again/i)).toBeInTheDocument();
    expect(disableMutate).not.toHaveBeenCalled();
  });

  it("turns it off once the operator confirms", async () => {
    const user = userEvent.setup();
    renderTab();

    await user.click(screen.getByRole("button", { name: /turn off this webhook/i }));
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: /^turn off$/i }));

    await waitFor(() => expect(disableMutate).toHaveBeenCalled());
    expect(disableMutate.mock.calls[0][0]).toBe("svc-1");
  });
});
