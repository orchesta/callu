import { describe, it, expect, vi, beforeEach } from "vitest";
import { QueryClient } from "@tanstack/react-query";

/** Deleting invalidated every key under the feature, including the detail query of the row that had
 * just been removed — which was still mounted, so it refetched a gone id and answered 404. */

const removed: unknown[][] = [];
const invalidated: unknown[][] = [];

function fakeClient() {
  return {
    removeQueries: ({ queryKey }: { queryKey: unknown[] }) => removed.push(queryKey),
    invalidateQueries: ({ queryKey }: { queryKey: unknown[] }) => invalidated.push(queryKey),
  } as unknown as QueryClient;
}

const client = fakeClient();
const captured: { onSuccess?: (data: unknown, vars: unknown) => void } = {};

vi.mock("@tanstack/react-query", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@tanstack/react-query")>()),
  useQueryClient: () => client,
}));

vi.mock("@/shared/api", () => ({
  apiQueryOptions: (queryKey: unknown[], queryFn: unknown) => ({ queryKey, queryFn }),
  useApiMutation: (_fn: unknown, options: typeof captured) => {
    captured.onSuccess = options.onSuccess;
    return { mutate: vi.fn() };
  },
}));

vi.mock("../api/email-templates.api", () => ({ emailTemplateApi: { delete: vi.fn(), getAll: vi.fn(), getById: vi.fn() } }));
vi.mock("../api/webhook-templates.api", () => ({ webhookTemplateApi: { delete: vi.fn(), getAll: vi.fn(), getById: vi.fn() } }));

const { useDeleteEmailTemplate, emailTemplateKeys } = await import("./use-email-templates");
const { useDeleteWebhookTemplate, webhookTemplateKeys } = await import("./use-webhook-templates");

beforeEach(() => {
  removed.length = 0;
  invalidated.length = 0;
  captured.onSuccess = undefined;
});

describe("deleting a template", () => {
  it("drops the deleted email template's detail query instead of refetching it", () => {
    useDeleteEmailTemplate();
    captured.onSuccess?.(undefined, "tpl-1");

    expect(removed).toContainEqual(emailTemplateKeys.detail("tpl-1"));
    expect(invalidated).toContainEqual(emailTemplateKeys.list());
    expect(invalidated).not.toContainEqual(emailTemplateKeys.all);
  });

  it("drops the deleted webhook template's detail query instead of refetching it", () => {
    useDeleteWebhookTemplate();
    captured.onSuccess?.(undefined, "tpl-2");

    expect(removed).toContainEqual(webhookTemplateKeys.detail("tpl-2"));
    expect(invalidated).toContainEqual(webhookTemplateKeys.lists());
    expect(invalidated).not.toContainEqual(webhookTemplateKeys.all);
  });

  /** Services carry the template they use, so their list has to be re-read either way. */
  it("still refreshes the services that referenced the webhook template", () => {
    useDeleteWebhookTemplate();
    captured.onSuccess?.(undefined, "tpl-2");

    expect(invalidated).toContainEqual(["services"]);
  });
});
