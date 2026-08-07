import "@testing-library/jest-dom/vitest";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

/** A carrier whose auth account differs from the SIP user is configured here or nowhere. */
// Every other layer carries the field — the column, the DTO, the request body callu-voice reads —
// and this screen is the only way a carrier is set at all.

const createTrunk = vi.fn();
const updateTrunk = vi.fn();

const { SipTrunkSection } = await import("./SipTrunkSection");

function renderSection(sipTrunks: unknown[] = []) {
    return render(
        <SipTrunkSection
            sipTrunks={sipTrunks as never}
            createSipTrunkMutation={{ mutateAsync: createTrunk, isPending: false } as never}
            updateSipTrunkMutation={{ mutateAsync: updateTrunk, isPending: false } as never}
            isSaving={false}
            onRequestDelete={vi.fn()}
        />,
    );
}

const storedTrunk = {
    id: "t1",
    name: "Carrier A",
    server: "sip.carrier.example",
    port: 5060,
    username: "900001",
    authUser: "auth-900001",
    callerId: "+905551234567",
    useTls: false,
    useTcp: false,
    isEnabled: true,
};

describe("the SIP trunk screen and the carrier's auth account", () => {
    beforeEach(() => {
        createTrunk.mockReset().mockResolvedValue({});
        updateTrunk.mockReset().mockResolvedValue({});
    });

    it("sends the auth username it was given", async () => {
        const user = userEvent.setup({ delay: null });
        renderSection();

        await user.click(screen.getByRole("button", { name: /add trunk/i }));
        await user.type(screen.getByPlaceholderText(/primary sip trunk/i), "Carrier A");
        await user.type(screen.getByPlaceholderText(/sip\.provider\.com/i), "sip.carrier.example");
        await user.type(screen.getByPlaceholderText(/same as username/i), "auth-900001");
        await user.type(screen.getByPlaceholderText(/\+905551234567/i), "+905551234567");
        await user.click(screen.getByRole("button", { name: /create sip trunk/i }));

        await waitFor(() => expect(createTrunk).toHaveBeenCalled());
        expect(createTrunk.mock.calls[0][0].authUser).toBe("auth-900001");
    });

    // A save for an unrelated reason used to send no auth user at all, and the field it was
    // never told about was written as null.
    it("carries a stored auth username back through an unrelated edit", async () => {
        const user = userEvent.setup({ delay: null });
        renderSection([storedTrunk]);

        await user.click(screen.getByRole("button", { name: /edit/i }));
        await user.type(screen.getByPlaceholderText(/primary sip trunk/i), " renamed");
        await user.click(screen.getByRole("button", { name: /update sip trunk/i }));

        await waitFor(() => expect(updateTrunk).toHaveBeenCalled());
        expect(updateTrunk.mock.calls[0][0].authUser).toBe("auth-900001");
    });

    // Switching the trunk row off is one of the documented ways to remove a carrier, and an
    // omitted flag used to read as "on" at the other end.
    it("does not switch a disabled trunk back on through an unrelated edit", async () => {
        const user = userEvent.setup({ delay: null });
        renderSection([{ ...storedTrunk, isEnabled: false }]);

        await user.click(screen.getByRole("button", { name: /edit/i }));
        await user.click(screen.getByRole("button", { name: /update sip trunk/i }));

        await waitFor(() => expect(updateTrunk).toHaveBeenCalled());
        expect(updateTrunk.mock.calls[0][0].isEnabled).toBe(false);
    });
});
