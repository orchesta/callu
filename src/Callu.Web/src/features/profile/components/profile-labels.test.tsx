import "@testing-library/jest-dom/vitest";
import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router";

/** Every field on this screen shipped with no id, no label association and no autocomplete, so a
 * screen reader announced three anonymous password boxes and a password manager could not tell
 * the current one from the new one. */

vi.mock("../hooks/use-profile", () => ({
  useProfile: () => ({
    data: { firstName: "Sam", lastName: "Rivera", email: "y@example.com", phoneNumber: "+905000000000", timezone: "Europe/Istanbul" },
    isLoading: false,
    isError: false,
  }),
  useUpdateProfile: () => ({ mutate: vi.fn(), isPending: false }),
  useChangePassword: () => ({ mutate: vi.fn(), isPending: false }),
  useNotificationPreferences: () => ({ data: null, isLoading: false }),
  useUpdateNotificationPreferences: () => ({ mutate: vi.fn(), isPending: false }),
}));

vi.mock("@/features/settings/hooks/use-settings", () => ({
  useTimezones: () => ({ data: [{ id: "Europe/Istanbul", displayName: "(UTC+03:00) Europe/Istanbul" }], isLoading: false }),
}));

vi.mock("@/shared/auth/auth.context", () => ({
  useAuth: () => ({ refreshIdentity: vi.fn() }),
}));

const { ProfilePage } = await import("./index");

function renderProfile() {
  render(
    <MemoryRouter>
      <ProfilePage />
    </MemoryRouter>,
  );
}

describe("profile form fields", () => {
  it.each([
    ["First Name", /first name/i],
    ["Last Name", /last name/i],
    ["Email", /email address/i],
    ["Timezone", /timezone/i],
  ])("%s is reachable by its visible label", (_name, pattern) => {
    renderProfile();
    expect(screen.getByLabelText(pattern)).toBeInTheDocument();
  });

  it("associates the phone label with the number box, not just the country picker", () => {
    renderProfile();
    // The library renders a country select beside it, so the match has to be pinned to the input.
    expect(screen.getByLabelText(/phone number/i, { selector: 'input[type="tel"]' })).toBeInTheDocument();
  });

  it("leaves no field on the screen without a label, quiet hours included", () => {
    renderProfile();

    const fields = [...document.querySelectorAll("input, select, textarea")];
    const unlabelled = fields.filter((el) => {
      const input = el as HTMLInputElement;
      if (input.type === "hidden") return true;
      const byId = input.id ? document.querySelector(`label[for="${CSS.escape(input.id)}"]`) : null;
      return !byId && !input.closest("label") && !input.getAttribute("aria-label") && !input.getAttribute("aria-labelledby");
    });

    expect(fields.length).toBeGreaterThan(0);
    expect(unlabelled).toHaveLength(0);
  });

  it("tells a password manager which box is which", () => {
    renderProfile();

    const current = screen.getByLabelText(/current password/i);
    const next = screen.getByLabelText(/^new password/i);
    const confirm = screen.getByLabelText(/confirm new password/i);

    expect(current).toHaveAttribute("autocomplete", "current-password");
    expect(next).toHaveAttribute("autocomplete", "new-password");
    expect(confirm).toHaveAttribute("autocomplete", "new-password");

    // Distinct ids, or the label association silently points two fields at one box.
    expect(new Set([current.id, next.id, confirm.id]).size).toBe(3);
  });
});
