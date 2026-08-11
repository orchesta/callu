import "@testing-library/jest-dom/vitest";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";

// Locale keys are asserted literally so the tests do not move when translations land.
vi.mock("@/shared/locales/i18n", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/shared/locales/i18n")>();
  return { ...actual, t: (key: string) => key };
});

const updateMutateAsync = vi.fn().mockResolvedValue({});
const idle = { mutate: vi.fn(), mutateAsync: vi.fn().mockResolvedValue({}), isPending: false };

const role = { value: "Admin" };

const ITEM = {
  id: "int-1",
  name: "grafana-prod",
  type: "Webhook",
  description: "prod alerts",
  serviceId: undefined as string | undefined,
  serviceName: undefined as string | undefined,
  teamId: "team-1" as string | undefined,
  teamName: "Payments Team",
  webhookTemplateId: undefined as string | undefined,
  webhookTemplateName: undefined as string | undefined,
  isActive: true,
  webhookEnabled: true,
  listeningMode: true,
  capturedCount: 0,
  hasToken: true,
  webhookUrl: "/api/v1/webhooks/tok-1",
  hasApiKey: true,
  maskedApiKey: "ab***yz",
  hasSignatureSecret: false,
  signatureHeaderName: undefined as string | undefined,
  lastWebhookReceivedAt: undefined as string | undefined,
  webhooksReceivedCount: 3,
  createdAt: "2026-08-01T00:00:00Z",
  updatedAt: undefined as string | undefined,
};

vi.mock("@/features/applications/hooks/use-integrations", () => ({
  useIntegrations: () => ({ data: [ITEM], isLoading: false, error: null }),
  useCreateIntegration: () => idle,
  useUpdateIntegration: () => ({ mutateAsync: updateMutateAsync, isPending: false }),
  useDeleteIntegration: () => idle,
  useRotateIntegrationCredentials: () => idle,
  useBindIntegrationService: () => idle,
}));

vi.mock("@/features/services/hooks/use-services", () => ({
  useServices: () => ({
    data: [{ id: "svc-1", name: "Checkout", type: "Api", status: "Operational" }],
  }),
}));

vi.mock("@/features/settings/hooks/use-webhook-templates", () => ({
  useWebhookTemplates: () => ({ data: [] }),
}));

vi.mock("@/shared/auth/auth.context", () => ({
  useAuth: () => ({ user: { role: role.value } }),
}));

const { ApplicationEndpoints } = await import("./endpoints");

function renderPage() {
  render(
    <MemoryRouter>
      <ApplicationEndpoints />
    </MemoryRouter>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  role.value = "Admin";
});

describe("editing an endpoint", () => {
  it("keeps the team binding on every update", async () => {
    const user = userEvent.setup({ pointerEventsCheck: 0 });
    renderPage();

    await user.click(screen.getByRole("button", { name: "common.edit" }));
    await user.click(screen.getByRole("button", { name: "common.save" }));

    await waitFor(() => expect(updateMutateAsync).toHaveBeenCalledTimes(1));
    expect(updateMutateAsync.mock.calls[0][0]).toMatchObject({
      id: "int-1",
      teamId: "team-1",
    });
  });
});

describe("the create dialog's listening switch", () => {
  it("turns off again when a real service replaces 'no service'", async () => {
    const user = userEvent.setup({ pointerEventsCheck: 0 });
    renderPage();

    await user.click(screen.getByRole("button", { name: "inboundWebhooks.create" }));

    const serviceSelect = screen.getAllByRole("combobox")[0];
    await user.click(serviceSelect);
    await user.click(await screen.findByRole("option", { name: "applications.noServiceCaptureOnly" }));
    expect(screen.getByRole("switch")).toBeChecked();

    await user.click(serviceSelect);
    await user.click(await screen.findByRole("option", { name: "Checkout" }));
    expect(screen.getByRole("switch")).not.toBeChecked();
  });
});

describe("credential rotation", () => {
  it("is visible to admins", () => {
    renderPage();
    expect(screen.getByRole("button", { name: "inboundWebhooks.rotate" })).toBeInTheDocument();
  });

  it("is hidden from team leads", () => {
    role.value = "TeamLead";
    renderPage();
    expect(screen.queryByRole("button", { name: "inboundWebhooks.rotate" })).not.toBeInTheDocument();
  });
});
