import "@testing-library/jest-dom/vitest";
import type { ReactElement } from "react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, within, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Routes, Route } from "react-router";

/** Pins what an operator needs mid-incident: a failed load lands on a readable error, each action
 * button fires the right call with the right id, buttons are role-gated, and war-room is wired. */

// --- incident hooks -------------------------------------------------------------------------
const reassignMutate = vi.fn();

const NOTE = {
  id: "n1",
  incidentId: "11111111-2222-3333-4444-555566667777",
  content: "Rolled back deploy 42",
  isInternal: false,
  isPinned: false,
  createdAt: "2026-07-15T10:05:00Z",
};
const updateNoteMutate = vi.fn();
const deleteNoteMutate = vi.fn();
const useIncident = vi.fn();
const useIncidentTimeline = vi.fn();
const useIncidentNotes = vi.fn();
const useWebhookDeliveries = vi.fn();
const useIncidentConference = vi.fn();
const acknowledgeMutate = vi.fn();
const investigateMutate = vi.fn();
const mitigateMutate = vi.fn();
const resolveMutate = vi.fn();
const closeMutate = vi.fn();
const reopenMutate = vi.fn();
const escalateMutate = vi.fn();
const addNoteMutate = vi.fn();
const createConferenceMutate = vi.fn();

vi.mock("@/features/users/hooks/use-users", () => ({
  useUsers: () => ({ data: [{ id: "u-2", firstName: "Ali", lastName: "Gören", email: "ali@example.io" }] }),
}));

vi.mock("../hooks/use-incidents", () => ({
  useIncident: (id: string) => useIncident(id),
  useIncidentTimeline: (id: string) => useIncidentTimeline(id),
  useIncidentNotes: (id: string) => useIncidentNotes(id),
  useWebhookDeliveries: (id: string) => useWebhookDeliveries(id),
  // The ladder has its own spec; here it only has to not be the reason the page fails to render.
  useIncidentEscalation: () => ({ data: null }),
  useReassignIncident: () => ({ mutate: reassignMutate, isPending: false }),
  useUpdateNote: () => ({ mutate: updateNoteMutate, isPending: false }),
  useDeleteNote: () => ({ mutate: deleteNoteMutate, isPending: false }),
  useIncidentConference: (id: string) => useIncidentConference(id),
  useAcknowledgeIncident: () => ({ mutate: acknowledgeMutate, isPending: false }),
  useInvestigateIncident: () => ({ mutate: investigateMutate, isPending: false }),
  useMitigateIncident: () => ({ mutate: mitigateMutate, isPending: false }),
  useResolveIncident: () => ({ mutate: resolveMutate, isPending: false }),
  useCloseIncident: () => ({ mutate: closeMutate, isPending: false }),
  useReopenIncident: () => ({ mutate: reopenMutate, isPending: false }),
  useEscalateIncident: () => ({ mutate: escalateMutate, isPending: false }),
  useAddNote: () => ({ mutate: addNoteMutate, isPending: false }),
  // The actions card has its own spec; here it only has to not break the page render.
  useServiceActions: () => ({ data: [] }),
  useExecuteServiceAction: () => ({ mutate: vi.fn(), isPending: false, data: undefined, error: null, reset: vi.fn() }),
}));

const usePostmortemsByIncident = vi.fn();
const useRunbooksByService = vi.fn();
vi.mock("@/features/postmortems/hooks/use-postmortems", () => ({
  usePostmortemsByIncident: (incidentId: string) => usePostmortemsByIncident(incidentId),
}));
vi.mock("@/features/runbooks/hooks/use-runbooks", () => ({
  useRunbooksByService: (serviceId: string) => useRunbooksByService(serviceId),
}));
vi.mock("@/features/conference/hooks/use-conference", () => ({
  useCreateConferenceRoom: () => ({ mutate: createConferenceMutate, isPending: false }),
}));

