import type { OperationStats } from "../types/tracing.types";

/**
 * A request that stays open by design: a SignalR hub connection, or anything upgraded to a
 * WebSocket. Its span lasts as long as the browser tab does.
 */
export function isLongLivedConnection(operation: string): boolean {
  const target = operation.toLowerCase();
  return target.includes("/hubs/") || target.includes("websocket");
}

/**
 * The slowest operation worth reporting, ignoring connections that are long by design.
 *
 * A hub connection open for eleven minutes is not a slow request, but it is always the largest
 * duration in the set, so it pins the headline figure and flattens every bar next to it.
 */
export function slowestOperation(operations: readonly OperationStats[]): OperationStats | null {
  return operations
    .filter((op) => !isLongLivedConnection(op.operation))
    .reduce<OperationStats | null>(
      (worst, op) => (!worst || op.p95DurationMs > worst.p95DurationMs ? op : worst),
      null,
    );
}
