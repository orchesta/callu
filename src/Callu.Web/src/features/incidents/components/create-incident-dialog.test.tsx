import "@testing-library/jest-dom/vitest";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";

/** The create path has one rule the UI must never break: a suppressed incident was never written,
 * so it must not be announced as created and must not navigate to a page that does not exist. */

const mutateAsync = vi.fn();
const pending = { value: false };
const navigate = vi.fn();

vi.mock("../hooks/use-incidents", () => ({
  useCreateIncident: () => ({ mutateAsync, isPending: pending.value }),
}));

vi.mock("@/features/services/hooks/use-services", () => ({
  useServices: () => ({ data: [{ id: "svc-1", name: "Payments API" }] }),
}));

vi.mock("@/features/teams/hooks/use-teams", () => ({
  useTeams: () => ({ data: [{ id: "team-1", name: "Platform" }] }),
}));

vi.mock("react-router", async () => {
  const actual = await vi.importActual<typeof import("react-router")>("react-router");
  return { ...actual, useNavigate: () => navigate };
});

const { CreateIncidentDialog } = await import("./create-incident-dialog");

function renderDialog() {
  const onOpenChange = vi.fn();
  render(
    <MemoryRouter>
      <CreateIncidentDialog open onOpenChange={onOpenChange} />
    </MemoryRouter>,
  );
  return { onOpenChange };
}

function submitButton() {
  return screen.getByRole("button", { name: /create incident/i });
}

beforeEach(() => {
  vi.clearAllMocks();
  pending.value = false;
  mutateAsync.mockResolvedValue({
    outcome: "Created",
    incident: { id: "inc-9", title: "DB down" },
  });
});

describe("CreateIncidentDialog", () => {
  it("refuses to submit without a title, and a blank one does not count", async () => {
    const user = userEvent.setup();
    renderDialog();

    expect(submitButton()).toBeDisabled();

    await user.type(screen.getByLabelText(/incident title/i), "   ");
    expect(submitButton()).toBeDisabled();

    await user.type(screen.getByLabelText(/incident title/i), "DB down");
    expect(submitButton()).toBeEnabled();
  });

  it("sends the trimmed title, the chosen severity, and omits the fields left unset", async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.type(screen.getByLabelText(/incident title/i), "  DB down  ");
    await user.click(submitButton());

    await waitFor(() => expect(mutateAsync).toHaveBeenCalledTimes(1));
    expect(mutateAsync).toHaveBeenCalledWith({
      title: "DB down",
      description: undefined,
      // High is the default, so an operator who does not touch the field still gets a real value.
      severity: "High",
      serviceId: undefined,
      teamId: undefined,
    });
  });

  it("sends the service and team once they are picked", async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.type(screen.getByLabelText(/incident title/i), "DB down");

    await user.click(screen.getByLabelText(/^service$/i));
    await user.click(await screen.findByRole("option", { name: "Payments API" }));

    await user.click(screen.getByLabelText(/^team$/i));
    await user.click(await screen.findByRole("option", { name: "Platform" }));

    await user.click(submitButton());

    await waitFor(() => expect(mutateAsync).toHaveBeenCalledTimes(1));
    expect(mutateAsync.mock.calls[0][0]).toMatchObject({
      serviceId: "svc-1",
      teamId: "team-1",
    });
  });

  it("opens the incident it just created", async () => {
    const user = userEvent.setup();
    const { onOpenChange } = renderDialog();

    await user.type(screen.getByLabelText(/incident title/i), "DB down");
    await user.click(submitButton());

    await waitFor(() => expect(navigate).toHaveBeenCalledWith("/incidents/inc-9"));
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("never navigates when a maintenance window suppressed the incident", async () => {
    // 202: the API considered it and deliberately wrote nothing, so there is no incident to open.
    mutateAsync.mockResolvedValue({
      outcome: "Suppressed",
      incident: null,
      reason: "DB migration 02:00-04:00",
    });

    const user = userEvent.setup();
    const { onOpenChange } = renderDialog();

    await user.type(screen.getByLabelText(/incident title/i), "DB down");
    await user.click(submitButton());

    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
    expect(navigate).not.toHaveBeenCalled();
  });

  it("cannot be submitted twice while the first create is in flight", () => {
    pending.value = true;
    renderDialog();

    // While pending the button reads "Creating...", so it is found by that label, not the idle one.
    expect(screen.getByRole("button", { name: /creating/i })).toBeDisabled();
    expect(screen.getByRole("button", { name: /cancel/i })).toBeDisabled();
  });
});