const useAuth = vi.fn();
vi.mock("@/shared/auth/auth.context", () => ({
  useAuth: () => useAuth(),
}));

const { IncidentDetail } = await import("./detail");

const INCIDENT_ID = "11111111-2222-3333-4444-555566667777";

function baseIncident(overrides: Record<string, unknown> = {}) {
  return {
    id: INCIDENT_ID,
    title: "Checkout latency spike",
    severity: "Critical",
    status: "Triggered",
    startedAt: "2026-07-15T10:00:00Z",
    createdAt: "2026-07-15T10:00:00Z",
    ...overrides,
  };
}

function setup(
  opts: {
    role?: string | null;
    incident?: unknown;
    isLoading?: boolean;
    error?: unknown;
    timeline?: { events: unknown[] };
    notes?: unknown[];
    conference?: unknown;
    postmortems?: unknown[];
    runbooks?: unknown[];
  } = {},
) {
  const {
    role = "Admin",
    incident = baseIncident(),
    isLoading = false,
    error = null,
    timeline = { events: [] },
    notes = [],
    conference = undefined,
    postmortems = [],
    runbooks = [],
  } = opts;
  useAuth.mockReturnValue({ user: role ? { role } : null });
  useIncident.mockReturnValue({ data: incident, isLoading, error });
  useIncidentTimeline.mockReturnValue({ data: timeline });
  useIncidentNotes.mockReturnValue({ data: notes });
  useWebhookDeliveries.mockReturnValue({ data: [] });
  useIncidentConference.mockReturnValue({ data: conference, refetch: vi.fn() });
  usePostmortemsByIncident.mockReturnValue({ data: postmortems });
  useRunbooksByService.mockReturnValue({ data: runbooks });
}

