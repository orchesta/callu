import "@testing-library/jest-dom/vitest";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";

/** The self-hosted voice provider has to be configurable from the panel like any other. */
// It reached the type dropdown with no fields behind it, so choosing it gave a provider that
// could never dial: no service address, no token, no callback.

const createProvider = vi.fn();
const updateProvider = vi.fn();

const { ProviderSection } = await import("./ProviderSection");

function renderSection(providers: unknown[] = []) {
  return render(
    <MemoryRouter>
    <ProviderSection
      providers={providers as never}
      sipTrunks={[]}
      createProviderMutation={{ mutateAsync: createProvider, isPending: false } as never}
      updateProviderMutation={{ mutateAsync: updateProvider, isPending: false } as never}
      isSaving={false}
      onRequestDelete={vi.fn()}
    />
    </MemoryRouter>,
  );
}

async function openTheFormForVoice(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getAllByRole("button", { name: /add provider/i })[0]);
  await user.click(screen.getByRole("combobox", { name: /provider type/i }));
  await user.click(await screen.findByText(/self-hosted voice/i));
}

describe("configuring the self-hosted voice provider from the panel", () => {
  beforeEach(() => {
    createProvider.mockReset().mockResolvedValue({});
    updateProvider.mockReset().mockResolvedValue({});
  });

  it("offers the three things it cannot dial without", async () => {
    const user = userEvent.setup({ delay: null });
    renderSection();

    await openTheFormForVoice(user);

    expect(screen.getByLabelText(/service address/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/api token/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/callback address/i)).toBeInTheDocument();
  });

  it("sends what was typed as the provider's config", async () => {
    const user = userEvent.setup({ delay: null });
    renderSection();

    await openTheFormForVoice(user);
    await user.type(screen.getByLabelText(/name \*/i), "House voice");
    await user.type(screen.getByLabelText(/service address/i), "http://callu-voice:8090");
    await user.type(screen.getByLabelText(/api token/i), "s3cr3t");
    await user.type(screen.getByLabelText(/callback address/i), "http://callu-api:5095");
    await user.click(screen.getByRole("button", { name: /create provider/i }));

    await waitFor(() => expect(createProvider).toHaveBeenCalled());
    const sent = createProvider.mock.calls[0][0];
    expect(sent.providerType).toBe("callu-voice");
    expect(sent.config.baseUrl).toBe("http://callu-voice:8090");
    expect(sent.config.apiToken).toBe("s3cr3t");
    expect(sent.config.callbackUrl).toBe("http://callu-api:5095");
  });

  /// The carrier is pushed to the voice service and applied without a restart, so it belongs here.
  it("offers the SIP trunk, and sends the chosen one with the provider", async () => {
    const user = userEvent.setup({ delay: null });
    render(
      <MemoryRouter>
        <ProviderSection
          providers={[] as never}
          sipTrunks={[{ id: "t1", name: "Carrier A" }] as never}
          createProviderMutation={{ mutateAsync: createProvider, isPending: false } as never}
          updateProviderMutation={{ mutateAsync: updateProvider, isPending: false } as never}
          isSaving={false}
          onRequestDelete={vi.fn()}
        />
      </MemoryRouter>,
    );

    await openTheFormForVoice(user);
    await user.type(screen.getByLabelText(/name \*/i), "House voice");
    await user.type(screen.getByLabelText(/service address/i), "http://callu-voice:8090");
    await user.type(screen.getByLabelText(/callback address/i), "http://callu-api:5095");

    await user.click(screen.getByLabelText(/sip trunk/i));
    await user.click(await screen.findByText("Carrier A"));
    await user.click(screen.getByRole("button", { name: /create provider/i }));

    await waitFor(() => expect(createProvider).toHaveBeenCalled());
    expect(createProvider.mock.calls[0][0].sipTrunkId).toBe("t1");
  });

  /// Saving is the only thing that takes a carrier off the voice service, so it has to be predictable.
  // Following the documented setup order — provider first, trunk second — used to send "no carrier"
  // and destroy a working one, behind a success toast.
  it("warns before a save that would remove the carrier, and only then", async () => {
    const user = userEvent.setup({ delay: null });
    const withTrunk = {
      id: "p1",
      name: "House voice",
      providerType: "callu-voice",
      isEnabled: true,
      priority: 1,
      capabilities: 1,
      sipTrunkId: "t1",
      calluVoice: { baseUrl: "http://callu-voice:8090", callbackUrl: "http://callu-api:5095", voice: null, requestTimeoutSeconds: null, hasApiToken: true },
    };

    render(
      <MemoryRouter>
        <ProviderSection
          providers={[withTrunk] as never}
          sipTrunks={[{ id: "t1", name: "Carrier A" }] as never}
          createProviderMutation={{ mutateAsync: createProvider, isPending: false } as never}
          updateProviderMutation={{ mutateAsync: updateProvider, isPending: false } as never}
          isSaving={false}
          onRequestDelete={vi.fn()}
        />
      </MemoryRouter>,
    );

    await user.click(screen.getByTitle(/edit/i));
    expect(screen.queryByText(/removes the carrier/i)).not.toBeInTheDocument();

    await user.click(screen.getByLabelText(/sip trunk/i));
    await user.click(await screen.findByText(/not set/i));

    expect(screen.getByText(/removes the carrier/i)).toBeInTheDocument();
  });

  /// A provider that has no trunk is not removing one, so nothing warns and nothing is at stake.
  it("does not warn on a provider that has no trunk to lose", async () => {
    const user = userEvent.setup({ delay: null });
    renderSection([{
      id: "p1",
      name: "House voice",
      providerType: "callu-voice",
      isEnabled: true,
      priority: 1,
      capabilities: 1,
      sipTrunkId: null,
      calluVoice: { baseUrl: "http://callu-voice:8090", callbackUrl: "http://callu-api:5095", voice: null, requestTimeoutSeconds: null, hasApiToken: true },
    }]);

    await user.click(screen.getByTitle(/edit/i));

    expect(screen.queryByText(/removes the carrier/i)).not.toBeInTheDocument();
  });

  it("does not offer a test SMS on a provider that only makes calls", async () => {
    const user = userEvent.setup({ delay: null });
    renderSection([{
      id: "p1",
      name: "House voice",
      providerType: "callu-voice",
      isEnabled: true,
      priority: 1,
      capabilities: 1,
      calluVoice: { baseUrl: "http://callu-voice:8090", callbackUrl: null, voice: null, requestTimeoutSeconds: null, hasApiToken: true },
    }]);

    await user.click(screen.getByTitle(/edit/i));

    expect(screen.queryByText(/send test sms/i)).not.toBeInTheDocument();
  });

  /// A stored credential is never handed back to whoever opens the form.
  it("does not prefill the stored token, and keeps it when the field is left blank", async () => {
    const user = userEvent.setup({ delay: null });
    renderSection([{
      id: "p1",
      name: "House voice",
      providerType: "callu-voice",
      isEnabled: true,
      priority: 1,
      capabilities: 1,
      calluVoice: { baseUrl: "http://callu-voice:8090", callbackUrl: "http://callu-api:5095", voice: null, requestTimeoutSeconds: null, hasApiToken: true },
    }]);

    await user.click(screen.getByTitle(/edit/i));

    expect(screen.getByLabelText(/api token/i)).toHaveValue("");

    await user.click(screen.getByRole("button", { name: /update provider/i }));

    await waitFor(() => expect(updateProvider).toHaveBeenCalled());
    expect(updateProvider.mock.calls[0][0].config).not.toHaveProperty("apiToken");
  });
});
