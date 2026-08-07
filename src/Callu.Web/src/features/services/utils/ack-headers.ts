export interface AckHeader {
  key: string;
  value: string;
}

/** Round-trip between the stored ack-header JSON object and the editor's ordered key/value list; a
 * header lost here surfaces as an incident that never acknowledges, not as an error on this screen. */

/** Malformed stored JSON yields an empty list rather than throwing: a bad value must not make the settings tab unopenable. */
export function parseAckHeaders(json?: string): AckHeader[] {
  try {
    const parsed = json ? JSON.parse(json) : {};
    return Object.entries(parsed).map(([k, v]) => ({ key: k, value: String(v) }));
  } catch {
    return [];
  }
}

/**
 * Blank-keyed rows are dropped — the editor seeds a new row empty, so an untouched one must not
 * travel. Returns undefined for "no headers", which is what the API takes to mean unset.
 */
export function serializeAckHeaders(headers: AckHeader[]): string | undefined {
  const headersObj: Record<string, string> = {};
  headers.forEach((h) => {
    if (h.key.trim()) headersObj[h.key.trim()] = h.value;
  });
  return Object.keys(headersObj).length > 0 ? JSON.stringify(headersObj) : undefined;
}
