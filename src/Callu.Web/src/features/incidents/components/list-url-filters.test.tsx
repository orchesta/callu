import "@testing-library/jest-dom/vitest";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Routes, Route, useLocation } from "react-router";

/** A filtered view is what an operator sends to whoever is joining the call. Held only in component
 * state it cannot be linked to, does not survive a reload, and Back walks out of the page instead of
 * undoing the filter. */

const useIncidents = vi.fn();

vi.mock("../hooks/use-incidents", () => ({
  useIncidents: (filter: unknown) => useIncidents(filter),
  useBulkAcknowledge: () => ({ mutate: vi.fn(), isPending: false }),
  useBulkResolve: () => ({ mutate: vi.fn(), isPending: false }),
}));

vi.mock("@/features/dashboard/hooks/use-dashboard", () => ({
  useIncidentCounts: () => ({ data: { Open: 2, Acknowledged: 1, Resolved: 5 } }),
}));

vi.mock("@/shared/auth/auth.context", () => ({
  useAuth: () => ({ user: { role: "Admin" } }),
}));

const { IncidentsList } = await import("./list");

const INCIDENTS = [
  {
    id: "inc-1",
    title: "DB down",
    severity: "Critical",
    status: "Open",
    serviceName: "api",
    startedAt: "2026-07-15T10:00:00Z",
    acknowledgedBy: null,
  },
];

let currentSearch = "";

function SearchProbe() {
  currentSearch = useLocation().search;
  return null;
}

function renderAt(initialEntry: string) {
  return render(
    <MemoryRouter initialEntries={[initialEntry]}>
      <SearchProbe />
      <Routes>
        <Route path="/incidents" element={<IncidentsList />} />
      </Routes>
    </MemoryRouter>,
  );
}

function lastFilter() {
  return useIncidents.mock.calls[useIncidents.mock.calls.length - 1][0];
}

beforeEach(() => {
  currentSearch = "";
  useIncidents.mockReset();
  useIncidents.mockReturnValue({
    data: { items: INCIDENTS, totalCount: 1, totalPages: 1 },
    isLoading: false,
    isError: false,
  });
});

describe("incident filters and the address bar", () => {
  it("opens already filtered when the link carries one", () => {
    renderAt("/incidents?status=Open&severity=Critical");

    expect(lastFilter()).toMatchObject({ status: "Open", severity: "Critical" });
  });

  it("writes a quick filter into the URL, so the view can be sent to someone", async () => {
    const user = userEvent.setup();
    renderAt("/incidents");

    await user.click(screen.getByRole("button", { name: /^critical$/i }));

    await waitFor(() => expect(currentSearch).toContain("severity=Critical"));
  });

  it("drops the filter from the URL when it is switched off again", async () => {
    const user = userEvent.setup();
    renderAt("/incidents?severity=Critical");

    await user.click(screen.getByRole("button", { name: /^critical$/i }));

    await waitFor(() => expect(currentSearch).not.toContain("severity"));
  });

  // Page 3 of one filter is not page 3 of the next, and the operator would land on an empty list.
  it("goes back to the first page when the filter changes", async () => {
    const user = userEvent.setup();
    renderAt("/incidents?page=3");

    await user.click(screen.getByRole("button", { name: /^critical$/i }));

    await waitFor(() => expect(currentSearch).not.toContain("page"));
    expect(lastFilter()).toMatchObject({ page: 1 });
  });

  it("reads the page from the URL", () => {
    useIncidents.mockReturnValue({
      data: { items: INCIDENTS, totalCount: 60, totalPages: 3 },
      isLoading: false,
      isError: false,
    });

    renderAt("/incidents?page=2");

    expect(lastFilter()).toMatchObject({ page: 2 });
  });

  // A link made when there were four pages of incidents still gets opened a month later.
  it("falls back to the last page when the link points past the end", async () => {
    useIncidents.mockReturnValue({
      data: { items: [], totalCount: 1, totalPages: 1 },
      isLoading: false,
      isError: false,
    });

    renderAt("/incidents?page=7");

    await waitFor(() => expect(currentSearch).not.toContain("page"));
    expect(lastFilter()).toMatchObject({ page: 1 });
  });

  it("leaves a page that is still in range alone", async () => {
    useIncidents.mockReturnValue({
      data: { items: INCIDENTS, totalCount: 60, totalPages: 3 },
      isLoading: false,
      isError: false,
    });

    renderAt("/incidents?page=2");

    await waitFor(() => expect(lastFilter()).toMatchObject({ page: 2 }));
    expect(currentSearch).toContain("page=2");
  });

  it("clears everything at once", async () => {
    const user = userEvent.setup();
    renderAt("/incidents?status=Open&severity=Critical&page=2");

    await user.click(screen.getByRole("button", { name: /clear|temizle/i }));

    await waitFor(() => expect(currentSearch).toBe(""));
  });
});
