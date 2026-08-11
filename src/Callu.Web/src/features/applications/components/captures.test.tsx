import "@testing-library/jest-dom/vitest";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

// Locale keys are asserted literally so the tests do not move when translations land.
vi.mock("@/shared/locales/i18n", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/shared/locales/i18n")>();
  return { ...actual, t: (key: string) => key };
});

const bindMutateAsync = vi.fn().mockResolvedValue({});
const createServiceMutateAsync = vi
  .fn()
  .mockResolvedValue({ id: "svc-new", name: "Payments API", type: "Api" });
const idle = { mutate: vi.fn(), mutateAsync: vi.fn(), isPending: false, data: undefined, reset: vi.fn() };

const SERVICES = [
  {
    id: "svc-1", name: "Checkout", type: "Api", status: "Operational", uptime: null,
    incidentCount: 0, isPublic: false, displayOrder: 0, createdAt: "2026-08-01T00:00:00Z",
    teamName: "Payments Team",
  },
  {
    id: "svc-2", name: "Search", type: "Api", status: "Operational", uptime: null,
    incidentCount: 0, isPublic: false, displayOrder: 1, createdAt: "2026-08-01T00:00:00Z",
    teamName: undefined,
  },
];

const integration = {
  value: { id: "int-1", name: "grafana-prod", teamId: undefined as string | undefined },
};

vi.mock("@/features/services/hooks/use-captures", () => ({
  useCapturesByService: () => ({ data: [], isLoading: false }),
  useMarkCaptureReviewed: () => idle,
  useDeleteCapture: () => idle,
  useDeleteAllCaptures: () => idle,
}));

vi.mock("@/features/services/hooks/use-services", () => ({
  serviceQueries: { all: () => ({ queryKey: ["services", "list", "all"], queryFn: async () => SERVICES }) },
  useService: () => ({ data: undefined }),
  useCreateService: () => ({ mutateAsync: createServiceMutateAsync, isPending: false }),
}));

vi.mock("@/features/applications/hooks/use-integrations", () => ({
  useIntegration: () => ({ data: integration.value }),
  useBindIntegrationService: () => ({ mutateAsync: bindMutateAsync, isPending: false }),
}));

vi.mock("@/features/applications/api/captures.api", () => ({
  integrationCapturesApi: {
    getByIntegration: vi.fn().mockResolvedValue({ success: true, data: [] }),
    deleteAll: vi.fn(),
    testTemplate: vi.fn(),
  },
}));

const { ApplicationCaptures } = await import("./captures");

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={["/applications/int-1/captures"]}>
        <Routes>
          <Route path="/applications/:id/captures" element={<ApplicationCaptures />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

async function openBindDialog(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole("button", { name: "applications.captures.bindToService" }));
  await screen.findByRole("radiogroup");
}

beforeEach(() => {
  vi.clearAllMocks();
  integration.value = { id: "int-1", name: "grafana-prod", teamId: undefined };
});

describe("binding an application endpoint to a service", () => {
  it("binds the service the operator picked", async () => {
    const user = userEvent.setup();
    renderPage();

    await openBindDialog(user);
    await user.click(await screen.findByRole("radio", { name: /Checkout/ }));
    await user.click(screen.getByRole("button", { name: "applications.captures.bindConfirm" }));

    await waitFor(() => expect(bindMutateAsync).toHaveBeenCalled());
    expect(bindMutateAsync.mock.calls[0][0]).toEqual({ id: "int-1", serviceId: "svc-1" });
  });

  it("creates the service first, then binds the endpoint to it", async () => {
    const user = userEvent.setup();
    renderPage();

    await openBindDialog(user);
    await user.click(screen.getByRole("button", { name: "applications.captures.createNew" }));
    await user.type(screen.getByLabelText("applications.captures.serviceNameLabel"), "Payments API");
    await user.click(screen.getByRole("button", { name: "applications.captures.createAndBind" }));

    await waitFor(() => expect(bindMutateAsync).toHaveBeenCalled());
    expect(createServiceMutateAsync.mock.calls[0][0]).toEqual({ name: "Payments API", type: "Api" });
    expect(bindMutateAsync.mock.calls[0][0]).toEqual({ id: "int-1", serviceId: "svc-new" });
    expect(createServiceMutateAsync.mock.invocationCallOrder[0]).toBeLessThan(
      bindMutateAsync.mock.invocationCallOrder[0],
    );
  });

  it("warns when neither the picked service nor the endpoint has a team", async () => {
    const user = userEvent.setup();
    renderPage();

    await openBindDialog(user);
    await user.click(await screen.findByRole("radio", { name: /Search/ }));

    expect(screen.getByText("applications.captures.noTeamWarning")).toBeInTheDocument();

    await user.click(screen.getByRole("radio", { name: /Checkout/ }));
    expect(screen.queryByText("applications.captures.noTeamWarning")).not.toBeInTheDocument();
  });

  it("does not warn when the endpoint itself carries a team", async () => {
    const user = userEvent.setup();
    integration.value = { id: "int-1", name: "grafana-prod", teamId: "team-1" };
    renderPage();

    await openBindDialog(user);
    await user.click(await screen.findByRole("radio", { name: /Search/ }));

    expect(screen.queryByText("applications.captures.noTeamWarning")).not.toBeInTheDocument();
  });
});
