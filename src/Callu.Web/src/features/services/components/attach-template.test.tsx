import "@testing-library/jest-dom/vitest";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { Tabs } from "@/shared/components/ui/tabs";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

// Radix Select does not commit a choice under jsdom, so what the picker sends is not asserted
// here; what it offers and what it reflects are.
const setTemplateMutate = vi.fn();
const idle = { mutate: vi.fn(), isPending: false, data: undefined };

const TEMPLATES = [
  { id: "tpl-1", name: "Grafana v11", fieldMappings: "{}", dataLanguage: "en", isBuiltIn: false, isActive: true, usageCount: 0 },
  { id: "tpl-2", name: "Alertmanager", fieldMappings: "{}", dataLanguage: "en", isBuiltIn: true, isActive: true, usageCount: 1 },
];

vi.mock("../../hooks/use-webhook-settings", () => ({
  useDisableWebhook: () => idle,
  useSetWebhookTemplate: () => ({ mutate: setTemplateMutate, isPending: false }),
  useRegenerateToken: () => idle,
  useRegenerateApiKey: () => idle,
  useToggleListeningMode: () => idle,
  useSetSignature: () => idle,
  useClearSignature: () => idle,
}));

vi.mock("@/features/settings/hooks/use-webhook-templates", () => ({
  useWebhookTemplates: () => ({ data: TEMPLATES, isLoading: false, error: null }),
}));

const { WebhooksTab } = await import("./detail/webhooks-tab");

const SETTINGS = {
  serviceId: "svc-1",
  webhookEnabled: true,
  webhookUrl: "/api/v1/webhooks/tok",
  hasApiKey: false,
  listeningMode: false,
  webhooksReceivedCount: 0,
  capturedCount: 0,
  templateId: undefined as string | undefined,
  templateName: undefined as string | undefined,
};

const settings = { value: SETTINGS as unknown };

function renderTab() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
    <MemoryRouter>
      <Tabs defaultValue="webhooks">
        <WebhooksTab
          serviceId="svc-1"
          webhookSettings={settings.value as never}
          isWebhookLoading={false}
          incidentCount={0}
        />
      </Tabs>
    </MemoryRouter>
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  settings.value = SETTINGS;
});

describe("pointing a service at a saved template", () => {
  /** Templates were only reachable by writing a new one per service; an existing one could not be
   * picked, so the same mapping had to be re-entered for every service that needed it. */
  it("offers every saved template", async () => {
    const user = userEvent.setup({ pointerEventsCheck: 0 });
    renderTab();

    await user.click(screen.getByRole("combobox", { name: /webhook template for this service/i }));

    expect(await screen.findByRole("option", { name: "Grafana v11" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Alertmanager" })).toBeInTheDocument();
  });

  /** Going back to the default parsing has to be expressible, not just "pick a different one". */
  it("offers taking the template back off", async () => {
    const user = userEvent.setup({ pointerEventsCheck: 0 });
    settings.value = { ...SETTINGS, templateId: "tpl-1", templateName: "Grafana v11" };
    renderTab();

    await user.click(screen.getByRole("combobox", { name: /webhook template for this service/i }));

    expect(await screen.findByRole("option", { name: /none/i })).toBeInTheDocument();
  });

  it("shows the template the service is on now", () => {
    settings.value = { ...SETTINGS, templateId: "tpl-1", templateName: "Grafana v11" };
    renderTab();

    expect(screen.getByRole("combobox", { name: /webhook template for this service/i }))
      .toHaveTextContent("Grafana v11");
  });

  it("shows the default when the service is on no template", () => {
    renderTab();

    expect(screen.getByRole("combobox", { name: /webhook template for this service/i }))
      .toHaveTextContent(/none/i);
  });
});
