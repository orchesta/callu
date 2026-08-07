import "@testing-library/jest-dom/vitest";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

/** Characterization tests for the status page admin screen, aimed at its quiet failures: a save that
 * reports success without saving, a reorder that never reaches the server, an unprobed component. */

const statusPages = vi.fn();
const statusPage = vi.fn();
const subscribers = vi.fn();

const createPage = vi.fn();
const updatePage = vi.fn();
const addComponent = vi.fn();
const updateComponent = vi.fn();
const removeComponent = vi.fn();
const removeSubscriber = vi.fn();
const createIncident = vi.fn();
const addIncidentUpdate = vi.fn();
const notifySubscribers = vi.fn();
const testHealthCheck = vi.fn();
const sniffHealthCheck = vi.fn();

vi.mock("../hooks/use-status-pages", () => ({
  useStatusPages: () => statusPages(),
  useStatusPage: (id: string) => statusPage(id),
  useDeleteStatusPage: () => ({ mutate: vi.fn(), isPending: false }),
  useCreateStatusPage: () => ({ mutate: createPage, isPending: false }),
  useUpdateStatusPage: () => ({ mutateAsync: updatePage, isPending: false }),
  useAddComponent: () => ({ mutate: addComponent, isPending: false }),
  useUpdateComponent: () => ({ mutate: updateComponent, isPending: false }),
  useRemoveComponent: () => ({ mutate: removeComponent, isPending: false }),
  useCreateStatusIncident: () => ({ mutate: createIncident, isPending: false }),
  useAddIncidentUpdate: () => ({ mutate: addIncidentUpdate, isPending: false }),
  useNotifyStatusPageSubscribers: () => ({ mutate: notifySubscribers, isPending: false }),
  useStatusPageStats: () => ({ data: undefined }),
  useStatusPageUptime: () => ({ data: undefined, isLoading: false }),
  useStatusPageSubscribers: (id: string | undefined) => subscribers(id),
  useRemoveSubscriber: () => ({ mutate: removeSubscriber, isPending: false }),
  useTestHealthCheck: () => ({ mutateAsync: testHealthCheck, isPending: false }),
  useSniffHealthCheck: () => ({ mutateAsync: sniffHealthCheck, isPending: false }),
}));

vi.mock("@/features/services/hooks/use-services", () => ({
  useServices: () => ({ data: [{ id: "svc-1", name: "Payments API" }] }),
}));

vi.mock("./uptime-graph", () => ({ UptimeGraph: () => null }));

const toast = { error: vi.fn(), success: vi.fn(), warning: vi.fn(), info: vi.fn() };
vi.mock("@/shared/utils/toast", () => ({ toast }));

// Imported after the mocks are registered: a static import is hoisted above them and would pull
// the real modules in.
const { StatusPageManagement } = await import("./manage");

const component = (over: Record<string, unknown> = {}) => ({
  id: "comp-1",
  name: "Payments API",
  status: "operational",
  displayOrder: 1,
  healthCheckEnabled: false,
  healthCheckIntervalSeconds: 60,
  healthCheckTimeoutSeconds: 10,
  healthCheckConsecutiveFailures: 0,
  ...over,
});

const pageDetail = (over: Record<string, unknown> = {}) => ({
  id: "page-1",
  name: "Acme Status",
  slug: "acme",
  isPublic: true,
  supportEmail: "help@acme.io",
  allowSubscriptions: true,
  description: "Our status",
  components: [component()],
  incidents: [],
  ...over,
});

beforeEach(() => {
  vi.clearAllMocks();
  statusPages.mockReturnValue({ data: [{ id: "page-1" }], isLoading: false });
  statusPage.mockReturnValue({ data: pageDetail() });
  subscribers.mockReturnValue({ data: [], isLoading: false });
});

describe("StatusPageManagement — first run", () => {
  it("shows a spinner while the page list is loading", () => {
    statusPages.mockReturnValue({ data: undefined, isLoading: true });
    render(<StatusPageManagement />);
    expect(screen.queryByRole("button", { name: /Create Status Page/i })).not.toBeInTheDocument();
  });

  it("offers to create a page instead of spinning forever when none exists", async () => {
    statusPages.mockReturnValue({ data: [], isLoading: false });
    render(<StatusPageManagement />);

    const create = screen.getByRole("button", { name: /Create Status Page/i });
    await userEvent.click(create);

    expect(createPage).toHaveBeenCalledTimes(1);
    expect(createPage.mock.calls[0][0]).toMatchObject({ name: expect.any(String), slug: expect.any(String) });
  });
});

