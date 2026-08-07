import { useEffect, useMemo, useState } from "react";
import { Link } from "react-router";
import { t } from "@/shared/locales/i18n";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Badge } from "@/shared/components/ui/badge";
import { Card } from "@/shared/components/ui/card";
import { Tabs, TabsList, TabsTrigger, TabsContent } from "@/shared/components/ui/tabs";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from "@/shared/components/ui/dialog";
import { Activity, AlertTriangle, ChevronRight, RefreshCw, Search } from "lucide-react";
import { LoadingState } from "@/shared/components/loading-state";
import { ErrorState } from "@/shared/components/error-state";
import { EmptyState } from "@/shared/components/empty-state";
import { PageHeader } from "@/shared/components/page-header";
import { useBuildIdentity, useTrace, useTraces, useTracingOverview, useTracingStatus } from "../hooks/use-tracing";
import type { OperationStats, TraceSpan, TraceSummary } from "../types/tracing.types";
import { dateLocale } from "@/shared/utils/datetime";
import { slowestOperation } from "../utils/slowest";
import { isUnreachableBaseUrl } from "@/shared/utils/base-url";
import { useOrganizationSettings } from "@/features/settings/hooks/use-settings";

const TRACE_BACKEND_SERVICE = "jaeger-all-in-one";

const LOOKBACK_OPTIONS = ["15", "60", "360", "1440"] as const;
const LIMIT_OPTIONS = ["25", "50", "100"] as const;

function formatDuration(ms: number): string {
  if (ms < 1) return `${(ms * 1000).toFixed(0)}µs`;
  if (ms < 1000) return `${ms.toFixed(ms < 10 ? 2 : 0)}ms`;
  return `${(ms / 1000).toFixed(2)}s`;
}

function formatTimestamp(iso: string): string {
  return new Date(iso).toLocaleString(dateLocale(), {
    month: "short",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
  });
}

function SummaryCard({ label, value, hint, tone }: {
  label: string;
  value: string;
  hint?: string;
  tone?: "error";
}) {
  return (
    <Card className="p-4">
      <div className="text-xs text-muted-foreground">{label}</div>
      <div className={`mt-1 text-2xl font-semibold tabular-nums ${tone === "error" ? "text-error-500" : ""}`}>
        {value}
      </div>
      {hint && <div className="mt-0.5 truncate text-xs text-muted-foreground">{hint}</div>}
    </Card>
  );
}

function SpanBar({ span, traceDurationMs }: { span: TraceSpan; traceDurationMs: number }) {
  const scale = traceDurationMs > 0 ? traceDurationMs : 1;
  const left = Math.min((span.startOffsetMs / scale) * 100, 100);
  const width = Math.max((span.durationMs / scale) * 100, 0.5);

  return (
    <div className="grid grid-cols-[minmax(0,16rem)_1fr_auto] items-center gap-3 py-1">
      <div className="min-w-0">
        <div className="flex items-center gap-1.5">
          {span.hasError && <AlertTriangle className="h-3.5 w-3.5 shrink-0 text-error-500" />}
          <span className="truncate text-sm">{span.operation}</span>
        </div>
        <span className="truncate text-xs text-muted-foreground">{span.service}</span>
      </div>

      <div className="relative h-4 rounded bg-muted/40">
        <div
          className={`absolute top-0 h-full rounded ${span.hasError ? "bg-error-500" : "bg-brand-500"}`}
          style={{ left: `${left}%`, width: `${width}%` }}
          title={`+${formatDuration(span.startOffsetMs)} · ${formatDuration(span.durationMs)}`}
        />
      </div>

      <span className="w-16 shrink-0 text-right tabular-nums text-xs text-muted-foreground">
        {formatDuration(span.durationMs)}
      </span>
    </div>
  );
}

