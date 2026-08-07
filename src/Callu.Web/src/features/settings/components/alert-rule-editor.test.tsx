import "@testing-library/jest-dom/vitest";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

/** The rules this editor saves decide who gets paged, so these pin that an invalid rule never reaches
 * onSave, a valid one arrives verbatim, editing seeds from the existing rule, and conditions apply. */

const toast = { error: vi.fn(), success: vi.fn(), warning: vi.fn(), info: vi.fn() };
vi.mock("@/shared/utils/toast", () => ({ toast }));

const { AlertRuleEditor } = await import("./alert-rule-editor");

const metadata = {
  conditionFields: [
    { value: "Severity", label: "Severity" },
    { value: "ServiceName", label: "Service Name" },
  ],
  conditionOperators: [
    { value: "Equals", label: "Equals" },
    { value: "Contains", label: "Contains" },
  ],
  actionTypes: [
    { value: "AutoEscalate", label: "Auto Escalate" },
    { value: "AddNote", label: "Add Note" },
    { value: "SuppressNotification", label: "Suppress Notification" },
  ],
  severityValues: ["Low", "High", "Critical"],
};

function renderEditor(props: Partial<React.ComponentProps<typeof AlertRuleEditor>> = {}) {
  const onSave = vi.fn();
  const onOpenChange = vi.fn();
  render(
    <AlertRuleEditor
      open
      onOpenChange={onOpenChange}
      editingRule={null}
      metadata={metadata}
      teams={[{ id: "team-1", name: "DB team" }]}
      users={[{ id: "user-1", firstName: "Ada", lastName: "Lovelace", email: "ada@acme.io" }]}
      escalations={[{ id: "esc-1", name: "Primary policy" }]}
      onSave={onSave}
      isSaving={false}
      {...props}
    />,
  );
  return { onSave, onOpenChange };
}

const NAME = /Auto-escalate critical payment incidents/i;

beforeEach(() => vi.clearAllMocks());

describe("AlertRuleEditor — seeding", () => {
  it("opens a new rule with the create title and an empty name", () => {
    renderEditor();
    expect(screen.getByText("Create Alert Rule")).toBeInTheDocument();
    expect(screen.getByPlaceholderText(NAME)).toHaveValue("");
  });

  it("seeds the form from the rule being edited", () => {
    renderEditor({
      editingRule: {
        id: "rule-1",
        name: "Page on critical",
        description: "",
        isEnabled: true,
        priority: 5,
        conditions: [{ field: "Severity", operator: "Equals", value: "Critical" }],
        actions: [{ type: "AutoEscalate", target: "esc-1", value: "" }],
        triggerCount: 0,
        createdAt: "2026-01-01T00:00:00Z",
      },
    });
    expect(screen.getByText("Edit Rule")).toBeInTheDocument();
    expect(screen.getByPlaceholderText(NAME)).toHaveValue("Page on critical");
    expect(screen.getByDisplayValue("5")).toBeInTheDocument();
  });
});

describe("AlertRuleEditor — validation gate", () => {
  it("does not save a rule with no name, and reports the error", async () => {
    const user = userEvent.setup();
    const { onSave } = renderEditor();

    // The default new rule already has a valid condition and action; only the name is missing.
    await user.click(screen.getByRole("button", { name: /Create Rule/i }));

    await waitFor(() => expect(toast.error).toHaveBeenCalled());
    expect(onSave).not.toHaveBeenCalled();
  });

  it("saves once the name is filled, passing exactly what was entered", async () => {
    const user = userEvent.setup();
    const { onSave } = renderEditor();

    await user.type(screen.getByPlaceholderText(NAME), "Page on critical");
    await user.click(screen.getByRole("button", { name: /Create Rule/i }));

    await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1));
    expect(onSave.mock.calls[0][0]).toMatchObject({
      name: "Page on critical",
      priority: 100,
      isEnabled: true,
      conditions: [{ field: "Severity", operator: "Equals", value: "Critical" }],
      actions: [{ type: "AutoEscalate", target: "", value: "" }],
    });
    expect(toast.error).not.toHaveBeenCalled();
  });
});

describe("AlertRuleEditor — conditions", () => {
  it("starts with one condition, and Add appends another row", async () => {
    const user = userEvent.setup();
    renderEditor();

    expect(screen.getByLabelText(/Condition 1 field/i)).toBeInTheDocument();
    expect(screen.queryByLabelText(/Condition 2 field/i)).not.toBeInTheDocument();

    // The first "Add" button belongs to the Conditions section.
    await user.click(screen.getAllByRole("button", { name: /^Add$/i })[0]);

    expect(await screen.findByLabelText(/Condition 2 field/i)).toBeInTheDocument();
  });

  it("removes a condition row when its remove button is clicked", async () => {
    const user = userEvent.setup();
    renderEditor();

    await user.click(screen.getAllByRole("button", { name: /^Add$/i })[0]);
    const secondField = await screen.findByLabelText(/Condition 2 field/i);

    // The remove button lives in the same row as this field's select; scope to that row so the
    // action rows' remove buttons are not in play.
    const row = secondField.closest("div.flex")!;
    const remove = within(row as HTMLElement).getAllByRole("button").find((b) => b.querySelector("svg.lucide-x"))!;
    await user.click(remove);

    await waitFor(() => expect(screen.queryByLabelText(/Condition 2 field/i)).not.toBeInTheDocument());
  });
});
