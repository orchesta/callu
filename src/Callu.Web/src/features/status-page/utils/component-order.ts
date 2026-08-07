interface Orderable {
  id: string;
  displayOrder: number;
}

/** Stored displayOrder, overridden by an in-progress drag. `dragOrder` may be stale against
 * `components`, so ids missing from it sort to the end rather than vanishing under the cursor. */
export function orderComponents<T extends Orderable>(components: readonly T[], dragOrder: string[] | null): T[] {
  const base = [...components].sort((a, b) => a.displayOrder - b.displayOrder);
  if (!dragOrder) return base;

  return base.sort((a, b) => {
    const ia = dragOrder.indexOf(a.id);
    const ib = dragOrder.indexOf(b.id);
    if (ia === -1 && ib === -1) return a.displayOrder - b.displayOrder;
    if (ia === -1) return 1;
    if (ib === -1) return -1;
    return ia - ib;
  });
}
