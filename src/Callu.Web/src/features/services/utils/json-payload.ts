/** Flattens a webhook sample payload into pickable paths and resolves one back to a value; both halves
 * must agree on the `$.a.b` / `[n]` syntax, so they live together. */

export interface ParsedField {
  path: string;
  value: string;
  type: string;
}

/**
 * Flattens `obj` into one entry per leaf. Only leaves are emitted: a key whose value is an
 * object or array is traversed, not listed, because a mapping can only target a scalar.
 */
export function extractFields(obj: unknown, prefix = "$"): ParsedField[] {
  const fields: ParsedField[] = [];

  const traverse = (current: unknown, path: string) => {
    if (current === null || current === undefined) return;

    if (Array.isArray(current)) {
      current.forEach((item: unknown, index: number) => {
        traverse(item, `${path}[${index}]`);
      });
    } else if (typeof current === "object") {
      const record = current as Record<string, unknown>;
      Object.keys(record).forEach((key) => {
        const newPath = `${path}.${key}`;
        const val = record[key];
        if (typeof val === "object" && val !== null) {
          traverse(val, newPath);
        } else {
          fields.push({
            path: newPath,
            value: String(val),
            type: typeof val,
          });
        }
      });
    }
  };

  traverse(obj, prefix);
  return fields;
}

/** Walks a path produced by extractFields against `obj`, returning undefined for every kind of
 * non-resolving path, which callers therefore cannot tell apart. */
export function resolveJsonPath(obj: Record<string, unknown>, mapping: string): unknown {
  const segments = mapping.replace("$.", "").split(".");
  let value: unknown = obj;
  for (const segment of segments) {
    if (value === null || typeof value !== "object") return undefined;
    const record = value as Record<string, unknown>;
    const arrayMatch = segment.match(/(.+)\[(\d+)\]/);
    if (arrayMatch) {
      const arr = record[arrayMatch[1]];
      value = Array.isArray(arr) ? arr[parseInt(arrayMatch[2])] : undefined;
    } else {
      value = record[segment];
    }
  }
  return value;
}
