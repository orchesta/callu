import "@testing-library/jest-dom/vitest";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Routes, Route } from "react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

/** Pins the event-flags hydration (null shows the legacy acknowledge+resolve pair), that an untouched
 * form never overwrites the stored events or secret, and the manual-action create payload. */

const useService = vi.fn();
const updateService = vi.fn();

vi.mock("../../hooks/use-services", () => ({
  useService: (id: string) => useService(id),
  useUpdateService: () => ({ mutate: updateService, isPending: false }),
  useDeleteService: () => ({ mutate: vi.fn(), isPending: false }),
  useServiceDependencies: () => ({ data: [] }),
  useAddDependency: () => ({ mutate: vi.fn(), isPending: false }),
  useRemoveDependency: () => ({ mutate: vi.fn(), isPending: false }),
  useServices: () => ({ data: [] }),
}));

vi.mock("@/features/settings/hooks/use-webhook-templates", () => ({
  useWebhookTemplates: () => ({ data: [], isLoading: false, error: null }),
}));

vi.mock("../../hooks/use-webhook-settings", () => ({
  useWebhookSettings: () => ({ data: undefined, isLoading: false }),
  useDisableWebhook: () => ({ mutate: vi.fn(), isPending: false }),
  useSetWebhookTemplate: () => ({ mutate: vi.fn(), isPending: false }),
  useRegenerateToken: () => ({ mutate: vi.fn(), isPending: false }),
  useRegenerateApiKey: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useToggleListeningMode: () => ({ mutate: vi.fn(), isPending: false }),
  useSetSignature: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useClearSignature: () => ({ mutate: vi.fn(), isPending: false }),
}));

vi.mock("@/features/teams/hooks/use-teams", () => ({
  useTeams: () => ({ data: [{ id: "team-1", name: "DB team" }] }),
}));

const listActions = vi.fn();
const createAction = vi.fn();
const updateAction = vi.fn();
const removeAction = vi.fn();

vi.mock("../../api/service-actions.api", () => ({
  serviceActionsApi: {
    list: (serviceId: string) => listActions(serviceId),
    create: (serviceId: string, data: unknown) => createAction(serviceId, data),
    update: (serviceId: string, actionId: string, data: unknown) =>
      updateAction(serviceId, actionId, data),
    remove: (serviceId: string, actionId: string) => removeAction(serviceId, actionId),
  },
}));

const { ServiceDetail } = await import("../detail");

function service(over: Record<string, unknown> = {}) {
  return {
    id: "svc-1",
    name: "Payments API",
    description: "Handles charges",
    type: "Api",
    environment: "production",
    status: "Operational",
    teamId: "team-1",
    uptime: 99.95,
    isPublic: true,
    displayOrder: 1,
    incidentCount: 0,
    createdAt: "2026-01-01T00:00:00Z",
    ackEnabled: true,
    ackUrl: "https://acme.io/ack",
    ackHttpMethod: "POST",
    ackContentType: "application/json",
    ackHeaders: undefined,
    ackPayloadTemplate: "",
    ackEvents: null,
    hasAckSecret: false,
    ackSignatureHeader: null,
    ...over,
  };
}

function renderDetail() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={["/services/svc-1"]}>
        <Routes>
          <Route path="/services/:id" element={<ServiceDetail />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

async function openActionsTab(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole("tab", { name: /^Actions$/i }));
}

beforeEach(() => {
  vi.clearAllMocks();
  useService.mockReturnValue({ data: service(), isLoading: false, error: null });
  listActions.mockResolvedValue({ success: true, data: [] });
  createAction.mockResolvedValue({
    success: true,
    data: {
      id: "act-1", serviceId: "svc-1", name: "Restart pods", url: "https://ops.acme.io/restart",
      httpMethod: "POST", contentType: "application/json", hasSecret: false, isEnabled: true,
      displayOrder: 0, createdAt: "2026-08-01T00:00:00Z",
    },
  });
});

describe("Actions tab — event flags", () => {
  it("shows the legacy acknowledge+resolve pair when the stored value is null", async () => {
    const user = userEvent.setup();
    renderDetail();
    await openActionsTab(user);

    expect(screen.getByRole("checkbox", { name: "Acknowledged" })).toBeChecked();
    expect(screen.getByRole("checkbox", { name: "Resolved" })).toBeChecked();
    expect(screen.getByRole("checkbox", { name: "Created" })).not.toBeChecked();
    expect(screen.getByRole("checkbox", { name: "Closed" })).not.toBeChecked();
    expect(screen.getByRole("checkbox", { name: "Reopened" })).not.toBeChecked();
  });

  it("saves the concrete flags once a checkbox is toggled", async () => {
    const user = userEvent.setup();
    renderDetail();
    await openActionsTab(user);

    await user.click(screen.getByRole("checkbox", { name: "Created" }));
    await user.click(screen.getByRole("button", { name: /Save Changes/i }));

    await waitFor(() => expect(updateService).toHaveBeenCalledTimes(1));
    expect(updateService.mock.calls[0][0].data.ackEvents).toBe(7);
  });

  it("sends neither events nor secret when the form was never touched", async () => {
    renderDetail();

    await userEvent.click(screen.getByRole("button", { name: /Save Changes/i }));

    await waitFor(() => expect(updateService).toHaveBeenCalledTimes(1));
    expect(updateService.mock.calls[0][0].data.ackEvents).toBeUndefined();
    expect(updateService.mock.calls[0][0].data.ackSecret).toBeUndefined();
  });
});

