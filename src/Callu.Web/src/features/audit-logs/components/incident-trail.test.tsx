import "@testing-library/jest-dom/vitest";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Routes, Route } from "react-router";

/** One incident's record read as a story: everything written about it, in the order it happened. */

const search = vi.fn();

vi.mock("../hooks/use-audit-logs", () => ({
  useAuditLogSearch: (filter: unknown) => search(filter),
}));

const { IncidentAuditTrail } = await import("./incident-trail");

const INCIDENT_ID = "33aebbd5-b8dc-4ebe-a42b-0b7e8c34ebb5";

function entry(over: Record<string, unknown> = {}) {
  return {
    id: crypto.randomUUID(),
    actorDisplayName: "Ali Gören",
    action: "Created",
    resourceType: "Incident",
    resourceId: INCIDENT_ID,
    createdAt: "2026-07-20T12:00:00Z",
    ...over,
  };
}

function page(items: unknown[], totalCount = items.length, totalPages = 1) {
  return {
    data: { items, totalCount, totalPages, page: 1, pageSize: 100 },
    isLoading: false,
    error: null,
  };
}

function lastFilter(): Record<string, unknown> {
  const { calls } = search.mock;
  return calls[calls.length - 1]?.[0] as Record<string, unknown>;
}

function trailTree() {
  return (
    <MemoryRouter initialEntries={[`/audit-logs/incident/${INCIDENT_ID}`]}>
      <Routes>
        <Route path="/audit-logs/incident/:id" element={<IncidentAuditTrail />} />
      </Routes>
    </MemoryRouter>
  );
}

function renderTrail() {
  return render(trailTree());
}

function rowText(container: HTMLElement): string[] {
  return [...container.querySelectorAll("tbody tr")].map((row) => row.textContent ?? "");
}

beforeEach(() => {
  vi.clearAllMocks();
  search.mockReturnValue(page([entry()]));
});

describe("incident audit trail", () => {
  // Notes are recorded against the incident itself, and nothing has ever written an IncidentNote row
  // carrying an incident id, so asking for that type only narrows the trail to nothing.
  it("asks for this incident under every type its record is written against", () => {
    renderTrail();

    expect(lastFilter().resourceId).toBe(INCIDENT_ID);
    expect(lastFilter().resourceTypes).toEqual([
      "Incident",
      "Escalation",
      "NotificationChannel",
      "AuditLog",
    ]);
    expect(lastFilter().resourceTypes).not.toContain("IncidentNote");
    expect(lastFilter().pageSize).toBe(100);
  });

  // The flat list is a log and reads newest first; a single incident is a story and reads forwards.
  it("reads oldest first, and asks the server for that order", () => {
    search.mockReturnValue(page([
      entry({ summary: "Incident created", createdAt: "2026-07-20T12:00:00Z" }),
      entry({ summary: "Escalated to level 2", createdAt: "2026-07-20T12:20:00Z" }),
      entry({ summary: "Incident resolved", createdAt: "2026-07-20T12:40:00Z" }),
    ]));
    const { container } = renderTrail();

    expect(lastFilter().sortAscending).toBe(true);
    expect(lastFilter().page).toBe(1);

    const rows = rowText(container);
    expect(rows).toHaveLength(3);
    expect(rows[0]).toContain("Incident created");
    expect(rows[1]).toContain("Escalated to level 2");
    expect(rows[2]).toContain("Incident resolved");
  });

  // Re-sorting one page cannot put back the pages before it; it only hides that they are missing.
  it("renders the page the server answered with, in the order it came", () => {
    search.mockReturnValue(page([
      entry({ summary: "Incident resolved", createdAt: "2026-07-20T12:40:00Z" }),
      entry({ summary: "Incident created", createdAt: "2026-07-20T12:00:00Z" }),
    ], 240, 3));
    const { container } = renderTrail();

    expect(rowText(container)[0]).toContain("Incident resolved");
  });

  it("still narrows by action, so 'when did nobody answer' is one choice", async () => {
    const user = userEvent.setup();
    renderTrail();

    await user.click(screen.getByLabelText(/^action$/i));
    await user.click(await screen.findByRole("option", { name: "EscalationNobodyReached" }));
    await user.click(screen.getByRole("button", { name: /^filter$/i }));

    expect(lastFilter().action).toBe("EscalationNobodyReached");
    expect(lastFilter().resourceId).toBe(INCIDENT_ID);
  });

  // Exporting one incident's history is filed against that incident, so it belongs in its story.
  it("includes the row that an export of this incident's history writes", () => {
    search.mockReturnValue(page([entry({
      resourceType: "AuditLog", action: "Exported", summary: "Trail exported as csv",
    })]));
    const { container } = renderTrail();

    expect(lastFilter().resourceTypes).toContain("AuditLog");
    expect(rowText(container)[0]).toContain("Trail exported as csv");
  });

  // The flat list ANDs a single entity type, so naming one there would hide the trail's other
  // types behind a button labelled "full".
  it("leads back to the incident and out to the full log, narrowed only by the id", () => {
    renderTrail();

    expect(screen.getByRole("link", { name: /open incident/i }))
      .toHaveAttribute("href", `/incidents/${INCIDENT_ID}`);
    expect(screen.getByRole("link", { name: /open in full audit log/i }))
      .toHaveAttribute("href", `/audit-logs?entityId=${INCIDENT_ID}`);
  });

  it("says so when nothing has been recorded yet", () => {
    search.mockReturnValue(page([]));
    renderTrail();

    expect(screen.getByText(/nothing recorded for this incident/i)).toBeInTheDocument();
  });

  it("shows a spinner while the trail loads", () => {
    search.mockReturnValue({ data: undefined, isLoading: true, error: null });
    renderTrail();

    expect(screen.getByRole("status")).toBeInTheDocument();
  });

  it("shows the failure instead of an empty trail when the read fails", () => {
    search.mockReturnValue({ data: undefined, isLoading: false, error: new Error("HTTP 500") });
    renderTrail();

    expect(screen.getByText(/failed to load audit trail/i)).toBeInTheDocument();
    expect(screen.getByText("HTTP 500")).toBeInTheDocument();
  });

  // The row carries role="button" as well, so the detail control is reached by its own title.
  it("opens the full entry behind a row", async () => {
    const user = userEvent.setup();
    search.mockReturnValue(page([entry({ summary: "Incident created", requestIpAddress: "10.0.0.7" })]));
    renderTrail();

    await user.click(screen.getByTitle(/view entry details/i));

    const dialog = await screen.findByRole("dialog");
    expect(dialog).toHaveTextContent("10.0.0.7");
  });
});

