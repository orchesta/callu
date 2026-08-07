import { useEffect, useState } from "react";
import { onLocaleChange } from "@/shared/locales/i18n";

// Re-renders the caller when the active locale changes. The number only increments — use it as a
// memo dependency or as a `key` to remount a subtree holding already-translated strings.
export function useLocaleTick(): number {
  const [tick, setTick] = useState(0);
  useEffect(() => onLocaleChange(() => setTick((n) => n + 1)), []);
  return tick;
}
