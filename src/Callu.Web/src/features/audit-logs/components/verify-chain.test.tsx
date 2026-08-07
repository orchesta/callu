import "@testing-library/jest-dom/vitest";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";

const verifyMutate = vi.fn();
const verifyState = { data: undefined as unknown, isPending: false };

vi.mock("../hooks/use-audit-logs", () => ({
  useAuditLogSearch: () => ({
    data: { items: [], totalCount: 0, page: 1, pageSize: 25, totalPages: 0 },
    isLoading: false,
    error: null,
  }),
  useVerifyAuditChain: () => ({ mutate: verifyMutate, ...verifyState }),
}));

const { AuditLogList } = await import("./list");

function renderList() {
  return render(
    <MemoryRouter initialEntries={["/audit-logs"]}>
      <AuditLogList />
    </MemoryRouter>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  verifyState.data = undefined;
  verifyState.isPending = false;
});

describe("verifying the audit chain", () => {
  it("is offered on the audit log screen", () => {
    renderList();

    expect(screen.getByRole("button", { name: /verify integrity/i })).toBeInTheDocument();
  });

  it("runs the check when asked", async () => {
    const user = userEvent.setup();
    renderList();

    await user.click(screen.getByRole("button", { name: /verify integrity/i }));

    await waitFor(() => expect(verifyMutate).toHaveBeenCalled());
  });

  it("says how many entries were checked when the chain holds", () => {
    verifyState.data = { intact: true, checkedCount: 1204, firstBrokenSequence: null, reason: null };
    renderList();

    expect(screen.getByRole("status")).toHaveTextContent(/1204 entries verified/i);
  });

  /** A break has to name the entry — "something is wrong" is not something an auditor can act on. */
  it("names the entry the chain breaks at, and why", () => {
    verifyState.data = {
      intact: false,
      checkedCount: 41,
      firstBrokenSequence: 42,
      reason: "This entry's contents no longer match its hash.",
    };
    renderList();

    const status = screen.getByRole("status");
    expect(status).toHaveTextContent(/breaks at entry 42/i);
    expect(status).toHaveTextContent(/no longer match its hash/i);
  });

  it("shows nothing until the check has been run", () => {
    renderList();

    expect(screen.queryByRole("status")).not.toBeInTheDocument();
  });
});