// A long incident outruns one page. Without a pager the screen showed one page and said nothing,
// so the beginning of the story — creation, the first escalation — was simply absent.
describe("a trail longer than one page", () => {
  it("starts at the oldest page", () => {
    search.mockReturnValue(page([entry()], 240, 3));
    renderTrail();

    expect(lastFilter().page).toBe(1);
    expect(lastFilter().sortAscending).toBe(true);
  });

  it("reaches the page after it", async () => {
    const user = userEvent.setup();
    search.mockReturnValue(page([entry()], 240, 3));
    renderTrail();

    await user.click(screen.getByRole("button", { name: /^next$/i }));

    await waitFor(() => expect(lastFilter().page).toBe(2));
  });

  it("goes back to the first page when the filter changes", async () => {
    const user = userEvent.setup();
    search.mockReturnValue(page([entry()], 240, 3));
    renderTrail();

    await user.click(screen.getByRole("button", { name: /^next$/i }));
    await waitFor(() => expect(lastFilter().page).toBe(2));

    await user.click(screen.getByRole("button", { name: /^filter$/i }));

    await waitFor(() => expect(lastFilter().page).toBe(1));
  });

  it("hides the pager when the whole trail fits on one page", () => {
    search.mockReturnValue(page([entry()], 1, 1));
    renderTrail();

    expect(screen.queryByRole("button", { name: /^next$/i })).not.toBeInTheDocument();
  });

  // Retention pruning deletes the old end of the trail while the auditor is sitting on it. No
  // filter changed, so nothing else would move them off a page the trail no longer has.
  it("brings the reader back when the trail shrinks under them", async () => {
    const user = userEvent.setup();
    let pruned = false;
    search.mockImplementation((filter: Record<string, unknown>) => {
      if (!pruned) return page([entry({ summary: "Incident created" })], 240, 3);
      return (filter.page as number) > 1
        ? page([], 40, 1)
        : page([entry({ summary: "Oldest surviving entry" })], 40, 1);
    });
    const { rerender } = renderTrail();

    await user.click(screen.getByRole("button", { name: /^next$/i }));
    await waitFor(() => expect(lastFilter().page).toBe(2));

    pruned = true;
    rerender(trailTree());

    await waitFor(() => expect(lastFilter().page).toBe(1));
    expect(screen.getByText("Oldest surviving entry")).toBeInTheDocument();
  });
});
