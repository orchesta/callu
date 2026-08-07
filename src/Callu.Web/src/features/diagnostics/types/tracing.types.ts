
export type TracingUnavailableReason = 'no-endpoint' | 'unreachable';

export interface BuildIdentity {
  version: string;
  informationalVersion: string;
}

export interface TracingStatus {
  available: boolean;
  reason: TracingUnavailableReason | null;
  services: string[];
}

export interface TraceSummary {
  traceId: string;
  rootService: string;
  rootOperation: string;
  startedAt: string;
  durationMs: number;
  spanCount: number;
  errorCount: number;
}

export interface OperationStats {
  operation: string;
  service: string;
  traceCount: number;
  avgDurationMs: number;
  p95DurationMs: number;
  maxDurationMs: number;
  errorCount: number;
  lastSeen: string;
}

export interface TracingOverview {
  sampleSize: number;
  totalErrors: number;
  p95DurationMs: number;
  maxDurationMs: number;
  operations: OperationStats[];
}

export interface TraceSpan {
  spanId: string;
  parentSpanId: string | null;
  service: string;
  operation: string;
  startedAt: string;
  startOffsetMs: number;
  durationMs: number;
  hasError: boolean;
  tags: Record<string, string>;
}

export interface TraceDetail {
  traceId: string;
  startedAt: string;
  durationMs: number;
  spans: TraceSpan[];
}

export interface TraceSearchFilters {
  service?: string;
  operation?: string;
  lookbackMinutes?: number;
  limit?: number;
  onlyErrors?: boolean;
  minDurationMs?: number;
}
