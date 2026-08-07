import "@testing-library/jest-dom/vitest";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Routes, Route } from "react-router";

/** The service editor owns the ack callback config, so these pin that no field is dropped on save —
 * a dropped one looks like an unacknowledged incident, not a settings bug — and that delete confirms. */

const navigate = vi.fn();
vi.mock("react-router", async (importOriginal) => ({
  ...(await importOriginal<typeof import("react-router")>()),
  useNavigate: () => navigate,
}));

const useService = vi.fn();
const updateService = vi.fn();
const deleteService = vi.fn();
const addDependency = vi.fn();
const removeDependency = vi.fn();

vi.mock("../hooks/use-services", () => ({
  useService: (id: string) => useService(id),
  useUpdateService: () => ({ mutate: updateService, isPending: false }),
  useDeleteService: () => ({ mutate: deleteService, isPending: false }),
  useServiceDependencies: () => ({ data: [] }),
  useAddDependency: () => ({ mutate: addDependency, isPending: false }),
  useRemoveDependency: () => ({ mutate: removeDependency, isPending: false }),
  useServices: () => ({ data: [{ id: "svc-2", name: "Billing API" }] }),
}));

vi.mock("@/features/settings/hooks/use-webhook-templates", () => ({
  useWebhookTemplates: () => ({ data: [], isLoading: false, error: null }),
}));

vi.mock("../hooks/use-webhook-settings", () => ({
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

const { ServiceDetail } = await import("./detail");

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
    ackHeaders: '{"X-Token":"abc"}',
    ackPayloadTemplate: '{"id":"{{incidentId}}"}',
    ...over,
  };
}

function renderDetail() {
  return render(
    <MemoryRouter initialEntries={["/services/svc-1"]}>
      <Routes>
        <Route path="/services/:id" element={<ServiceDetail />} />
      </Routes>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  useService.mockReturnValue({ data: service(), isLoading: false, error: null });
});

describe("ServiceDetail — load states", () => {
  it("shows the reason and a way back rather than spinning forever", async () => {
    useService.mockReturnValue({ data: undefined, isLoading: false, error: new Error("nope") });
    renderDetail();

    expect(screen.queryByDisplayValue("Payments API")).not.toBeInTheDocument();
    expect(screen.getByText("nope")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: /Back to Services/i }));
    expect(navigate).toHaveBeenCalledWith("/services");
  });

  it("seeds the form from the stored service", () => {
    renderDetail();
    expect(screen.getByDisplayValue("Payments API")).toBeInTheDocument();
    expect(screen.getByDisplayValue("Handles charges")).toBeInTheDocument();
  });
});

describe("ServiceDetail — save", () => {
  it("sends the edited name against the service id", async () => {
    const user = userEvent.setup();
    renderDetail();

    const name = screen.getByDisplayValue("Payments API");
    await user.clear(name);
    await user.type(name, "Payments v2");
    await user.click(screen.getByRole("button", { name: /Save Changes/i }));

    await waitFor(() => expect(updateService).toHaveBeenCalledTimes(1));
    expect(updateService.mock.calls[0][0]).toMatchObject({
      id: "svc-1",
      data: expect.objectContaining({ name: "Payments v2" }),
    });
  });

  // The ack config round-trips through a JSON string. A field lost on the way out is an ack that
  // never reaches the alerting system.
  it("round-trips the whole ack config, headers included", async () => {
    renderDetail();

    await userEvent.click(screen.getByRole("button", { name: /Save Changes/i }));

    expect(updateService.mock.calls[0][0].data).toMatchObject({
      ackEnabled: true,
      ackUrl: "https://acme.io/ack",
      ackHttpMethod: "POST",
      ackContentType: "application/json",
      ackHeaders: '{"X-Token":"abc"}',
      ackPayloadTemplate: '{"id":"{{incidentId}}"}',
    });
  });

  it("sends no ack headers rather than an empty object when the service has none", async () => {
    useService.mockReturnValue({ data: service({ ackHeaders: undefined }), isLoading: false, error: null });
    renderDetail();

    await userEvent.click(screen.getByRole("button", { name: /Save Changes/i }));

    expect(updateService.mock.calls[0][0].data.ackHeaders).toBeUndefined();
  });

  it("survives a service whose stored ack headers will not parse", async () => {
    useService.mockReturnValue({ data: service({ ackHeaders: "{broken" }), isLoading: false, error: null });
    renderDetail();

    expect(screen.getByDisplayValue("Payments API")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: /Save Changes/i }));
    expect(updateService.mock.calls[0][0].data.ackHeaders).toBeUndefined();
  });
});

describe("ServiceDetail — delete", () => {
  it("does not delete until the dialog is confirmed", async () => {
    const user = userEvent.setup();
    renderDetail();

    await user.click(screen.getByRole("button", { name: /^Delete$/i }));
    expect(deleteService).not.toHaveBeenCalled();

    await user.click(screen.getByRole("button", { name: /Delete Service/i }));
    expect(deleteService).toHaveBeenCalledWith("svc-1", expect.anything());
  });
});
