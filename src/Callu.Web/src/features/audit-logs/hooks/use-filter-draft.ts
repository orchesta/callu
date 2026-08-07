import { useCallback, useEffect, useState } from 'react';
import type { AuditFilterDraft } from '../utils/filter-draft';

/** Draft-versus-applied filter state plus the page it is read on. */
// Every change to the applied filter answers a different question, so it starts at page one.
export function useAuditFilterDraft<T extends AuditFilterDraft>(empty: T, initial?: () => T) {
  const [draft, setDraft] = useState<T>(initial ?? empty);
  const [applied, setApplied] = useState<T>(initial ?? empty);
  const [page, setPage] = useState(1);

  const set = (key: keyof T, value: string) => setDraft((d) => ({ ...d, [key]: value }));
  const apply = () => { setApplied(draft); setPage(1); };
  const applyNow = useCallback((next: T) => { setDraft(next); setApplied(next); setPage(1); }, []);
  const clear = () => applyNow(empty);

  return { draft, applied, page, setPage, set, apply, applyNow, clear };
}

/** Pulls the reader back to the first page once the trail is shorter than the page they are on. */
// Retention pruning shortens the trail with no filter change, so nothing else moves the page.
export function usePageWithinBounds(page: number, totalPages: number, setPage: (page: number) => void) {
  useEffect(() => {
    if (totalPages > 0 && page > totalPages) setPage(1);
  }, [page, totalPages, setPage]);
}
