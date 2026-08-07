import "@testing-library/jest-dom/vitest";
import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";

/** The editor is the widest form in the app: a condition is three controls plus a remove button on
 * one line. It rendered at the base dialog width and scrolled sideways, hiding the save button. */

vi.mock("@/features/teams/hooks/use-teams", () => ({ useTeams: () => ({ data: [] }) }));
vi.mock("@/features/users/hooks/use-users", () => ({ useUsers: () => ({ data: [] }) }));
vi.mock("@/features/escalations/hooks/use-escalations", () => ({ useEscalationPolicies: () => ({ data: [] }) }));

const { AlertRuleEditor } = await import("./alert-rule-editor");

function renderEditor() {
  render(
    <AlertRuleEditor
      open
      onOpenChange={vi.fn()}
      editingRule={null}
      onSave={vi.fn()}
      isSaving={false}
      metadata={undefined}
      teams={[]}
      users={[]}
      escalations={[]}
    />,
  );
}

describe("alert rule editor layout", () => {
  it("widens itself at a breakpoint the base dialog cannot override", () => {
    renderEditor();

    const dialog = screen.getByRole("dialog");

    // The base component keeps its own sm:max-w-lg; this one has to win at the same breakpoint.
    expect(dialog.className).toMatch(/\bsm:max-w-\w+/);
    expect(dialog.className).not.toContain("sm:max-w-lg");
  });

  it("lets a condition row wrap instead of forcing the dialog to scroll sideways", () => {
    renderEditor();

    const field = screen.getByLabelText(/condition 1 field/i);
    const row = field.closest("div.flex");

    expect(row).not.toBeNull();
    expect(row!.className).toContain("flex-wrap");
  });

  it("names the buttons that remove a condition and an action", () => {
    renderEditor();

    expect(screen.getByRole("button", { name: /remove condition 1/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /remove action 1/i })).toBeInTheDocument();
  });
});
