import "@testing-library/jest-dom/vitest";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

const deleteMutate = vi.fn();
const testMutate = vi.fn();
const testReset = vi.fn();
const testState = { data: undefined as unknown, isPending: false };

const CUSTOM = {
  id: "tpl-1",
  name: "Grafana v11",
  description: "Grafana unified alerting",
  fieldMappings: "{}",
  samplePayload: '{"title":"disk full"}',
  dataLanguage: "en",
  isBuiltIn: false,
  isActive: true,
  usageCount: 3,
};

const BUILT_IN = { ...CUSTOM, id: "tpl-2", name: "Alertmanager", isBuiltIn: true, usageCount: 0 };

const templates = { value: [CUSTOM, BUILT_IN] as unknown[] };

vi.mock("../hooks/use-webhook-templates", () => ({
  useWebhookTemplates: () => ({ data: templates.value, isLoading: false, error: null }),
  useDeleteWebhookTemplate: () => ({ mutate: deleteMutate, isPending: false }),
  useTestWebhookTemplate: () => ({ mutate: testMutate, reset: testReset, ...testState }),
}));

const { WebhookTemplatesSettings } = await import("./webhook-templates");

beforeEach(() => {
  vi.clearAllMocks();
  templates.value = [CUSTOM, BUILT_IN];
  testState.data = undefined;
  testState.isPending = false;
});

describe("the webhook templates screen", () => {
  it("lists what a service can be pointed at", () => {
    render(<WebhookTemplatesSettings />);

    expect(screen.getByText("Grafana v11")).toBeInTheDocument();
    expect(screen.getByText("Alertmanager")).toBeInTheDocument();
  });

  it("says how many services depend on a template", () => {
    render(<WebhookTemplatesSettings />);

    expect(screen.getByText(/used by 3 service/i)).toBeInTheDocument();
  });

  /** The backend refuses to delete a built-in one, so offering it would only produce a 404. */
  it("does not offer to delete a built-in template", () => {
    render(<WebhookTemplatesSettings />);

    expect(screen.getByRole("button", { name: /delete Alertmanager/i })).toBeDisabled();
    expect(screen.getByRole("button", { name: /delete Grafana v11/i })).toBeEnabled();
  });

  /** Deleting detaches the template from every service using it, which is the part that pages. */
  it("warns how many services a delete would change", async () => {
    const user = userEvent.setup();
    render(<WebhookTemplatesSettings />);

    await user.click(screen.getByRole("button", { name: /delete Grafana v11/i }));
    const dialog = await screen.findByRole("dialog");

    expect(within(dialog).getByText(/3 service\(s\) use it right now/i)).toBeInTheDocument();
    expect(deleteMutate).not.toHaveBeenCalled();
  });

  it("deletes the one the operator picked, once they confirm", async () => {
    const user = userEvent.setup();
    render(<WebhookTemplatesSettings />);

    await user.click(screen.getByRole("button", { name: /delete Grafana v11/i }));
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: /^delete$/i }));

    await waitFor(() => expect(deleteMutate).toHaveBeenCalled());
    expect(deleteMutate.mock.calls[0][0]).toBe("tpl-1");
  });

  it("runs a template against a payload without writing anything", async () => {
    const user = userEvent.setup();
    render(<WebhookTemplatesSettings />);

    await user.click(screen.getByRole("button", { name: /test Grafana v11/i }));
    await user.click(screen.getByRole("button", { name: /^run$/i }));

    await waitFor(() => expect(testMutate).toHaveBeenCalled());
    expect(testMutate.mock.calls[0][0]).toEqual({
      id: "tpl-1",
      samplePayload: '{"title":"disk full"}',
    });
  });

  it("shows which fields came out, and which did not", async () => {
    const user = userEvent.setup();
    testState.data = {
      success: true,
      mappedFields: { title: "disk full", severity: null },
    };
    render(<WebhookTemplatesSettings />);

    await user.click(screen.getByRole("button", { name: /test Grafana v11/i }));
    const dialog = await screen.findByRole("dialog");

    expect(within(dialog).getByText("disk full")).toBeInTheDocument();
    expect(within(dialog).getByText(/not mapped/i)).toBeInTheDocument();
  });

  it("shows why a payload could not be read", async () => {
    const user = userEvent.setup();
    testState.data = { success: false, errorMessage: "labels.alertname is missing", mappedFields: {} };
    render(<WebhookTemplatesSettings />);

    await user.click(screen.getByRole("button", { name: /test Grafana v11/i }));
    const dialog = await screen.findByRole("dialog");

    expect(within(dialog).getByText(/labels.alertname is missing/i)).toBeInTheDocument();
  });
});
