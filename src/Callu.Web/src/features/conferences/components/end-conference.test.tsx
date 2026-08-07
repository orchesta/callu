import "@testing-library/jest-dom/vitest";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";

/** A crisis room stays open until someone closes it. The screen was read-only, so the only way to
 * end one was the API — while everyone still in it kept the room and the recording alive. */

const endMutate = vi.fn();
const refetch = vi.fn();
const useAuth = vi.fn();

const ACTIVE = {
  id: "room-1",
  incidentId: "inc-1",
  incidentTitle: "Payment API down",
  status: "Active",
  startedAt: "2026-07-20T12:00:00Z",
  participantCount: 2,
  recordingEnabled: false,
};

const ENDED = { ...ACTIVE, id: "room-2", incidentTitle: "DB unreachable", status: "Ended", endedAt: "2026-07-20T13:00:00Z" };

const rooms = { value: [ACTIVE] as unknown[] };

vi.mock("../hooks/use-conferences", () => ({
  useConferences: () => ({
    data: { items: rooms.value, totalCount: rooms.value.length, page: 1, pageSize: 25, totalPages: 1 },
    isLoading: false,
    error: null,
    refetch,
  }),
}));

vi.mock("@/features/conference/hooks/use-conference", () => ({
  useEndConference: () => ({ mutate: endMutate, isPending: false }),
}));

vi.mock("../api/conference.api", () => ({
  conferenceApi: { getJoinInfo: vi.fn() },
}));

vi.mock("@/shared/auth/auth.context", () => ({ useAuth: () => useAuth() }));

const { ConferenceList } = await import("./list");

function renderList() {
  render(
    <MemoryRouter>
      <ConferenceList />
    </MemoryRouter>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  rooms.value = [ACTIVE];
  useAuth.mockReturnValue({ user: { role: "Admin" } });
});

describe("ending a conference", () => {
  it("offers it on a live room, named so it is clear which one", () => {
    renderList();

    expect(screen.getByRole("button", { name: /end the conference for Payment API down/i })).toBeInTheDocument();
  });

  it("is not offered on a room that already ended", () => {
    rooms.value = [ENDED];
    renderList();

    expect(screen.queryByRole("button", { name: /end the conference/i })).not.toBeInTheDocument();
  });

  it("asks before disconnecting everyone", async () => {
    const user = userEvent.setup();
    renderList();

    await user.click(screen.getByRole("button", { name: /end the conference for/i }));

    expect(endMutate).not.toHaveBeenCalled();
    expect(await screen.findByRole("dialog")).toBeInTheDocument();
  });

  it("ends the room the operator picked, once they confirm", async () => {
    const user = userEvent.setup();
    renderList();

    await user.click(screen.getByRole("button", { name: /end the conference for/i }));
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: /^end$/i }));

    await waitFor(() => expect(endMutate).toHaveBeenCalled());
    expect(endMutate.mock.calls[0][0]).toBe("room-1");
  });

  it("a viewer cannot end a conference", () => {
    useAuth.mockReturnValue({ user: { role: "Viewer" } });
    renderList();

    expect(screen.queryByRole("button", { name: /end the conference/i })).not.toBeInTheDocument();
  });
});
