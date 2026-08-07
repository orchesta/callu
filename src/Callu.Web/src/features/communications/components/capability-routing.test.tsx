import "@testing-library/jest-dom/vitest";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

/** Voice and video can sit on different providers, so each channel picks its own. */

const routes = vi.fn();
const setRoute = vi.fn();

vi.mock("@/features/settings/hooks/use-communications", () => ({
  useCapabilityRoutes: () => routes(),
  useSetCapabilityRoute: () => ({ mutate: setRoute, isPending: false }),
}));

const { CapabilityRoutingSection } = await import("./CapabilityRoutingSection");

const VOX = "11111111-1111-4111-8111-111111111111";
const VOICE_BOX = "22222222-2222-4222-8222-222222222222";

function candidate(id: string, name: string, isEnabled = true) {
  return { providerId: id, name, providerType: "x", isEnabled };
}

function route(over: Record<string, unknown> = {}) {
  return {
    capability: "VoiceCalls",
    providerId: null,
    providerName: null,
    providerType: null,
    isProviderEnabled: false,
    candidates: [candidate(VOX, "Voximplant"), candidate(VOICE_BOX, "Self-hosted voice")],
    ...over,
  };
}

function answer(items: unknown[]) {
  return { data: items, isLoading: false };
}

describe("routing a channel to a named provider", () => {
  beforeEach(() => {
    routes.mockReset();
    setRoute.mockReset();
  });

  it("offers only the providers that declare the channel", () => {
    routes.mockReturnValue(answer([
      route(),
      route({ capability: "VideoConference", candidates: [candidate(VOX, "Voximplant")] }),
    ]));

    render(<CapabilityRoutingSection />);

    expect(screen.getByText(/voice calls/i)).toBeInTheDocument();
    expect(screen.getByText(/video conference/i)).toBeInTheDocument();
    // The lookup shipped keyed by the enum's numbers while the API sends its names, so every
    // row read "Channel" — the fallback — and nothing said so.
    expect(screen.queryByText("Channel")).not.toBeInTheDocument();
  });

  it("sends the provider the operator picked for that channel", async () => {
    const user = userEvent.setup({ delay: null });
    routes.mockReturnValue(answer([route()]));

    render(<CapabilityRoutingSection />);

    await user.click(screen.getByRole("combobox"));
    await user.click(await screen.findByText("Self-hosted voice"));

    await waitFor(() =>
      expect(setRoute).toHaveBeenCalledWith({ capability: "VoiceCalls", providerId: VOICE_BOX }));
  });

  it("clears the pin when the operator goes back to automatic", async () => {
    const user = userEvent.setup({ delay: null });
    routes.mockReturnValue(answer([route({ providerId: VOICE_BOX, isProviderEnabled: true })]));

    render(<CapabilityRoutingSection />);

    await user.click(screen.getByRole("combobox"));
    await user.click(await screen.findByText(/automatic/i));

    await waitFor(() =>
      expect(setRoute).toHaveBeenCalledWith({ capability: "VoiceCalls", providerId: null }));
  });

  /// A channel pinned to a switched-off provider is carried by nobody.
  it("warns when the pinned provider is switched off", () => {
    routes.mockReturnValue(answer([route({ providerId: VOICE_BOX, isProviderEnabled: false })]));

    render(<CapabilityRoutingSection />);

    expect(screen.getByText(/nothing carries it/i)).toBeInTheDocument();
  });

  /// The server drops a channel nothing can carry, so an empty candidate list means the pinned
  /// provider went away — which is a warning, not an ordinary state.
  it("warns when a pinned channel has lost the provider that carried it", () => {
    routes.mockReturnValue(answer([route({ providerId: VOICE_BOX, isProviderEnabled: true, candidates: [] })]));

    render(<CapabilityRoutingSection />);

    expect(screen.getByText(/no longer supports it, or has been removed/i)).toBeInTheDocument();
  });
});
