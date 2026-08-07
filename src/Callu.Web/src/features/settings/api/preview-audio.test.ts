import { describe, it, expect, vi, beforeEach } from "vitest";

/** The rendered sample is fetched by hand, so it has to do by hand what the typed client does. */
// It leaves the envelope behind because a WAV is not one — but leaving the API base and the token
// renewal behind too means the player shows "no audio" for a render sitting right there, and only
// on the deployments where the panel and the API are not the same origin.

const refreshAccessToken = vi.fn();

vi.mock("@/shared/config", () => ({ API_URL: "https://api.example.org" }));
vi.mock("@/shared/api", () => ({ apiClient: {} }));
vi.mock("@/shared/auth/auth.service", () => ({
  authService: {
    getAccessToken: () => "access-token",
    refreshAccessToken: () => refreshAccessToken(),
  },
}));

const { communicationsApi } = await import("./communications.api");

beforeEach(() => {
  vi.restoreAllMocks();
  refreshAccessToken.mockReset();
  vi.stubGlobal("URL", { ...URL, createObjectURL: () => "blob:rendered", revokeObjectURL: () => {} });
});

describe("preview audio", () => {
  it("asks the API host, not the host the panel happens to be served from", async () => {
    const fetch = vi.fn(async () => new Response("RIFF", { status: 200 }));
    vi.stubGlobal("fetch", fetch);

    await communicationsApi.ttsPreviewAudio("0123456789abcdef0123456789abcdef");

    expect(fetch).toHaveBeenCalledWith(
      "https://api.example.org/api/v1/providers/tts/preview/audio/0123456789abcdef0123456789abcdef",
      expect.objectContaining({ headers: { Authorization: "Bearer access-token" } }),
    );
  });

  it("renews an expired token once rather than reporting the audio as unavailable", async () => {
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(new Response("", { status: 401 }))
      .mockResolvedValueOnce(new Response("RIFF", { status: 200 }));
    vi.stubGlobal("fetch", fetch);
    refreshAccessToken.mockResolvedValue(true);

    const url = await communicationsApi.ttsPreviewAudio("0123456789abcdef0123456789abcdef");

    expect(url).toBe("blob:rendered");
    expect(fetch).toHaveBeenCalledTimes(2);
  });

  it("gives up when the renewal fails, instead of retrying forever", async () => {
    const fetch = vi.fn(async () => new Response("", { status: 401 }));
    vi.stubGlobal("fetch", fetch);
    refreshAccessToken.mockResolvedValue(false);

    await expect(
      communicationsApi.ttsPreviewAudio("0123456789abcdef0123456789abcdef"),
    ).rejects.toThrow(/401/);
    expect(fetch).toHaveBeenCalledTimes(1);
  });
});
