import "@testing-library/jest-dom/vitest";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";

/** The bare "a"/"r" window shortcuts bulk-acknowledge and resolve with no confirmation, so most of
 * these pin when they must NOT fire; the rest pin the paging reset when a filter changes. */

const useIncidents = vi.fn();
const bulkAckMutate = vi.fn();
const bulkResolveMutate = vi.fn();
const bulkAckPending = { value: false };
const bulkResolvePending = { value: false };

vi.mock("../hooks/use-incidents", () => ({
  useIncidents: (filter: unknown) => useIncidents(filter),
  useBulkAcknowledge: () => ({ mutate: bulkAckMutate, isPending: bulkAckPending.value }),
  useBulkResolve: () => ({ mutate: bulkResolveMutate, isPending: bulkResolvePending.value }),
}));

vi.mock("@/features/dashboard/hooks/use-dashboard", () => ({
  useIncidentCounts: () => ({ data: { Open: 2, Acknowledged: 1, Resolved: 5 } }),
}));

const useAuth = vi.fn();
vi.mock("@/shared/auth/auth.context", () => ({
  useAuth: () => useAuth(),
}));

const { IncidentsList } = await import("./list");

function incident(id: string, title: string) {
  return {
    id,
    title,
    severity: "High",
    status: "Open",
    serviceName: "api",
    startedAt: "2026-07-15T10:00:00Z",
    acknowledgedBy: null,
  };
}

const INCIDENTS = [incident("inc-1", "DB down"), incident("inc-2", "API latency")];

function renderList() {
  return render(
    <MemoryRouter>
      <IncidentsList />
    </MemoryRouter>,
  );
}

/** Select every incident via the header select-all checkbox. */
async function selectAll(user: ReturnType<typeof userEvent.setup>) {
  const [selectAllBox] = screen.getAllByRole("checkbox");
  await user.click(selectAllBox);
  // The count shows in both the header row and the bulk action bar.
  await screen.findAllByText(/2 selected/i);
}

/** The filter the component most recently asked the server for. */
function lastFilter(): Record<string, unknown> {
  const { calls } = useIncidents.mock;
  return calls[calls.length - 1]?.[0] as Record<string, unknown>;
}

beforeEach(() => {
  vi.clearAllMocks();
  useAuth.mockReturnValue({ user: { role: "Admin" } });
  bulkAckPending.value = false;
  bulkResolvePending.value = false;
  useIncidents.mockReturnValue({
    data: {
      items: INCIDENTS,
      totalCount: 2,
      totalPages: 1,
      hasNextPage: false,
      hasPreviousPage: false,
    },
    isLoading: false,
    isError: false,
  });
});

