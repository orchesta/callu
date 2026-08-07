import "@testing-library/jest-dom/vitest";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Routes, Route, Link, useNavigate } from "react-router";

/** The trail is only useful if an auditor can narrow it. The screen used to offer entity type and
 * a row count, so "who signed in on the 3rd" meant scrolling. */

const search = vi.fn();

vi.mock("../hooks/use-audit-logs", () => ({
  useAuditLogSearch: (filter: unknown) => search(filter),
  useVerifyAuditChain: () => ({ mutate: vi.fn(), data: undefined, isPending: false }),
}));

const { AuditLogList } = await import("./list");

function entry(over: Record<string, unknown> = {}) {
  return {
    id: crypto.randomUUID(),
    actorDisplayName: "Ali Gören",
    action: "Login",
    resourceType: "User",
    createdAt: "2026-07-20T12:00:00Z",
    ...over,
  };
}

function page(items: unknown[], totalCount = items.length, totalPages = 1) {
  return { data: { items, totalCount, totalPages, page: 1, pageSize: 50 }, isLoading: false, error: null };
}

function listTree(entry = "/audit-logs") {
  return (
    <MemoryRouter initialEntries={[entry]}>
      <AuditLogList />
    </MemoryRouter>
  );
}

function renderList(entry = "/audit-logs") {
  return render(listTree(entry));
}

const RECORD_A = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
const RECORD_B = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";

/** Moves around the audit-log route without leaving it, which is what the sidebar and Back do. */
function Nav() {
  const navigate = useNavigate();
  return (
    <nav>
      <Link to={`/audit-logs?entityId=${RECORD_B}`}>to another record</Link>
      <Link to="/audit-logs">to the whole trail</Link>
      <button type="button" onClick={() => navigate(-1)}>go back</button>
    </nav>
  );
}

// A same-route navigation does not remount the screen, so it is the screen's job to follow the URL.
function renderWithNav(entry: string) {
  return render(
    <MemoryRouter initialEntries={[entry]}>
      <Routes>
        <Route path="/audit-logs" element={<><Nav /><AuditLogList /></>} />
      </Routes>
    </MemoryRouter>,
  );
}

/** The filter the screen most recently asked the server for. */
function lastFilter(): Record<string, unknown> {
  const { calls } = search.mock;
  return calls[calls.length - 1]?.[0] as Record<string, unknown>;
}

beforeEach(() => {
  vi.clearAllMocks();
  search.mockReturnValue(page([entry()]));
});

describe("audit log screen", () => {
  it("asks for the first page and nothing else before a filter is set", () => {
    renderList();

    expect(lastFilter()).toMatchObject({ page: 1 });
    expect(lastFilter().action).toBeUndefined();
    expect(lastFilter().from).toBeUndefined();
  });

  it("sends the chosen action, which is what the trail can now be filtered by", async () => {
    const user = userEvent.setup();
    renderList();

    await user.click(screen.getByLabelText(/^action$/i));
    await user.click(await screen.findByRole("option", { name: "EscalationNobodyReached" }));
    await user.click(screen.getByRole("button", { name: /^filter$/i }));

    await waitFor(() => expect(lastFilter().action).toBe("EscalationNobodyReached"));
  });

  it("turns the date boxes into an instant range, because the trail is stored in UTC", async () => {
    const user = userEvent.setup();
    renderList();

    await user.type(screen.getByLabelText(/^from$/i), "2026-07-20");
    await user.type(screen.getByLabelText(/^to$/i), "2026-07-20");
    await user.click(screen.getByRole("button", { name: /^filter$/i }));

    await waitFor(() => expect(lastFilter().from).toBeTruthy());
    expect(String(lastFilter().from)).toMatch(/Z$/);
    expect(String(lastFilter().to)).toMatch(/Z$/);
    // A single day has to include its own last second, or the newest entries fall out.
    expect(new Date(String(lastFilter().to)).getTime())
      .toBeGreaterThan(new Date(String(lastFilter().from)).getTime());
  });

  it("does not query on every keystroke", async () => {
    const user = userEvent.setup();
    renderList();

    const before = lastFilter();
    await user.type(screen.getByLabelText(/^search$/i), "operator");

    expect(lastFilter()).toEqual(before);
  });

  it("goes back to the first page when the filter changes", async () => {
    const user = userEvent.setup();
    search.mockReturnValue(page([entry()], 120, 3));
    renderList();

    await user.click(screen.getByRole("button", { name: /^next$/i }));
    await waitFor(() => expect(lastFilter().page).toBe(2));

    await user.click(screen.getByRole("button", { name: /^filter$/i }));

    await waitFor(() => expect(lastFilter().page).toBe(1));
  });

  it("hides the pager when everything fits on one page", () => {
    search.mockReturnValue(page([entry()], 1, 1));
    renderList();

    expect(screen.queryByRole("button", { name: /^next$/i })).not.toBeInTheDocument();
  });

  // Retention pruning deletes the old end of the log under an auditor who changed no filter, so
  // without this the page they are on has no rows, no pager, and no way back.
  it("brings the reader back when the log shrinks under them", async () => {
    const user = userEvent.setup();
    let pruned = false;
    search.mockImplementation((filter: Record<string, unknown>) => {
      if (!pruned) return page([entry({ summary: "Signed in" })], 2000, 40);
      return (filter.page as number) > 1
        ? page([], 40, 1)
        : page([entry({ summary: "Oldest surviving entry" })], 40, 1);
    });
    const { rerender } = renderList();

    await user.click(screen.getByRole("button", { name: /^next$/i }));
    await waitFor(() => expect(lastFilter().page).toBe(2));

    pruned = true;
    rerender(listTree());

    await waitFor(() => expect(lastFilter().page).toBe(1));
    expect(screen.getByText("Oldest surviving entry")).toBeInTheDocument();
  });

  // Exporting has its own spec (export-download.test.tsx); it stopped being a link the browser
  // could follow, so what is asserted there is the request, not an href.

  it("clearing puts every filter back", async () => {
    const user = userEvent.setup();
    renderList();

    await user.type(screen.getByLabelText(/^search$/i), "operator");
    await user.click(screen.getByRole("button", { name: /^filter$/i }));
    await waitFor(() => expect(lastFilter().query).toBe("operator"));

    await user.click(screen.getByRole("button", { name: /^clear$/i }));

    await waitFor(() => expect(lastFilter().query).toBeUndefined());
  });
});

