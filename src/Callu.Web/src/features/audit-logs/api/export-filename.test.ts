import { describe, it, expect, vi, beforeEach } from "vitest";

/** A download whose name has no extension is a file the operating system will not open, so whatever
 * the server sends — or fails to send — the saved name has to end in the format that was asked for. */

const saved: string[] = [];

vi.mock("@/shared/config", () => ({ API_URL: "" }));
vi.mock("@/shared/api", () => ({ apiClient: {} }));
vi.mock("@/shared/auth/auth.service", () => ({
  authService: { getAccessToken: () => "token", refreshAccessToken: async () => false },
}));

const { downloadAuditExport } = await import("./audit-log.api");

function respondWith(disposition: string | null) {
  const headers = new Headers();
  if (disposition !== null) headers.set("content-disposition", disposition);

  vi.stubGlobal("fetch", vi.fn(async () => new Response("a,b\n1,2", { status: 200, headers })));
}

beforeEach(() => {
  saved.length = 0;
  vi.restoreAllMocks();

  vi.stubGlobal("URL", { ...URL, createObjectURL: () => "blob:x", revokeObjectURL: () => {} });

  // A real anchor, so the href setter behaves; only the click is intercepted, since jsdom would
  // otherwise try to navigate to the blob URL.
  const createElement = document.createElement.bind(document);
  vi.spyOn(document, "createElement").mockImplementation(((tag: string) => {
    const element = createElement(tag);
    if (tag === "a") {
      element.click = () => saved.push((element as HTMLAnchorElement).download);
    }
    return element;
  }) as typeof document.createElement);
});

describe("naming the downloaded export", () => {
  it("uses the name the server sent", async () => {
    respondWith('attachment; filename="audit-20260729-103526.csv"');

    await downloadAuditExport("csv", new URLSearchParams());

    expect(saved[0]).toBe("audit-20260729-103526.csv");
  });

  /** Behind a proxy the header can be stripped; the file still has to be openable. */
  it("still names the file when the server sent no name at all", async () => {
    respondWith(null);

    await downloadAuditExport("jsonl", new URLSearchParams());

    expect(saved[0]).toMatch(/\.jsonl$/);
  });

  it("adds the extension when the server's name has none", async () => {
    respondWith('attachment; filename="audit-export"');

    await downloadAuditExport("csv", new URLSearchParams());

    expect(saved[0]).toBe("audit-export.csv");
  });

  it("does not double the extension when the name already has it", async () => {
    respondWith('attachment; filename="report.csv"');

    await downloadAuditExport("csv", new URLSearchParams());

    expect(saved[0]).toBe("report.csv");
  });

  it("reads an unquoted name", async () => {
    respondWith("attachment; filename=audit.jsonl");

    await downloadAuditExport("jsonl", new URLSearchParams());

    expect(saved[0]).toBe("audit.jsonl");
  });

  /** The encoded form sits after the plain one, so a naive `filename=` match captures `*=UTF-8''…`
   * and produces a name with no usable extension. */
  it("prefers the encoded name over the parameter that precedes it", async () => {
    respondWith(`attachment; filename="audit.csv"; filename*=UTF-8''denetim-kayd%C4%B1.csv`);

    await downloadAuditExport("csv", new URLSearchParams());

    expect(saved[0]).toBe("denetim-kaydı.csv");
  });
});