describe("IncidentsList — the bulk shortcuts fire only when they should", () => {
  it("acknowledges exactly the selected incidents on 'a'", async () => {
    const user = userEvent.setup();
    renderList();
    await selectAll(user);

    fireEvent.keyDown(window, { key: "a" });

    expect(bulkAckMutate).toHaveBeenCalledTimes(1);
    expect(bulkAckMutate.mock.calls[0][0]).toEqual(["inc-1", "inc-2"]);
    expect(bulkResolveMutate).not.toHaveBeenCalled();
  });

  it("resolves exactly the selected incidents on 'r'", async () => {
    const user = userEvent.setup();
    renderList();
    await selectAll(user);

    fireEvent.keyDown(window, { key: "r" });

    expect(bulkResolveMutate).toHaveBeenCalledTimes(1);
    expect(bulkResolveMutate.mock.calls[0][0]).toEqual(["inc-1", "inc-2"]);
    expect(bulkAckMutate).not.toHaveBeenCalled();
  });

  /** Read together with the negative case below, which would otherwise pass for the dull reason
   * that no keystroke was ever delivered. */
  it("fires on a genuinely typed key when focus is not in a text field", async () => {
    const user = userEvent.setup();
    renderList();
    await selectAll(user);

    await user.keyboard("a");

    expect(bulkAckMutate).toHaveBeenCalledTimes(1);
  });

  it("does NOT acknowledge when that same 'a' is typed into the search box", async () => {
    // The bug this guards: search for "api" with a selection live, and the queue is acknowledged
    // out from under the escalation policy without anyone touching an incident.
    const user = userEvent.setup();
    renderList();
    await selectAll(user);

    const search = screen.getByPlaceholderText(/Search incidents by title/i);
    await user.click(search);
    await user.keyboard("api");

    // The keystrokes landed in the field — they were delivered, and deliberately ignored.
    expect(search).toHaveValue("api");
    expect(bulkAckMutate).not.toHaveBeenCalled();
    expect(bulkResolveMutate).not.toHaveBeenCalled();
  });

  it("ignores 'a' with a modifier held, so Ctrl/Cmd+A still means select-all", async () => {
    const user = userEvent.setup();
    renderList();
    await selectAll(user);

    fireEvent.keyDown(window, { key: "a", ctrlKey: true });
    fireEvent.keyDown(window, { key: "a", metaKey: true });
    fireEvent.keyDown(window, { key: "a", altKey: true });

    expect(bulkAckMutate).not.toHaveBeenCalled();
  });

  it("does nothing when no incident is selected", () => {
    renderList();

    fireEvent.keyDown(window, { key: "a" });
    fireEvent.keyDown(window, { key: "r" });

    expect(bulkAckMutate).not.toHaveBeenCalled();
    expect(bulkResolveMutate).not.toHaveBeenCalled();
  });

  it("does not fire a second bulk call while one is still in flight", async () => {
    // Set before render so every render of the effect closes over the pending state; selecting
    // then re-renders and re-binds the listener with the guard live.
    bulkAckPending.value = true;
    const user = userEvent.setup();
    renderList();
    await selectAll(user);

    fireEvent.keyDown(window, { key: "a" });

    expect(bulkAckMutate).not.toHaveBeenCalled();
    // The guard is about the in-flight ack specifically, not about freezing the whole screen.
    fireEvent.keyDown(window, { key: "r" });
    expect(bulkResolveMutate).not.toHaveBeenCalled();
  });

  it("stops listening once unmounted, so the shortcut cannot outlive the screen", async () => {
    const user = userEvent.setup();
    const { unmount } = renderList();
    await selectAll(user);

    unmount();
    fireEvent.keyDown(window, { key: "a" });

    expect(bulkAckMutate).not.toHaveBeenCalled();
  });
});

describe("IncidentsList — filtering", () => {
  it("returns to page 1 when a filter changes, so the queue is not silently empty", async () => {
    const user = userEvent.setup();
    useIncidents.mockReturnValue({
      data: {
        items: INCIDENTS,
        totalCount: 60,
        totalPages: 3,
        hasNextPage: true,
        hasPreviousPage: false,
      },
      isLoading: false,
      isError: false,
    });
    renderList();

    // Walk to page 2 first.
    await user.click(screen.getByRole("button", { name: /Next/i }));
    await waitFor(() =>
      expect(lastFilter()).toMatchObject({ page: 2 }),
    );

    // Narrowing the search from page 2 must not leave the request asking for page 2 of a
    // result that may now have only one page.
    await user.type(screen.getByPlaceholderText(/Search incidents by title/i), "db");

    await waitFor(() =>
      expect(lastFilter()).toMatchObject({
        page: 1,
        searchQuery: "db",
      }),
    );
  });

  it("asks the server for no status/severity filter while both are 'all'", () => {
    renderList();

    // `undefined` (not the string "all") is what the API contract expects for "no filter".
    expect(useIncidents.mock.calls[0][0]).toMatchObject({
      status: undefined,
      severity: undefined,
      searchQuery: undefined,
      page: 1,
    });
  });
});