// An auditor reads a row, then wants everything about that one record. Before this the trail could
// only be narrowed by entity *type*, so "this incident" meant scrolling.
// The date/action card is shared with the incident trail, which has no use for these four.
describe("the filters only the flat list has", () => {
  it("renders alongside the shared date and action fields", () => {
    renderList();

    expect(screen.getByLabelText(/^from$/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/^to$/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/^action$/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/entity type/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/record id/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/actor/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/^search$/i)).toBeInTheDocument();
  });

  it("still sends the free-text filter the shared card knows nothing about", async () => {
    const user = userEvent.setup();
    renderList();

    await user.type(screen.getByLabelText(/^search$/i), "operator");
    await user.click(screen.getByRole("button", { name: /^filter$/i }));

    await waitFor(() => expect(lastFilter().query).toBe("operator"));
  });
});

describe("narrowing the trail to one record", () => {
  it("offers a record-id and an actor filter", async () => {
    renderList();

    expect(screen.getByLabelText(/record id/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/actor/i)).toBeInTheDocument();
  });

  it("sends both to the server", async () => {
    // A full id is 36 keystrokes, and the default per-key delay alone outruns the 5s budget
    // once the rest of the suite is competing for the event loop.
    const user = userEvent.setup({ delay: null });
    renderList();

    await user.type(screen.getByLabelText(/record id/i), "33aebbd5-b8dc-4ebe-a42b-0b7e8c34ebb5");
    await user.type(screen.getByLabelText(/actor/i), "system:maintenance");
    await user.click(screen.getByRole("button", { name: /^filter$/i }));

    await waitFor(() => expect(lastFilter().resourceId).toBe("33aebbd5-b8dc-4ebe-a42b-0b7e8c34ebb5"));
    expect(lastFilter().actorId).toBe("system:maintenance");
  });

  it("clicking a row's record id filters to it and drops the other filters", async () => {
    const user = userEvent.setup();
    search.mockReturnValue(page([entry({ resourceType: "Incident", resourceId: "33aebbd5-b8dc-4ebe-a42b-0b7e8c34ebb5" })]));
    renderList();

    await user.type(screen.getByLabelText(/^search$/i), "noise");
    await user.click(screen.getByRole("button", { name: /^filter$/i }));
    await waitFor(() => expect(lastFilter().query).toBe("noise"));

    // The row is a button too, so target the id itself by the title only it carries.
    await user.click(screen.getByTitle(/Show everything recorded about/i));

    await waitFor(() => expect(lastFilter().resourceId).toBe("33aebbd5-b8dc-4ebe-a42b-0b7e8c34ebb5"));
    expect(lastFilter().query).toBeUndefined();
  });

  // The control says "everything recorded about this id", and the backend ANDs the type — so
  // sending one would answer a narrower question than the one that was asked.
  it("asks for the record under every type, not only the one the clicked row happened to be", async () => {
    const user = userEvent.setup({ delay: null });
    search.mockReturnValue(page([entry({ resourceType: "Incident", resourceId: "33aebbd5-b8dc-4ebe-a42b-0b7e8c34ebb5" })]));
    renderList();

    await user.click(screen.getByTitle(/Show everything recorded about/i));

    await waitFor(() => expect(lastFilter().resourceId).toBe("33aebbd5-b8dc-4ebe-a42b-0b7e8c34ebb5"));
    expect(lastFilter().resourceType).toBeUndefined();
  });

  // The first click puts the id in the URL, so the second one pushes the URL the screen already
  // has. Nothing changes identity, no effect runs, and the click used to do nothing at all.
  it("still narrows when the URL already names the record", async () => {
    const user = userEvent.setup({ delay: null });
    search.mockReturnValue(page([entry({ resourceType: "Incident", resourceId: "33aebbd5-b8dc-4ebe-a42b-0b7e8c34ebb5" })]));
    renderList("/audit-logs?entityId=33aebbd5-b8dc-4ebe-a42b-0b7e8c34ebb5");

    await user.type(screen.getByLabelText(/^search$/i), "noise");
    await user.click(screen.getByRole("button", { name: /^filter$/i }));
    await waitFor(() => expect(lastFilter().query).toBe("noise"));

    await user.click(screen.getByTitle(/Show everything recorded about/i));

    await waitFor(() => expect(lastFilter().query).toBeUndefined());
    expect(lastFilter().resourceId).toBe("33aebbd5-b8dc-4ebe-a42b-0b7e8c34ebb5");
  });
});

