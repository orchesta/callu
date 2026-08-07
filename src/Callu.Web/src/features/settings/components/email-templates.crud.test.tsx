import "@testing-library/jest-dom/vitest";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";

const createMutate = vi.fn();
const deleteMutate = vi.fn();

const SYSTEM = {
  id: "t-1",
  key: "incident-created",
  name: "Incident created",
  subject: "New incident",
  isSystem: true,
  isActive: true,
  createdAt: "2026-07-01T00:00:00Z",
};

const CUSTOM = { ...SYSTEM, id: "t-2", key: "weekly-digest", name: "Weekly digest", isSystem: false };

const list = { value: [SYSTEM, CUSTOM] as unknown[] };
const detail = {
  value: {
    ...SYSTEM,
    htmlBody: "<p>Hello {{UserName}}, incident {{IncidentTitle}} is open.</p>",
    variables: ["UserName", "IncidentTitle"],
  } as unknown,
};

vi.mock("../hooks/use-email-templates", () => ({
  useEmailTemplates: () => ({ data: list.value }),
  useEmailTemplate: () => ({ data: detail.value }),
  useUpdateEmailTemplate: () => ({ mutate: vi.fn(), mutateAsync: vi.fn(), isPending: false }),
  useSendTestEmail: () => ({ mutate: vi.fn(), mutateAsync: vi.fn(), isPending: false }),
  useCreateEmailTemplate: () => ({ mutate: createMutate, isPending: false }),
  useDeleteEmailTemplate: () => ({ mutate: deleteMutate, isPending: false }),
}));

const { EmailTemplates } = await import("./email-templates");

function renderScreen() {
  render(
    <MemoryRouter>
      <EmailTemplates />
    </MemoryRouter>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  list.value = [SYSTEM, CUSTOM];
});

describe("adding and removing an email template", () => {
  it("offers a way to add one", () => {
    renderScreen();

    expect(screen.getByRole("button", { name: /new template/i })).toBeInTheDocument();
  });

  /** The server refuses to delete a built-in one; the first template is selected on load. */
  it("does not offer to delete a built-in template", () => {
    renderScreen();

    expect(screen.getByRole("button", { name: /delete Incident created/i })).toBeDisabled();
  });

  it("will not create a template with an empty field", async () => {
    const user = userEvent.setup();
    renderScreen();

    await user.click(screen.getByRole("button", { name: /new template/i }));
    const dialog = await screen.findByRole("dialog");

    expect(within(dialog).getByRole("button", { name: /^create$/i })).toBeDisabled();
  });

  it("creates the template the operator filled in", async () => {
    const user = userEvent.setup();
    renderScreen();

    await user.click(screen.getByRole("button", { name: /new template/i }));
    const dialog = await screen.findByRole("dialog");

    await user.type(within(dialog).getByLabelText(/^name$/i), "Weekly digest");
    await user.type(within(dialog).getByLabelText(/^key$/i), "weekly-digest");
    await user.type(within(dialog).getByLabelText(/^subject$/i), "This week");
    await user.type(within(dialog).getByLabelText(/html body/i), "<p>hi</p>");
    await user.click(within(dialog).getByRole("button", { name: /^create$/i }));

    await waitFor(() => expect(createMutate).toHaveBeenCalled());
    expect(createMutate.mock.calls[0][0]).toEqual({
      name: "Weekly digest",
      key: "weekly-digest",
      subject: "This week",
      htmlBody: "<p>hi</p>",
    });
  });

  /** The key addresses the template, so a stray capital or space would create one nothing sends. */
  it("keeps the key lowercase and untrimmed of meaning", async () => {
    const user = userEvent.setup();
    renderScreen();

    await user.click(screen.getByRole("button", { name: /new template/i }));
    const dialog = await screen.findByRole("dialog");
    const key = within(dialog).getByLabelText(/^key$/i);

    await user.type(key, "Weekly-Digest");

    expect(key).toHaveValue("weekly-digest");
  });
});

describe("previewing an email template", () => {
  /** The editor previews the body being edited, and a raw {{Variable}} tells the operator nothing
   * about what the email will look like. */
  it("fills the variables in the way the test email does", async () => {
    const user = userEvent.setup();
    renderScreen();

    await user.click(screen.getByRole("tab", { name: /preview/i }));

    const frame = (await screen.findByTitle(/preview/i)) as HTMLIFrameElement;

    expect(frame.getAttribute("srcdoc")).toContain("[UserName]");
    expect(frame.getAttribute("srcdoc")).toContain("[IncidentTitle]");
    expect(frame.getAttribute("srcdoc")).not.toContain("{{");
  });
});
