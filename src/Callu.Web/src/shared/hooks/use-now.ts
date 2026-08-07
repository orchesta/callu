import { useEffect, useState } from "react";

/** The current epoch time, re-read every `tickMs` while `active`. */
// The first tick is aligned to the epoch boundary, so two cards reading the same rate turn over on
// the same instant instead of drifting up to a whole tick apart.
export function useNow(tickMs: number, active = true): number {
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    if (!active) return;

    let interval = 0;
    const timeout = window.setTimeout(
      () => {
        setNow(Date.now());
        interval = window.setInterval(() => setNow(Date.now()), tickMs);
      },
      tickMs - (Date.now() % tickMs),
    );

    return () => {
      window.clearTimeout(timeout);
      window.clearInterval(interval);
    };
  }, [active, tickMs]);

  return now;
}
