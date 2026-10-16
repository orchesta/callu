import "@testing-library/jest-dom/vitest";
import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { TransitionDialog } from "./transition-dialog";

/** The profile needs a resolution type on resolve and a reason on close and reopen; the dialog will not
 * confirm without them, and sends nothing the person did not type. */
describe("TransitionDialog", () => {
  it("will not resolve until a resolution type is chosen", async () => {
    const onConfirm = vi.fn();
    render(<TransitionDialog mode="resolve" pending={false} onCancel={vi.fn()} onConfirm={onConfirm} />);

    const confirm = screen.getByRole("button", { name: /resolve/i });
    expect(confirm).toBeDisabled();

    await userEvent.selectOptions(screen.getByRole("combobox"), "permanent-fix");
    await userEvent.click(confirm);

    expect(onConfirm).toHaveBeenCalledWith({ resolutionType: "permanent-fix", reason: undefined });
  });

  it("will not close or reopen without a reason, and trims it", async () => {
    const onConfirm = vi.fn();
    render(<TransitionDialog mode="reopen" pending={false} onCancel={vi.fn()} onConfirm={onConfirm} />);

    const confirm = screen.getByRole("button", { name: /reopen/i });
    await userEvent.type(screen.getByRole("textbox"), "   ");
    expect(confirm).toBeDisabled();

    await userEvent.type(screen.getByRole("textbox"), " Aynı hata tekrar görüldü. ");
    await userEvent.click(confirm);

    expect(onConfirm).toHaveBeenCalledWith({ reason: "Aynı hata tekrar görüldü." });
  });

  it("keeps close disabled for a whitespace-only reason", async () => {
    render(<TransitionDialog mode="close" pending={false} onCancel={vi.fn()} onConfirm={vi.fn()} />);

    await userEvent.type(screen.getByRole("textbox"), "     ");

    expect(screen.getByRole("button", { name: /close incident/i })).toBeDisabled();
  });

  it("accepts a reason of exactly 500 characters and refuses 501", async () => {
    render(<TransitionDialog mode="close" pending={false} onCancel={vi.fn()} onConfirm={vi.fn()} />);
    const confirm = screen.getByRole("button", { name: /close incident/i });

    await userEvent.click(screen.getByRole("textbox"));
    await userEvent.paste("x".repeat(500));
    expect(confirm).toBeEnabled();

    await userEvent.paste("x");
    expect(confirm).toBeDisabled();
  });

  it("ignores Escape while pending, and honours it otherwise", async () => {
    const onCancel = vi.fn();
    const { rerender } = render(<TransitionDialog mode="close" pending onCancel={onCancel} onConfirm={vi.fn()} />);

    await userEvent.keyboard("{Escape}");
    expect(onCancel).not.toHaveBeenCalled();

    rerender(<TransitionDialog mode="close" pending={false} onCancel={onCancel} onConfirm={vi.fn()} />);
    await userEvent.keyboard("{Escape}");
    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  it("refuses a reason over the limit", async () => {
    render(<TransitionDialog mode="close" pending={false} onCancel={vi.fn()} onConfirm={vi.fn()} />);

    await userEvent.click(screen.getByRole("textbox"));
    await userEvent.paste("x".repeat(501));

    expect(screen.getByRole("button", { name: /close incident/i })).toBeDisabled();
  });
});
