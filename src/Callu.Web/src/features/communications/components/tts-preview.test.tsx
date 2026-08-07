import "@testing-library/jest-dom/vitest";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

/** Hearing what a call will say, before one is placed. */
// Every wrong prompt this product has shipped was invisible until a phone rang. What makes this
// section worth having is the spoken text coming back — the typed text proves nothing.

const previewMutate = vi.fn();
const fetchAudio = vi.fn();

vi.mock("@/features/settings/hooks/use-communications", () => ({
  usePreviewTts: () => ({ mutateAsync: previewMutate, isPending: false }),
}));

vi.mock("@/features/settings/api/communications.api", () => ({
  communicationsApi: { ttsPreviewAudio: (key: string) => fetchAudio(key) },
}));

const { TtsPreviewSection } = await import("./TtsPreviewSection");

beforeEach(() => {
  previewMutate.mockReset();
  fetchAudio.mockReset();
  fetchAudio.mockResolvedValue("blob:audio");
  vi.stubGlobal("URL", { ...URL, createObjectURL: () => "blob:x", revokeObjectURL: () => {} });
});

describe("TTS preview", () => {
  it("sends each segment with the language it was written in", async () => {
    previewMutate.mockResolvedValue([]);
    const user = userEvent.setup();
    render(<TtsPreviewSection />);

    await user.type(screen.getByLabelText(/segment text|parça metni/i), "Onaylamak için 1'e basın.");
    await user.click(screen.getByRole("button", { name: /speak it|seslendir/i }));

    await waitFor(() => expect(previewMutate).toHaveBeenCalled());
    expect(previewMutate).toHaveBeenCalledWith({
      segments: [{ text: "Onaylamak için 1'e basın.", lang: "tr-TR" }],
    });
  });

  // The normalized text is the whole point: it is what the synthesizer said, not what was typed.
  it("shows what the synthesizer actually said", async () => {
    previewMutate.mockResolvedValue([
      { key: "abc", normalized: "Onaylamak için bire basın.", duration_s: 2.5 },
    ]);
    const user = userEvent.setup();
    render(<TtsPreviewSection />);

    await user.type(screen.getByLabelText(/segment text|parça metni/i), "Onaylamak için 1'e basın.");
    await user.click(screen.getByRole("button", { name: /speak it|seslendir/i }));

    expect(await screen.findByText("Onaylamak için bire basın.")).toBeInTheDocument();
  });

  it("does not call the service when nothing was typed", async () => {
    const user = userEvent.setup();
    render(<TtsPreviewSection />);

    await user.click(screen.getByRole("button", { name: /speak it|seslendir/i }));

    expect(previewMutate).not.toHaveBeenCalled();
  });

  // A render that failed must not leave the previous one on screen: it would read as this text's result.
  it("clears the previous render when a new one fails", async () => {
    previewMutate.mockResolvedValueOnce([
      { key: "abc", normalized: "Birinci deneme.", duration_s: 1 },
    ]);
    const user = userEvent.setup();
    render(<TtsPreviewSection />);

    const textarea = screen.getByLabelText(/segment text|parça metni/i);
    await user.type(textarea, "bir");
    await user.click(screen.getByRole("button", { name: /speak it|seslendir/i }));
    expect(await screen.findByText("Birinci deneme.")).toBeInTheDocument();

    previewMutate.mockRejectedValueOnce(new Error("synthesizer refused"));
    await user.type(textarea, " iki");
    await user.click(screen.getByRole("button", { name: /speak it|seslendir/i }));

    await waitFor(() => expect(screen.queryByText("Birinci deneme.")).not.toBeInTheDocument());
  });
});