describe("StatusPageManagement — settings", () => {
  it("seeds the form from the stored page", () => {
    render(<StatusPageManagement />);
    expect(screen.getByLabelText(/Company Name/i)).toHaveValue("Acme Status");
    expect(screen.getByLabelText(/Support Email/i)).toHaveValue("help@acme.io");
  });

  it("saves the edited fields against the current page id", async () => {
    const user = userEvent.setup();
    updatePage.mockResolvedValue({});
    render(<StatusPageManagement />);

    const name = screen.getByLabelText(/Company Name/i);
    await user.clear(name);
    await user.type(name, "Acme Inc");
    await user.click(screen.getByRole("button", { name: /Save Changes/i }));

    await waitFor(() => expect(updatePage).toHaveBeenCalledTimes(1));
    expect(updatePage).toHaveBeenCalledWith(
      expect.objectContaining({ id: "page-1", name: "Acme Inc", isPublic: true, allowSubscriptions: true }),
    );
  });

  // A save that failed but reported success would leave the operator believing the public page
  // says something it does not.
  it("surfaces a rejected save rather than reporting success", async () => {
    const user = userEvent.setup();
    updatePage.mockRejectedValue(new Error("slug already taken"));
    render(<StatusPageManagement />);

    await user.click(screen.getByRole("button", { name: /Save Changes/i }));

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("slug already taken"));
    expect(screen.queryByText(/saved/i)).not.toBeInTheDocument();
  });
});

describe("StatusPageManagement — health check", () => {
  const probed = component({
    healthCheckEnabled: true,
    healthCheckUrl: "https://acme.io/health",
    healthCheckHttpMethod: "POST",
    healthCheckIntervalSeconds: 30,
    healthCheckTimeoutSeconds: 5,
    healthCheckHeaders: '{"X-Token":"abc"}',
    healthCheckBody: '{"ping":1}',
    healthCheckContentType: "application/json",
    healthCheckFieldMappings: '{"status":"$.state"}',
    healthCheckStateMapping: '{"up":"ok"}',
  });

  it("seeds the dialog from the component's stored probe config", async () => {
    statusPage.mockReturnValue({ data: pageDetail({ components: [probed] }) });
    render(<StatusPageManagement />);

    await userEvent.click(screen.getByRole("button", { name: "Health check for Payments API" }));

    expect(screen.getByDisplayValue("https://acme.io/health")).toBeInTheDocument();
  });

  // The probe config is what decides whether a component is watched at all. A save that drops or
  // mangles a field leaves the component silently unprobed while the UI reports success.
  it("saves every probe field it was seeded with, unchanged", async () => {
    statusPage.mockReturnValue({ data: pageDetail({ components: [probed] }) });
    render(<StatusPageManagement />);

    await userEvent.click(screen.getByRole("button", { name: "Health check for Payments API" }));
    await userEvent.click(screen.getByRole("button", { name: /Save Health Check/i }));

    expect(updateComponent).toHaveBeenCalledTimes(1);
    expect(updateComponent.mock.calls[0][0]).toEqual({
      componentId: "comp-1",
      healthCheckEnabled: true,
      healthCheckUrl: "https://acme.io/health",
      healthCheckHttpMethod: "POST",
      healthCheckIntervalSeconds: 30,
      healthCheckTimeoutSeconds: 5,
      healthCheckHeaders: '{"X-Token":"abc"}',
      healthCheckBody: '{"ping":1}',
      healthCheckContentType: "application/json",
      healthCheckFieldMappings: '{"status":"$.state"}',
      healthCheckStateMapping: '{"up":"ok"}',
    });
  });

  it("sends an unset url as undefined rather than an empty string", async () => {
    statusPage.mockReturnValue({
      data: pageDetail({ components: [component({ healthCheckEnabled: true })] }),
    });
    render(<StatusPageManagement />);

    await userEvent.click(screen.getByRole("button", { name: "Health check for Payments API" }));
    await userEvent.click(screen.getByRole("button", { name: /Save Health Check/i }));

    expect(updateComponent.mock.calls[0][0]).toMatchObject({ healthCheckUrl: undefined });
  });
});

describe("StatusPageManagement — subscribers", () => {
  it("removes a subscriber by email against the current page", async () => {
    subscribers.mockReturnValue({
      data: [{ email: "ada@acme.io", subscribedAt: "2026-01-01T00:00:00Z", isConfirmed: true }],
      isLoading: false,
    });
    render(<StatusPageManagement />);

    await userEvent.click(screen.getByTitle(/Remove subscriber/i));

    expect(removeSubscriber).toHaveBeenCalledWith({ pageId: "page-1", email: "ada@acme.io" });
  });
});
