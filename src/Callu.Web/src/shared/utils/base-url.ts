/**
 * Whether the public base URL is still unset, or set to an address only this machine can reach.
 *
 * The value goes into the links in outgoing emails, so getting it wrong produces notifications that
 * open nothing for anyone but the person running the server — with no error anywhere.
 */
export function isUnreachableBaseUrl(baseUrl: string | null | undefined): boolean {
  const value = baseUrl?.trim();
  if (!value) return true;

  try {
    const host = new URL(value).hostname.toLowerCase();
    return host === "localhost" || host === "127.0.0.1" || host === "::1" || host === "0.0.0.0";
  } catch {
    // Not a URL at all, which is at least as broken as an unreachable one.
    return true;
  }
}