function renderDetail(ui: ReactElement = <IncidentDetail />) {
  return render(
    <MemoryRouter initialEntries={[`/incidents/${INCIDENT_ID}`]}>
      <Routes>
        <Route path="/incidents/:id" element={ui} />
      </Routes>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe("IncidentDetail — load states", () => {
  it("shows a spinner while the incident is loading, and no error copy", () => {
    setup({ incident: null, isLoading: true });
    const { container } = renderDetail();

    expect(container.querySelector(".animate-spin")).toBeTruthy();
    expect(screen.queryByText("Failed to load incidents")).toBeNull();
  });

  it("renders a readable error with a way back instead of spinning forever on a failed load", () => {
    // A 404/403/network failure arrives as an error + no incident. The page must resolve to an
    // error card, never an endless spinner during an outage.
    setup({ incident: null, isLoading: false, error: new Error("Request failed with status 404") });
    const { container } = renderDetail();

    expect(screen.getByText("Failed to load incidents")).toBeInTheDocument();
    expect(screen.getByText("Request failed with status 404")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Go Back" })).toHaveAttribute("href", "/incidents");
    expect(container.querySelector(".animate-spin")).toBeNull();
  });

  it("treats a missing incident with no error (e.g. 404 → null body) as an error, not a spinner", () => {
    setup({ incident: null, isLoading: false, error: null });
    const { container } = renderDetail();

    expect(screen.getByText("Failed to load incidents")).toBeInTheDocument();
    expect(container.querySelector(".animate-spin")).toBeNull();
  });
});

describe("IncidentDetail — actions fire the correct server call", () => {
  it("acknowledge calls the acknowledge mutation with the incident id (not resolve)", () => {
    setup({ role: "Admin", incident: baseIncident({ status: "Triggered" }) });
    renderDetail();

    fireEvent.click(screen.getAllByRole("button", { name: /Acknowledge/i })[0]);

    expect(acknowledgeMutate).toHaveBeenCalledWith(INCIDENT_ID);
    expect(resolveMutate).not.toHaveBeenCalled();
  });

  it("investigate calls the investigate mutation with the incident id", () => {
    setup({ role: "Admin", incident: baseIncident({ status: "Acknowledged" }) });
    renderDetail();

    fireEvent.click(screen.getAllByRole("button", { name: /Investigate/i })[0]);

    expect(investigateMutate).toHaveBeenCalledWith(INCIDENT_ID);
    expect(acknowledgeMutate).not.toHaveBeenCalled();
  });

  it("mitigate calls the mitigate mutation with the incident id", () => {
    setup({ role: "Admin", incident: baseIncident({ status: "Investigating" }) });
    renderDetail();

    fireEvent.click(screen.getAllByRole("button", { name: /Mitigate/i })[0]);

    expect(mitigateMutate).toHaveBeenCalledWith(INCIDENT_ID);
    expect(resolveMutate).not.toHaveBeenCalled();
  });

  it("does not offer Acknowledge once the incident is Investigating", () => {
    setup({ role: "Admin", incident: baseIncident({ status: "Investigating" }) });
    renderDetail();

    expect(screen.queryByRole("button", { name: /Acknowledge/i })).toBeNull();
    expect(screen.queryByRole("button", { name: /Investigate/i })).toBeNull();
    expect(screen.getAllByRole("button", { name: /Mitigate/i }).length).toBeGreaterThan(0);
  });

  /** The hook and its test shipped long before any component called them. */
  it("reassign is reachable, and asks before it moves the incident", async () => {
    const user = userEvent.setup();
    setup({ role: "Admin", incident: baseIncident({ status: "Acknowledged" }) });
    renderDetail();

    await user.click(screen.getAllByRole("button", { name: /^Reassign$/i })[0]);

    // The dialog opens; nothing has moved yet.
    expect(reassignMutate).not.toHaveBeenCalled();
    expect(await screen.findByRole("dialog")).toBeInTheDocument();
  });

  it("reassign sends the chosen person, not just the incident", async () => {
    const user = userEvent.setup();
    setup({ role: "Admin", incident: baseIncident({ status: "Acknowledged" }) });
    renderDetail();

    await user.click(screen.getAllByRole("button", { name: /^Reassign$/i })[0]);
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByLabelText(/assign to/i));
    await user.click(await screen.findByRole("option", { name: /Ali Gören/ }));
    await user.click(within(dialog).getByRole("button", { name: /^Reassign$/i }));

    await waitFor(() => expect(reassignMutate).toHaveBeenCalled());
    expect(reassignMutate.mock.calls[0][0]).toMatchObject({ id: INCIDENT_ID, targetUserId: "u-2" });
  });

  /** A note is part of the written record, so rewriting one has to leave a trace and ask first. */
  it("a note can be edited, and the edit carries both ids", async () => {
    const user = userEvent.setup();
    setup({ role: "Admin", notes: [NOTE] });
    renderDetail();

    await user.click(screen.getByRole("button", { name: /edit note/i }));
    const box = screen.getByLabelText(/note text/i);
    await user.clear(box);
    await user.type(box, "Rolled back deploy 43");
    await user.click(screen.getByRole("button", { name: /^save$/i }));

    await waitFor(() => expect(updateNoteMutate).toHaveBeenCalled());
    expect(updateNoteMutate.mock.calls[0][0]).toMatchObject({
      noteId: "n1",
      incidentId: INCIDENT_ID,
      content: "Rolled back deploy 43",
    });
  });

  it("deleting a note asks first", async () => {
    const user = userEvent.setup();
    setup({ role: "Admin", notes: [NOTE] });
    renderDetail();

    await user.click(screen.getByRole("button", { name: /delete note/i }));

    expect(deleteNoteMutate).not.toHaveBeenCalled();
    expect(await screen.findByRole("dialog")).toBeInTheDocument();
  });

  it("a viewer is offered neither editing nor deleting a note", () => {
    setup({ role: "Viewer", notes: [NOTE] });
    renderDetail();

    expect(screen.queryByRole("button", { name: /edit note/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /delete note/i })).not.toBeInTheDocument();
  });

  it("a viewer is not offered reassign at all", () => {
    setup({ role: "Viewer", incident: baseIncident({ status: "Acknowledged" }) });
    renderDetail();

    expect(screen.queryByRole("button", { name: /^Reassign$/i })).not.toBeInTheDocument();
  });

  it("escalate calls the escalate mutation carrying { id }", () => {
    setup({ role: "Admin", incident: baseIncident({ status: "Acknowledged" }) });
    renderDetail();

    fireEvent.click(screen.getAllByRole("button", { name: /Escalate/i })[0]);

    expect(escalateMutate).toHaveBeenCalledWith({ id: INCIDENT_ID });
  });

  it("start-conference is wired to the conference mutation (the only path that SMS-invites the team)", () => {
    setup({ role: "Admin", incident: baseIncident({ status: "Triggered" }), conference: undefined });
    renderDetail();

    fireEvent.click(screen.getAllByRole("button", { name: /Start conference/i })[0]);

    expect(createConferenceMutate).toHaveBeenCalledTimes(1);
    expect(createConferenceMutate.mock.calls[0][0]).toBe(INCIDENT_ID);
  });

  it("close is offered only once the incident is Resolved, and calls the close mutation", () => {
    setup({ role: "Admin", incident: baseIncident({ status: "Resolved", resolvedAt: "2026-07-15T11:00:00Z" }) });
    renderDetail();

    const closeButtons = screen.getAllByRole("button", { name: /^Close$/i });
    fireEvent.click(closeButtons[0]);
    expect(closeMutate).toHaveBeenCalledWith(INCIDENT_ID);
  });
});

describe("IncidentDetail — role gating avoids buttons the backend would 403", () => {
  it("a Viewer sees no action buttons at all", () => {
    setup({ role: "Viewer", incident: baseIncident({ status: "Triggered" }) });
    renderDetail();

    expect(screen.queryByRole("button", { name: /Acknowledge/i })).toBeNull();
    expect(screen.queryByRole("button", { name: /Escalate/i })).toBeNull();
    expect(screen.queryByRole("button", { name: /Start conference/i })).toBeNull();
  });

  it("a Member may acknowledge/resolve but is not offered investigate, mitigate, escalate, reopen or start-conference", () => {
    setup({ role: "Member", incident: baseIncident({ status: "Triggered" }) });
    renderDetail();

    expect(screen.getAllByRole("button", { name: /Acknowledge/i }).length).toBeGreaterThan(0);
    expect(screen.getAllByRole("button", { name: /Resolve/i }).length).toBeGreaterThan(0);
    expect(screen.queryByRole("button", { name: /Investigate/i })).toBeNull();
    expect(screen.queryByRole("button", { name: /Mitigate/i })).toBeNull();
    expect(screen.queryByRole("button", { name: /Escalate/i })).toBeNull();
    expect(screen.queryByRole("button", { name: /Start conference/i })).toBeNull();
  });
});

describe("IncidentDetail — the audit trail is offered only to those allowed to read it", () => {
  it("links an Admin to this incident's trail", () => {
    setup({ role: "Admin" });
    renderDetail();

    expect(screen.getByRole("link", { name: /audit trail/i }))
      .toHaveAttribute("href", `/audit-logs/incident/${INCIDENT_ID}`);
  });

  it("offers no trail to a role the audit endpoint would refuse", () => {
    setup({ role: "Member" });
    renderDetail();

    expect(screen.queryByRole("link", { name: /audit trail/i })).not.toBeInTheDocument();
  });
});

describe("IncidentDetail — postmortem and runbook side panels", () => {
  it("asks only for this incident's postmortem and this service's runbooks", () => {
    setup({ incident: baseIncident({ serviceId: "svc-7" }) });
    renderDetail();

    expect(usePostmortemsByIncident).toHaveBeenCalledWith(INCIDENT_ID);
    expect(useRunbooksByService).toHaveBeenCalledWith("svc-7");
  });

  it("asks for no runbooks at all when the incident names no service", () => {
    setup({ incident: baseIncident() });
    renderDetail();

    expect(useRunbooksByService).toHaveBeenCalledWith("");
  });

  it("shows the incident's postmortem and the service's runbooks", () => {
    setup({
      incident: baseIncident({ serviceId: "svc-7" }),
      postmortems: [
        {
          id: "pm-1",
          incidentId: INCIDENT_ID,
          title: "Checkout latency — root cause",
          status: "Published",
          createdAt: "2026-07-16T09:00:00Z",
        },
      ],
      runbooks: [{ id: "rb-1", serviceId: "svc-7", title: "Restart the checkout pods", tags: [] }],
    });
    renderDetail();

    expect(screen.getByText("Checkout latency — root cause")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /View Postmortem/i })).toHaveAttribute(
      "href",
      "/postmortems/pm-1",
    );
    expect(screen.getByRole("link", { name: /Restart the checkout pods/i })).toHaveAttribute(
      "href",
      "/runbooks/rb-1",
    );
  });
});