function TraceDetailDialog({ traceId, onClose }: { traceId: string | null; onClose: () => void }) {
  const { data: trace, isLoading, error } = useTrace(traceId);

  return (
    <Dialog open={Boolean(traceId)} onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-4xl">
        <DialogHeader>
          <DialogTitle>{t("diagnostics.traceDetail")}</DialogTitle>
        </DialogHeader>

        {isLoading && <LoadingState />}
        {error && <ErrorState message={t("diagnostics.traceLoadFailed")} />}

        {trace && (
          <div className="space-y-4">
            <div className="flex flex-wrap gap-x-6 gap-y-1 text-sm">
              <span className="text-muted-foreground">
                {t("diagnostics.duration")}:{" "}
                <span className="font-medium text-foreground">{formatDuration(trace.durationMs)}</span>
              </span>
              <span className="text-muted-foreground">
                {t("diagnostics.spans")}:{" "}
                <span className="font-medium text-foreground">{trace.spans.length}</span>
              </span>
              <span className="font-mono text-xs text-muted-foreground">{trace.traceId}</span>
            </div>

            <div className="max-h-[55vh] overflow-y-auto pr-1">
              {trace.spans.map((span) => (
                <SpanBar key={span.spanId} span={span} traceDurationMs={trace.durationMs} />
              ))}
            </div>
          </div>
        )}
      </DialogContent>
    </Dialog>
  );
}

function OperationRow({ op, slowestMs, onDrill }: {
  op: OperationStats;
  slowestMs: number;
  onDrill: (operation: string) => void;
}) {
  const width = slowestMs > 0 ? Math.max((op.p95DurationMs / slowestMs) * 100, 1) : 0;

  return (
    <button
      type="button"
      onClick={() => onDrill(op.operation)}
      className="grid w-full grid-cols-[minmax(0,1fr)_4rem_5rem_5rem_8rem_1rem] items-center gap-3 px-4 py-2 text-left hover:bg-muted/40"
    >
      <div className="min-w-0">
        <div className="flex items-center gap-1.5">
          {op.errorCount > 0 && <AlertTriangle className="h-3.5 w-3.5 shrink-0 text-error-500" />}
          <span className="truncate text-sm font-medium">{op.operation}</span>
          {op.errorCount > 0 && (
            <Badge className="border-error-500/20 bg-error-500/10 text-error-500">
              {op.errorCount}
            </Badge>
          )}
        </div>
        <span className="text-xs text-muted-foreground">
          {op.service} · {t("diagnostics.lastSeen")} {formatTimestamp(op.lastSeen)}
        </span>
      </div>

      <span className="text-right tabular-nums text-sm">{op.traceCount}</span>
      <span className="text-right tabular-nums text-sm text-muted-foreground">
        {formatDuration(op.avgDurationMs)}
      </span>
      <span className="text-right tabular-nums text-sm font-medium">
        {formatDuration(op.p95DurationMs)}
      </span>

      <div className="relative h-1.5 rounded bg-muted/40">
        <div
          className={`absolute left-0 top-0 h-full rounded ${op.errorCount > 0 ? "bg-error-500" : "bg-brand-500"}`}
          style={{ width: `${width}%` }}
        />
      </div>

      <ChevronRight className="h-4 w-4 text-muted-foreground" />
    </button>
  );
}

function BuildStrip({ version, informationalVersion }: { version: string; informationalVersion: string }) {
  return (
    <p className="text-xs text-muted-foreground font-mono" data-testid="diagnostics-build">
      {t("diagnostics.buildLabel")}: {version}
      {informationalVersion !== version && (
        <span className="text-muted-foreground/80"> · {informationalVersion}</span>
      )}
    </p>
  );
}

