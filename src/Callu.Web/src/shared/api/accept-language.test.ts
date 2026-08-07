import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";

/** The backend picks its language from Accept-Language. Sending nothing hands that choice to the
 * browser's own language setting, which is a different setting and often a different language:
 * an English screen answering in Turkish is what that looks like. */

const getLocale = vi.fn(() => "en");

vi.mock("@/shared/locales/i18n", () => ({
  getLocale,
  cultureFor: (code: string) => (code === "tr" ? "tr-TR" : "en-US"),
}));

vi.mock("../auth/auth.service", () => ({
  authService: {
    getAccessToken: () => null,
    onUnauthorized: () => () => {},
  },
}));

const { apiClient } = await import("./client");

function headersOfLastCall(): Headers {
  const calls = (globalThis.fetch as ReturnType<typeof vi.fn>).mock.calls;
  const [, init] = calls[calls.length - 1];
  return new Headers((init as RequestInit).headers);
}

beforeEach(() => {
  getLocale.mockReturnValue("en");
  globalThis.fetch = vi.fn(async () =>
    new Response(JSON.stringify({ success: true, data: {} }), {
      status: 200,
      headers: { "Content-Type": "application/json" },
    }),
  ) as unknown as typeof fetch;
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe("Accept-Language", () => {
  it("asks for the language the app is showing", async () => {
    await apiClient.get("/anything");

    expect(headersOfLastCall().get("Accept-Language")).toBe("en");
  });

  it("follows the user switching language, without a reload", async () => {
    getLocale.mockReturnValue("tr");

    await apiClient.get("/anything");

    expect(headersOfLastCall().get("Accept-Language")).toBe("tr");
  });
});