describe("IncidentDetail — conference + notes + timeline", () => {
  it("renders a Join link and hides Start-conference once a room is active", () => {
    setup({
      role: "Admin",
      incident: baseIncident({ status: "Triggered" }),
      conference: { participantCount: 2, userParticipantToken: "tok-abc", status: "Active" },
    });
    renderDetail();

    expect(screen.getByRole("link", { name: "Join conference" })).toHaveAttribute(
      "href",
      "/conference/tok-abc",
    );
    expect(screen.queryByRole("button", { name: /Start conference/i })).toBeNull();
  });

  it("add-note is disabled until text is entered, then submits { incidentId, content }", () => {
    setup({ role: "Admin", incident: baseIncident({ status: "Triggered" }) });
    renderDetail();

    const textarea = screen.getByPlaceholderText("Share updates, findings, or actions taken...");
    const addButtons = screen.getAllByRole("button", { name: /Add Note/i });
    const submit = addButtons[addButtons.length - 1];

    expect(submit).toBeDisabled();
    fireEvent.change(textarea, { target: { value: "Rolled back deploy 42" } });
    expect(submit).not.toBeDisabled();
    fireEvent.click(submit);

    expect(addNoteMutate).toHaveBeenCalledTimes(1);
    expect(addNoteMutate.mock.calls[0][0]).toEqual({
      incidentId: INCIDENT_ID,
      content: "Rolled back deploy 42",
    });
  });

  it("does not double-render a note: the NoteAdded marker event is dropped, the note row stays", () => {
    setup({
      role: "Admin",
      incident: baseIncident({ status: "Triggered" }),
      timeline: {
        events: [
          {
            id: "e1",
            eventType: "NoteAdded",
            title: "Note added",
            createdAt: "2026-07-15T10:05:00Z",
          },
          {
            id: "e2",
            eventType: "Escalated",
            title: "Escalated to level 2",
            createdAt: "2026-07-15T10:10:00Z",
          },
        ],
      },
      notes: [
        {
          id: "n1",
          incidentId: INCIDENT_ID,
          content: "Rolled back deploy 42",
          isInternal: false,
          isPinned: false,
          createdAt: "2026-07-15T10:05:00Z",
        },
      ],
    });
    renderDetail();

    const timelineCard = screen.getByText("Timeline").closest("div");
    const scope = within(timelineCard as HTMLElement);
    // The escalation event and the real note both show; the "Note added" marker does not add a
    // second entry for the note.
    expect(scope.getByText("Escalated to level 2")).toBeInTheDocument();
    expect(scope.getByText("Rolled back deploy 42")).toBeInTheDocument();
    expect(scope.queryByText("Note added")).toBeNull();
  });
});
