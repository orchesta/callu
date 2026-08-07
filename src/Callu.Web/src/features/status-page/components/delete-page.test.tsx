import "@testing-library/jest-dom/vitest";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

const deleteMutate = vi.fn();

const PAGE = { id: "sp-1", name: "Acme Status", slug: "acme", isPublic: true };

const idle = { data: undefined, isLoading: false, isPending: false, mutate: vi.fn(), error: null };

// The screen and its dialogs pull in every hook this feature exports; only the ones this spec is
// about get real behaviour, and the rest answer with an idle mutation.
vi.mock("../hooks/use-status-pages", () => ({
  useStatusPages: () => ({ data: [PAGE], isLoading: false }),
  useStatusPage: () => ({ data: { ...PAGE, description: "", components: [], incidents: [] } }),
  useStatusPageStats: () => ({ data: undefined }),
  useStatusPageUptime: () => ({ data: undefined, isLoading: false }),
  useStatusPageSubscribers: () => ({ data: [], isLoading: false }),
  useDeleteStatusPage: () => ({ mutate: deleteMutate, isPending: false }),
  useStatusPageBySlug: () => idle,
  useCreateStatusPage: () => idle,
  useUpdateStatusPage: () => idle,
  useAddComponent: () => idle,
  useUpdateComponent: () => idle,
  useRemoveComponent: () => idle,
  useTestHealthCheck: () => idle,
  useSniffHealthCheck: () => idle,
  useCreateStatusIncident: () => idle,
  useAddIncidentUpdate: () => idle,
  useNotifyStatusPageSubscribers: () => idle,
  useRecordStatusPageView: () => idle,
  useSubscribeToStatusPage: () => idle,
  useRemoveSubscriber: () => idle,
}));

const { StatusPageManagement } = await import("./manage");

function renderScreen() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <StatusPageManagement />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

beforeEach(() => vi.clearAllMocks());

describe("deleting a status page", () => {
  it("is offered on the manage screen", () => {
    renderScreen();

    expect(screen.getByRole("button", { name: /delete this status page/i })).toBeInTheDocument();
  });

  /** The public URL is the thing customers have bookmarked, so this is not a one-click action. */
  it("asks first, and says what goes with it", async () => {
    const user = userEvent.setup();
    renderScreen();

    await user.click(screen.getByRole("button", { name: /delete this status page/i }));
    const dialog = await screen.findByRole("dialog");

    expect(within(dialog).getByText(/public URL stops working/i)).toBeInTheDocument();
    expect(deleteMutate).not.toHaveBeenCalled();
  });

  it("deletes the page once the operator confirms", async () => {
    const user = userEvent.setup();
    renderScreen();

    await user.click(screen.getByRole("button", { name: /delete this status page/i }));
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: /^delete$/i }));

    await waitFor(() => expect(deleteMutate).toHaveBeenCalled());
    expect(deleteMutate.mock.calls[0][0]).toBe("sp-1");
  });
});