describe("getting from a row to the incident's own trail", () => {
  it("offers the trail on incident rows, alongside the filter the id already carries", () => {
    search.mockReturnValue(page([entry({ resourceType: "Incident", resourceId: "33aebbd5-b8dc-4ebe-a42b-0b7e8c34ebb5" })]));
    renderList();

    expect(screen.getByTitle(/audit trail/i))
      .toHaveAttribute("href", "/audit-logs/incident/33aebbd5-b8dc-4ebe-a42b-0b7e8c34ebb5");
    expect(screen.getByTitle(/Show everything recorded about/i)).toBeInTheDocument();
  });

  it("offers no trail on rows that are not about an incident", () => {
    search.mockReturnValue(page([entry({ resourceType: "Service", resourceId: "9c1f0e2a-1111-2222-3333-444455556666" })]));
    renderList();

    expect(screen.queryByTitle(/audit trail/i)).not.toBeInTheDocument();
  });

  it("opens already narrowed when another screen names the record in the URL", () => {
    renderList("/audit-logs?entityType=Incident&entityId=33aebbd5-b8dc-4ebe-a42b-0b7e8c34ebb5");

    expect(lastFilter().resourceType).toBe("Incident");
    expect(lastFilter().resourceId).toBe("33aebbd5-b8dc-4ebe-a42b-0b7e8c34ebb5");
  });

  // The incident trail sends the id alone, because a type would AND away the trail's other types.
  it("narrows to the record alone when the URL names no type", () => {
    renderList(`/audit-logs?entityId=${RECORD_A}`);

    expect(lastFilter().resourceId).toBe(RECORD_A);
    expect(lastFilter().resourceType).toBeUndefined();
  });
});

// Same-route navigation reuses the mounted screen, so a filter read once at mount outlives the URL
// that set it: arriving from a second incident, or on the bare log, kept asking for the first one.
describe("following the URL after the screen is already open", () => {
  it("narrows to the record the new URL names", async () => {
    const user = userEvent.setup();
    renderWithNav(`/audit-logs?entityId=${RECORD_A}`);
    expect(lastFilter().resourceId).toBe(RECORD_A);

    await user.click(screen.getByRole("link", { name: /to another record/i }));

    await waitFor(() => expect(lastFilter().resourceId).toBe(RECORD_B));
  });

  it("stops narrowing when the URL stops naming a record", async () => {
    const user = userEvent.setup();
    renderWithNav(`/audit-logs?entityId=${RECORD_A}`);

    await user.click(screen.getByRole("link", { name: /to the whole trail/i }));

    await waitFor(() => expect(lastFilter().resourceId).toBeUndefined());
  });

  it("puts a row's narrowing in the URL, so back leaves it again", async () => {
    const user = userEvent.setup();
    search.mockReturnValue(page([entry({ resourceType: "Incident", resourceId: RECORD_A })]));
    renderWithNav("/audit-logs");

    await user.click(screen.getByTitle(/Show everything recorded about/i));
    await waitFor(() => expect(lastFilter().resourceId).toBe(RECORD_A));

    await user.click(screen.getByRole("button", { name: /go back/i }));

    await waitFor(() => expect(lastFilter().resourceId).toBeUndefined());
  });
});

// Escalation-outcome rows put their whole content in changeAfter, so the column that is supposed to
// say what happened showed a dash for exactly the rows saying nobody was reached.
describe("what a row says it is", () => {
  it("falls back to the values when nothing wrote a summary", () => {
    search.mockReturnValue(page([
      entry({ summary: undefined, changeBefore: "Status: Open", changeAfter: "Status: Acknowledged" }),
    ]));
    renderList();

    expect(screen.getByText("Status: Open → Status: Acknowledged")).toBeInTheDocument();
  });

  it("prefers the summary when there is one", () => {
    search.mockReturnValue(page([
      entry({ summary: "Incident acknowledged", changeBefore: "Status: Open", changeAfter: "Status: Acknowledged" }),
    ]));
    renderList();

    expect(screen.getByText("Incident acknowledged")).toBeInTheDocument();
  });

  it("shows a dash only when the row genuinely carries nothing", () => {
    search.mockReturnValue(page([entry({ summary: undefined, changeBefore: undefined, changeAfter: undefined })]));
    renderList();

    expect(screen.getByText("—")).toBeInTheDocument();
  });
});