export function TraceExplorer() {
  const { data: status, isLoading: statusLoading, error: statusError } = useTracingStatus();
  const { data: build } = useBuildIdentity();
  const { data: orgSettings } = useOrganizationSettings();

  const [service, setService] = useState<string>("");
  const [lookback, setLookback] = useState<string>("60");
  const [tab, setTab] = useState<string>("grouped");

  const [operation, setOperation] = useState("");
  const [appliedOperation, setAppliedOperation] = useState<string | undefined>(undefined);
  const [limit, setLimit] = useState<string>("50");
  const [onlyErrors, setOnlyErrors] = useState(false);
  const [selected, setSelected] = useState<string | null>(null);

  const services = useMemo(() => {
    const all = status?.services ?? [];
    const withoutBackend = all.filter((name) => name !== TRACE_BACKEND_SERVICE);
    return withoutBackend.length > 0 ? withoutBackend : all;
  }, [status]);

  useEffect(() => {
    if (services.length === 0) return;
    if (!service || !services.includes(service)) {
      setService(services[0]);
    }
  }, [services, service]);

  const overviewQuery = useTracingOverview(service, Number(lookback));
  const listQuery = useTraces({
    service: service || undefined,
    operation: appliedOperation,
    lookbackMinutes: Number(lookback),
    limit: Number(limit),
    onlyErrors,
  });

  const drillInto = (op: string) => {
    setAppliedOperation(op);
    setOperation(op);
    setTab("all");
  };

  const refreshAll = () => {
    void overviewQuery.refetch();
    void listQuery.refetch();
  };

  if (statusLoading) return <LoadingState />;
  if (statusError) return <ErrorState message={t("diagnostics.statusFailed")} />;

  if (!status?.available) {
    return (
      <div className="p-6 space-y-6">
        <PageHeader title={t("diagnostics.title")} subtitle={t("diagnostics.subtitle")} />
        {build && (
          <BuildStrip version={build.version} informationalVersion={build.informationalVersion} />
        )}
        <EmptyState
          icon={Activity}
          title={t("diagnostics.unavailableTitle")}
          description={
            status?.reason === "unreachable"
              ? t("diagnostics.unavailableUnreachable")
              : t("diagnostics.unavailableNoEndpoint")
          }
        />
      </div>
    );
  }

  const overview = overviewQuery.data;
  const rows: TraceSummary[] = listQuery.data ?? [];
  const slowestOp = slowestOperation(overview?.operations ?? []);
  const isFetching = overviewQuery.isFetching || listQuery.isFetching;

  return (
    <div className="p-6 space-y-6">
      <PageHeader
        title={t("diagnostics.title")}
        subtitle={t("diagnostics.subtitle")}
        action={
          <div className="flex items-center gap-2">
            <Select value={service} onValueChange={setService}>
              <SelectTrigger className="w-[11rem]">
                <SelectValue placeholder={t("diagnostics.service")} />
              </SelectTrigger>
              <SelectContent>
                {services.map((name) => (
                  <SelectItem key={name} value={name}>
                    {name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>

            <Select value={lookback} onValueChange={setLookback}>
              <SelectTrigger className="w-[10rem]">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {LOOKBACK_OPTIONS.map((value) => (
                  <SelectItem key={value} value={value}>
                    {t(`diagnostics.lookback${value}`)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>

            <Button variant="outline" size="icon" aria-label={t("common.refresh")} onClick={refreshAll} disabled={isFetching}>
              <RefreshCw className={`h-4 w-4 ${isFetching ? "animate-spin" : ""}`} />
            </Button>
          </div>
        }
      />

      {build && (
        <BuildStrip version={build.version} informationalVersion={build.informationalVersion} />
      )}

      {isUnreachableBaseUrl(orgSettings?.baseUrl) && (
        <div
          role="alert"
          className="flex items-start gap-2 rounded-lg border border-warning-500/30 bg-warning-500/10 p-3 text-sm"
        >
          <AlertTriangle className="mt-0.5 h-4 w-4 flex-shrink-0 text-warning-500" />
          <span>
            {t("diagnostics.baseUrlUnreachable")}{" "}
            <Link to="/settings" className="underline hover:text-brand-500">
              {t("nav.settings")}
            </Link>
          </span>
        </div>
      )}

      {overview && overview.sampleSize > 0 && (
        <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
          <SummaryCard
            label={t("diagnostics.tracesSampled")}
            value={String(overview.sampleSize)}
            hint={t("diagnostics.operationCount").replace("{n}", String(overview.operations.length))}
          />
          <SummaryCard
            label={t("diagnostics.errors")}
            value={String(overview.totalErrors)}
            tone={overview.totalErrors > 0 ? "error" : undefined}
          />
          <SummaryCard label={t("diagnostics.p95")} value={formatDuration(overview.p95DurationMs)} />
          <SummaryCard
            label={t("diagnostics.slowest")}
            value={formatDuration(overview.maxDurationMs)}
            hint={slowestOp?.operation}
          />
        </div>
      )}

      <Tabs value={tab} onValueChange={setTab}>
        <TabsList>
          <TabsTrigger value="grouped">{t("diagnostics.grouped")}</TabsTrigger>
          <TabsTrigger value="all">{t("diagnostics.allTraces")}</TabsTrigger>
        </TabsList>

        <TabsContent value="grouped" className="mt-4">
          {overviewQuery.isLoading && <LoadingState />}
          {overviewQuery.error && (
            <ErrorState message={t("diagnostics.searchFailed")} onRetry={() => void overviewQuery.refetch()} />
          )}

          {overview && overview.operations.length === 0 && !overviewQuery.isLoading && (
            <EmptyState
              icon={Activity}
              title={t("diagnostics.noTraces")}
              description={t("diagnostics.noTracesHint")}
            />
          )}

          {overview && overview.operations.length > 0 && (
            <Card className="divide-y">
              <div className="grid grid-cols-[minmax(0,1fr)_4rem_5rem_5rem_8rem_1rem] items-center gap-3 px-4 py-2 text-xs text-muted-foreground">
                <span>{t("diagnostics.operation")}</span>
                <span className="text-right">{t("diagnostics.count")}</span>
                <span className="text-right">{t("diagnostics.avg")}</span>
                <span className="text-right">{t("diagnostics.p95")}</span>
                <span />
                <span />
              </div>
              {overview.operations.map((op) => (
                <OperationRow
                  key={`${op.service}/${op.operation}`}
                  op={op}
                  slowestMs={slowestOp?.p95DurationMs ?? 0}
                  onDrill={drillInto}
                />
              ))}
            </Card>
          )}
        </TabsContent>

        <TabsContent value="all" className="mt-4 space-y-4">
          <Card className="p-3">
            <div className="flex flex-wrap items-center gap-2">
              <Input
                value={operation}
                onChange={(e) => setOperation(e.target.value)}
                onKeyDown={(e) => e.key === "Enter" && setAppliedOperation(operation.trim() || undefined)}
                placeholder={t("diagnostics.operationPlaceholder")}
                className="min-w-[14rem] flex-1"
              />

              <Select value={limit} onValueChange={setLimit}>
                <SelectTrigger className="w-[6rem]">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {LIMIT_OPTIONS.map((value) => (
                    <SelectItem key={value} value={value}>
                      {value}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>

              <Button
                variant={onlyErrors ? "default" : "outline"}
                onClick={() => setOnlyErrors((v) => !v)}
              >
                <AlertTriangle className="mr-2 h-4 w-4" />
                {t("diagnostics.onlyErrors")}
              </Button>

              <Button onClick={() => setAppliedOperation(operation.trim() || undefined)}>
                <Search className="mr-2 h-4 w-4" />
                {t("common.search")}
              </Button>

              {appliedOperation && (
                <Button
                  variant="ghost"
                  onClick={() => {
                    setAppliedOperation(undefined);
                    setOperation("");
                  }}
                >
                  {t("common.clear")}
                </Button>
              )}
            </div>
          </Card>

          {listQuery.isLoading && <LoadingState />}
          {listQuery.error && (
            <ErrorState message={t("diagnostics.searchFailed")} onRetry={() => void listQuery.refetch()} />
          )}

          {!listQuery.isLoading && !listQuery.error && rows.length === 0 && (
            <EmptyState
              icon={Activity}
              title={t("diagnostics.noTraces")}
              description={t("diagnostics.noTracesHint")}
            />
          )}

          {rows.length > 0 && (
            <Card className="divide-y">
              {rows.map((trace) => (
                <button
                  key={trace.traceId}
                  type="button"
                  onClick={() => setSelected(trace.traceId)}
                  className="flex w-full items-center gap-4 px-4 py-2 text-left hover:bg-muted/40"
                >
                  <div className="min-w-0 flex-1">
                    <div className="flex items-center gap-2">
                      {trace.errorCount > 0 && (
                        <AlertTriangle className="h-3.5 w-3.5 shrink-0 text-error-500" />
                      )}
                      <span className="truncate text-sm">{trace.rootOperation}</span>
                    </div>
                    <span className="text-xs text-muted-foreground">
                      {formatTimestamp(trace.startedAt)}
                    </span>
                  </div>

                  <span className="shrink-0 text-xs text-muted-foreground">
                    {trace.spanCount} {t("diagnostics.spans")}
                  </span>
                  <span className="w-20 shrink-0 text-right tabular-nums text-sm font-medium">
                    {formatDuration(trace.durationMs)}
                  </span>
                </button>
              ))}
            </Card>
          )}
        </TabsContent>
      </Tabs>

      <TraceDetailDialog traceId={selected} onClose={() => setSelected(null)} />
    </div>
  );
}
