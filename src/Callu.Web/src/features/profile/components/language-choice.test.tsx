import "@testing-library/jest-dom/vitest";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";

/** The language a person reads the product in is also the language a page is spoken to them in, so
 * the choice has to reach the server — it used to live only in this browser's localStorage. */

const updateMutate = vi.fn();
const profile = {
  value: {
    userId: "u-1",
    firstName: "Ada",
    lastName: "Lovelace",
    email: "ada@x.io",
    phoneNumber: "+905000000000",
    timezone: "Europe/Istanbul",
    culture: null as string | null,
    createdAt: "2026-01-01T00:00:00Z",
  },
};

vi.mock("../hooks/use-profile", () => ({
  useProfile: () => ({ data: profile.value, isLoading: false, error: null }),
  useUpdateProfile: () => ({ mutate: updateMutate, isPending: false }),
  useChangePassword: () => ({ mutate: vi.fn(), isPending: false }),
  useNotificationPreferences: () => ({ data: null, isLoading: false }),
  useUpdateNotificationPreferences: () => ({ mutate: vi.fn(), isPending: false }),
}));

vi.mock("@/features/settings/hooks/use-settings", () => ({
  useTimezones: () => ({ data: [{ id: "Europe/Istanbul", displayName: "(UTC+03:00) Europe/Istanbul" }], isLoading: false }),
}));

vi.mock("@/shared/auth/auth.context", () => ({ useAuth: () => ({ refreshIdentity: vi.fn() }) }));

const { ProfilePage } = await import("./index");

function renderProfile() {
  render(
    <MemoryRouter>
      <ProfilePage />
    </MemoryRouter>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  profile.value = { ...profile.value, culture: null };
});

describe("choosing the language you are paged in", () => {
  it("offers it on the profile screen", () => {
    renderProfile();

    expect(screen.getByLabelText(/^language$/i)).toBeInTheDocument();
  });

  it("offers every language the product speaks, plus the organization default", () => {
    renderProfile();

    const picker = screen.getByLabelText(/^language$/i) as HTMLSelectElement;
    const values = [...picker.options].map((o) => o.value);

    expect(values).toContain("");
    expect(values).toContain("en-US");
    expect(values).toContain("tr-TR");
  });

  /** Unset is a real state: it is what lets the organization's language apply. */
  it("starts on the organization default when the person has not chosen", () => {
    renderProfile();

    expect(screen.getByLabelText(/^language$/i)).toHaveValue("");
  });

  it("shows the stored choice when there is one", () => {
    profile.value = { ...profile.value, culture: "tr-TR" };
    renderProfile();

    expect(screen.getByLabelText(/^language$/i)).toHaveValue("tr-TR");
  });

  it("sends the chosen culture on save", async () => {
    const user = userEvent.setup();
    renderProfile();

    await user.selectOptions(screen.getByLabelText(/^language$/i), "tr-TR");
    await user.click(screen.getByRole("button", { name: /save changes/i }));

    await waitFor(() => expect(updateMutate).toHaveBeenCalled());
    expect(updateMutate.mock.calls[0][0]).toMatchObject({ culture: "tr-TR" });
  });

  /** Going back to the organization default has to be expressible, not just "pick another one". */
  it("sends no culture when the person clears the choice", async () => {
    const user = userEvent.setup();
    profile.value = { ...profile.value, culture: "tr-TR" };
    renderProfile();

    await user.selectOptions(screen.getByLabelText(/^language$/i), "");
    await user.click(screen.getByRole("button", { name: /save changes/i }));

    await waitFor(() => expect(updateMutate).toHaveBeenCalled());
    expect(updateMutate.mock.calls[0][0].culture).toBeUndefined();
  });
});
