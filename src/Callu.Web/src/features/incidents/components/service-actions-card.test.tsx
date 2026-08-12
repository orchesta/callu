import "@testing-library/jest-dom/vitest";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

// Locale keys are asserted literally so the tests do not move when translations change.
vi.mock("@/shared/locales/i18n", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/shared/locales/i18n")>();
  return { ...actual, t: (key: string) => key };
});

const getServiceActions = vi.fn();
const executeAction = vi.fn();
vi.mock("../api/incident.api", () => ({
  incidentApi: {
    getServiceActions: (serviceId: string) => getServiceActions(serviceId),
    executeAction: (incidentId: string, actionId: string) => executeAction(incidentId, actionId),
  },
}));

const useAuth = vi.fn();
vi.mock("@/shared/auth/auth.context", () => ({
  useAuth: () => useAuth(),
}));

const { ServiceActionsCard } = await import("./service-actions-card");
const { ApiError } = await import("@/shared/api");

function action(overrides: Record<string, unknown> = {}) {
  return {
    id: "act-1",
    serviceId: "svc-1",
    name: "Restart checkout pods",
    description: "Rolling restart of the checkout deployment",
    url: "https://ops.example.io/hooks/restart",
    httpMethod: "POST",
    contentType: "application/json",
    hasSecret: false,
    isEnabled: true,
    displayOrder: 0,
    createdAt: "2026-08-01T00:00:00Z",
    ...overrides,
  };
}

function renderCard() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <ServiceActionsCard incidentId="inc-1" serviceId="svc-1" />
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  useAuth.mockReturnValue({ user: { role: "Admin" } });
  getServiceActions.mockResolvedValue({
    success: true,
    data: [
      action(),
      action({ id: "act-2", name: "Rotate edge cache", isEnabled: false, displayOrder: 1 }),
    ],
  });
});

describe("ServiceActionsCard", () => {
  it("lists enabled actions only, asks before running, and executes exactly once", async () => {
    const user = userEvent.setup();
    executeAction.mockResolvedValue({
      success: true,
      data: { outcome: "succeeded", httpStatus: 200 },
    });
    renderCard();

    expect(await screen.findByText("serviceActions.incidentCardTitle")).toBeInTheDocument();
    expect(screen.getByText("Restart checkout pods")).toBeInTheDocument();
    expect(screen.queryByText("Rotate edge cache")).toBeNull();

    await user.click(screen.getByRole("button", { name: "serviceActions.runButton" }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText("serviceActions.runConfirmText")).toBeInTheDocument();
    expect(within(dialog).getByText("https://ops.example.io/hooks/restart")).toBeInTheDocument();
    expect(executeAction).not.toHaveBeenCalled();

    await user.click(within(dialog).getByRole("button", { name: "serviceActions.runButton" }));

    await waitFor(() => expect(executeAction).toHaveBeenCalledTimes(1));
    expect(executeAction).toHaveBeenCalledWith("inc-1", "act-1");
    expect(await within(dialog).findByText("serviceActions.resultSucceeded")).toBeInTheDocument();
    // The result replaces the confirm controls; only close remains (the header X shares its name).
    expect(within(dialog).queryByRole("button", { name: "serviceActions.runButton" })).toBeNull();
    expect(within(dialog).queryByRole("button", { name: "common.cancel" })).toBeNull();
    expect(within(dialog).getAllByRole("button", { name: "common.close" }).length).toBeGreaterThanOrEqual(1);
  });

  it("cancel closes the dialog without calling the execute api", async () => {
    const user = userEvent.setup();
    renderCard();

    await user.click(await screen.findByRole("button", { name: "serviceActions.runButton" }));
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: "common.cancel" }));

    expect(executeAction).not.toHaveBeenCalled();
  });

  it("disables every run button while an execution is pending, so nothing double-fires", async () => {
    const user = userEvent.setup();
    let resolveExecute: (value: unknown) => void = () => {};
    executeAction.mockImplementation(() => new Promise((resolve) => { resolveExecute = resolve; }));
    renderCard();

    await user.click(await screen.findByRole("button", { name: "serviceActions.runButton" }));
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: "serviceActions.runButton" }));

    const running = await within(dialog).findByRole("button", { name: "serviceActions.running" });
    expect(running).toBeDisabled();
    await user.click(running);
    expect(executeAction).toHaveBeenCalledTimes(1);

    // The row's run button sits behind the modal, so query by text rather than role.
    for (const label of screen.getAllByText("serviceActions.runButton")) {
      expect(label.closest("button")).toBeDisabled();
    }

    resolveExecute({ success: true, data: { outcome: "succeeded" } });
    expect(await within(dialog).findByText("serviceActions.resultSucceeded")).toBeInTheDocument();
  });

  it("shows the localized throttle text on a 409, not the API's English message", async () => {
    const user = userEvent.setup();
    executeAction.mockRejectedValue(new ApiError(409, "Action was executed recently."));
    renderCard();

    await user.click(await screen.findByRole("button", { name: "serviceActions.runButton" }));
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: "serviceActions.runButton" }));

    expect(await within(dialog).findByText("serviceActions.throttled")).toBeInTheDocument();
    expect(within(dialog).queryByText("Action was executed recently.")).toBeNull();
  });

  it("keeps the API message for non-409 failures", async () => {
    const user = userEvent.setup();
    executeAction.mockRejectedValue(new ApiError(500, "upstream exploded"));
    renderCard();

    await user.click(await screen.findByRole("button", { name: "serviceActions.runButton" }));
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: "serviceActions.runButton" }));

    expect(await within(dialog).findByText("upstream exploded")).toBeInTheDocument();
    expect(within(dialog).queryByText("serviceActions.throttled")).toBeNull();
  });

  it("renders nothing for a Viewer, and does not even fetch the actions", async () => {
    useAuth.mockReturnValue({ user: { role: "Viewer" } });
    renderCard();

    expect(screen.queryByText("serviceActions.incidentCardTitle")).toBeNull();
    expect(getServiceActions).not.toHaveBeenCalled();
  });
});
