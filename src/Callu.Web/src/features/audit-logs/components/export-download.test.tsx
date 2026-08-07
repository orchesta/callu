import "@testing-library/jest-dom/vitest";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";

/** The export was an <a href>, which a browser follows without an Authorization header — so every
 * click answered 401. It has to be fetched with the token and handed to the browser as a file. */

const downloadExport = vi.fn();

vi.mock("../hooks/use-audit-logs", () => ({
  useAuditLogSearch: () => ({
    data: { items: [], totalCount: 0, page: 1, pageSize: 25, totalPages: 0 },
    isLoading: false,
    error: null,
  }),
  useVerifyAuditChain: () => ({ mutate: vi.fn(), data: undefined, isPending: false }),
}));

vi.mock("../api/audit-log.api", () => ({
  auditLogApi: {},
  downloadAuditExport: (format: string, params: URLSearchParams) => downloadExport(format, params),
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
  downloadExport.mockResolvedValue(undefined);
});

describe("downloading the audit export", () => {
  it("is a button, not a link the browser would follow unauthenticated", () => {
    renderList();

    const csv = screen.getByRole("button", { name: /export csv/i });

    expect(csv.tagName).toBe("BUTTON");
    expect(document.querySelector('a[href*="audit-logs/export"]')).toBeNull();
  });

  it("fetches the export when asked", async () => {
    const user = userEvent.setup();
    renderList();

    await user.click(screen.getByRole("button", { name: /export csv/i }));

    await waitFor(() => expect(downloadExport).toHaveBeenCalled());
    expect(downloadExport.mock.calls[0][0]).toBe("csv");
  });

  it("offers both formats", async () => {
    const user = userEvent.setup();
    renderList();

    await user.click(screen.getByRole("button", { name: /export jsonl/i }));

    await waitFor(() => expect(downloadExport).toHaveBeenCalled());
    expect(downloadExport.mock.calls[0][0]).toBe("jsonl");
  });

  /** An export of the whole trail when the operator narrowed the screen to one day is a different
   * document from the one they asked for. */
  it("sends the filters the screen is showing", async () => {
    const user = userEvent.setup();
    renderList();

    await user.click(screen.getByLabelText(/^action$/i));
    await user.click(await screen.findByRole("option", { name: "LoginFailed" }));
    await user.type(screen.getByLabelText(/^search$/i), "operator");
    await user.type(screen.getByLabelText(/entity/i), "Incident");
    await user.click(screen.getByRole("button", { name: /^filter$/i }));
    await user.click(screen.getByRole("button", { name: /export csv/i }));

    await waitFor(() => expect(downloadExport).toHaveBeenCalled());

    const params = downloadExport.mock.calls[0][1];
    expect(params.get("action")).toBe("LoginFailed");
    expect(params.get("query")).toBe("operator");
    expect(params.get("resourceType")).toBe("Incident");
  });

  /** A filter typed but not applied is not what the screen is showing. */
  it("does not send a filter the operator has not applied", async () => {
    const user = userEvent.setup();
    renderList();

    await user.type(screen.getByLabelText(/^search$/i), "operator");
    await user.click(screen.getByRole("button", { name: /export csv/i }));

    await waitFor(() => expect(downloadExport).toHaveBeenCalled());
    expect(downloadExport.mock.calls[0][1].get("query")).toBeNull();
  });

  it("says so when the export cannot be produced", async () => {
    const user = userEvent.setup();
    downloadExport.mockRejectedValue(new Error("HTTP 500"));
    renderList();

    await user.click(screen.getByRole("button", { name: /export csv/i }));

    await waitFor(() => expect(downloadExport).toHaveBeenCalled());
    expect(screen.getByRole("button", { name: /export csv/i })).toBeEnabled();
  });
});