describe("Actions tab — clearing fields", () => {
  it("sends the cleared callback URL as an empty string, not undefined", async () => {
    const user = userEvent.setup();
    renderDetail();
    await openActionsTab(user);

    await user.clear(screen.getByDisplayValue("https://acme.io/ack"));
    await user.click(screen.getByRole("button", { name: /Save Changes/i }));

    await waitFor(() => expect(updateService).toHaveBeenCalledTimes(1));
    expect(updateService.mock.calls[0][0].data.ackUrl).toBe("");
  });

  it("sends ackSecret as an empty string only after the explicit clear", async () => {
    useService.mockReturnValue({ data: service({ hasAckSecret: true }), isLoading: false, error: null });
    const user = userEvent.setup();
    renderDetail();
    await openActionsTab(user);

    await user.click(screen.getByRole("button", { name: "Clear secret" }));
    await user.click(screen.getByRole("button", { name: /Save Changes/i }));

    await waitFor(() => expect(updateService).toHaveBeenCalledTimes(1));
    expect(updateService.mock.calls[0][0].data.ackSecret).toBe("");
  });
});

describe("Actions tab — manual actions", () => {
  it("creates an action from the dialog with the defaults it shows", async () => {
    const user = userEvent.setup();
    renderDetail();
    await openActionsTab(user);

    expect(await screen.findByText("No manual actions yet")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /Add action/i }));
    await user.type(screen.getByLabelText("Name"), "Restart pods");
    await user.type(screen.getByLabelText("URL"), "https://ops.acme.io/restart");
    await user.click(screen.getByRole("button", { name: /^Save$/i }));

    await waitFor(() => expect(createAction).toHaveBeenCalledTimes(1));
    expect(createAction.mock.calls[0][0]).toBe("svc-1");
    expect(createAction.mock.calls[0][1]).toEqual({
      name: "Restart pods",
      description: undefined,
      url: "https://ops.acme.io/restart",
      httpMethod: "POST",
      contentType: "application/json",
      headersJson: undefined,
      payloadTemplate: undefined,
      secret: undefined,
      isEnabled: true,
      displayOrder: 0,
    });
  });
});

describe("Actions tab — manual action edit", () => {
  beforeEach(() => {
    listActions.mockResolvedValue({
      success: true,
      data: [{
        id: "act-9", serviceId: "svc-1", name: "Rotate cache", url: "https://ops.acme.io/rotate",
        httpMethod: "POST", contentType: "application/json", hasSecret: true, isEnabled: true,
        displayOrder: 0, createdAt: "2026-08-01T00:00:00Z",
      }],
    });
    updateAction.mockResolvedValue({ success: true, data: {} });
  });

  it("omits the secret from the update payload when it was not touched", async () => {
    const user = userEvent.setup();
    renderDetail();
    await openActionsTab(user);

    await user.click(await screen.findByRole("button", { name: "Edit action" }));
    await user.click(screen.getByRole("button", { name: /^Save$/i }));

    await waitFor(() => expect(updateAction).toHaveBeenCalledTimes(1));
    expect(updateAction.mock.calls[0][0]).toBe("svc-1");
    expect(updateAction.mock.calls[0][1]).toBe("act-9");
    expect(updateAction.mock.calls[0][2]).not.toHaveProperty("secret");
  });

  it("sends an empty secret only after the explicit clear", async () => {
    const user = userEvent.setup();
    renderDetail();
    await openActionsTab(user);

    await user.click(await screen.findByRole("button", { name: "Edit action" }));
    await user.click(await screen.findByRole("button", { name: "Clear secret" }));
    await user.click(screen.getByRole("button", { name: /^Save$/i }));

    await waitFor(() => expect(updateAction).toHaveBeenCalledTimes(1));
    expect(updateAction.mock.calls[0][2].secret).toBe("");
  });

  it("sends the typed replacement value as the secret", async () => {
    const user = userEvent.setup();
    renderDetail();
    await openActionsTab(user);

    await user.click(await screen.findByRole("button", { name: "Edit action" }));
    await user.type(screen.getByLabelText("Signing secret"), "new-secret");
    await user.click(screen.getByRole("button", { name: /^Save$/i }));

    await waitFor(() => expect(updateAction).toHaveBeenCalledTimes(1));
    expect(updateAction.mock.calls[0][2].secret).toBe("new-secret");
  });
});
