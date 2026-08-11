import "@testing-library/jest-dom/vitest";
import type { ReactNode } from "react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Routes, Route } from "react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

const createMutate = vi.fn();
const updateTemplateMutate = vi.fn();
const fromCaptureMutate = vi.fn();
const setServiceTemplateMutate = vi.fn();
const updateIntegrationMutate = vi.fn();

const serviceData = { value: undefined as unknown };
const integrationData = { value: undefined as unknown };

vi.mock("@/features/services/hooks/use-services", () => ({
  useService: () => ({ data: serviceData.value }),
}));

vi.mock("@/features/services/hooks/use-captures", () => ({
  useCapture: () => ({ data: undefined }),
}));

vi.mock("@/features/services/hooks/use-webhook-settings", () => ({
  useSetWebhookTemplate: () => ({ mutate: setServiceTemplateMutate, isPending: false }),
}));

vi.mock("@/features/settings/hooks/use-webhook-templates", () => ({
  useWebhookTemplate: () => ({ data: undefined }),
  useCreateWebhookTemplate: () => ({ mutate: createMutate, isPending: false }),
  useUpdateWebhookTemplate: () => ({ mutate: updateTemplateMutate, isPending: false }),
  useCreateWebhookTemplateFromCapture: () => ({ mutate: fromCaptureMutate, isPending: false }),
}));

vi.mock("@/features/applications/hooks/use-integrations", () => ({
  useIntegration: (id: string | undefined) => ({ data: id ? integrationData.value : undefined }),
  useUpdateIntegration: () => ({ mutate: updateIntegrationMutate, isPending: false }),
  integrationKeys: { all: ["integrations"] },
}));

const { ApplicationTemplateEditor } = await import("./template-editor");
const { WebhookTemplateEditor } = await import(
  "@/features/services/components/webhook-template-editor"
);

const INTEGRATION = {
  id: "int-1",
  name: "Payments Gateway",
  description: "gateway hooks",
  teamId: "team-1",
  isActive: true,
  webhookEnabled: true,
  listeningMode: true,
};

function renderAt(element: ReactNode, url: string, pattern: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[url]}>
        <Routes>
          <Route path={pattern} element={element} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

async function save(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole("button", { name: /save template/i }));
}

beforeEach(() => {
  vi.clearAllMocks();
  serviceData.value = undefined;
  integrationData.value = undefined;
  createMutate.mockImplementation((_vars, opts) => opts?.onSuccess?.({ id: "tpl-new" }));
  fromCaptureMutate.mockImplementation((_vars, opts) => opts?.onSuccess?.({ id: "tpl-cap" }));
  updateIntegrationMutate.mockImplementation((_vars, opts) => opts?.onSettled?.());
  setServiceTemplateMutate.mockImplementation((_vars, opts) => opts?.onSettled?.());
});

describe("application scope", () => {
  it("saves a capture through from-capture alone — the backend attaches it", async () => {
    const user = userEvent.setup({ pointerEventsCheck: 0 });
    integrationData.value = INTEGRATION;
    renderAt(
      <ApplicationTemplateEditor />,
      "/applications/int-1/template?captureId=cap-1",
      "/applications/:id/template",
    );

    await save(user);

    expect(fromCaptureMutate).toHaveBeenCalledTimes(1);
    expect(fromCaptureMutate.mock.calls[0][0]).toMatchObject({ captureId: "cap-1" });
    expect(createMutate).not.toHaveBeenCalled();
    expect(updateIntegrationMutate).not.toHaveBeenCalled();
    expect(setServiceTemplateMutate).not.toHaveBeenCalled();
  });

  it("creates a hand-written template, then points the application at it", async () => {
    const user = userEvent.setup({ pointerEventsCheck: 0 });
    integrationData.value = INTEGRATION;
    renderAt(
      <ApplicationTemplateEditor />,
      "/applications/int-1/template",
      "/applications/:id/template",
    );

    await user.type(screen.getByPlaceholderText(/prometheus alerts/i), "My Template");
    await save(user);

    expect(createMutate).toHaveBeenCalledTimes(1);
    expect(updateIntegrationMutate).toHaveBeenCalledTimes(1);
    expect(createMutate.mock.invocationCallOrder[0]).toBeLessThan(
      updateIntegrationMutate.mock.invocationCallOrder[0],
    );

    const vars = updateIntegrationMutate.mock.calls[0][0];
    expect(vars).toMatchObject({
      id: "int-1",
      name: "Payments Gateway",
      description: "gateway hooks",
      teamId: "team-1",
      webhookTemplateId: "tpl-new",
      isActive: true,
      webhookEnabled: true,
    });
    expect(vars).not.toHaveProperty("listeningMode");
    expect(setServiceTemplateMutate).not.toHaveBeenCalled();
    expect(fromCaptureMutate).not.toHaveBeenCalled();
  });
});

describe("service scope", () => {
  it("still creates a template and attaches it to the service", async () => {
    const user = userEvent.setup({ pointerEventsCheck: 0 });
    serviceData.value = { id: "svc-1", name: "Checkout" };
    renderAt(<WebhookTemplateEditor />, "/services/svc-1/template", "/services/:id/template");

    await user.type(screen.getByPlaceholderText(/prometheus alerts/i), "My Template");
    await save(user);

    expect(createMutate).toHaveBeenCalledTimes(1);
    expect(setServiceTemplateMutate).toHaveBeenCalledTimes(1);
    expect(setServiceTemplateMutate.mock.calls[0][0]).toEqual({
      serviceId: "svc-1",
      templateId: "tpl-new",
    });
    expect(updateIntegrationMutate).not.toHaveBeenCalled();
    expect(fromCaptureMutate).not.toHaveBeenCalled();
  });
});
