/**
 * Copies text, reporting whether it actually landed on the clipboard.
 */
// navigator.clipboard is gated on a secure context, and Callu's default deployment is plain HTTP,
// so on most installs it is simply absent. Callers must not paint "Copied" before this resolves.
export async function copyText(text: string): Promise<boolean> {
  if (navigator.clipboard?.writeText) {
    try {
      await navigator.clipboard.writeText(text);
      return true;
    } catch {
      // Permission denied, an extension in the way, or a broken OS clipboard: fall through.
    }
  }

  return copyViaTextarea(text);
}

function copyViaTextarea(text: string): boolean {
  const previous = document.activeElement as HTMLElement | null;
  const area = document.createElement('textarea');

  try {
    area.value = text;
    area.setAttribute('readonly', '');
    area.style.position = 'fixed';
    area.style.opacity = '0';
    document.body.appendChild(area);
    area.select();

    return document.execCommand?.('copy') ?? false;
  } catch {
    return false;
  } finally {
    area.remove();
    previous?.focus();
  }
}
