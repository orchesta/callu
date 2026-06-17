import { useState, useMemo, memo, useCallback, useEffect } from "react";
import { Link } from "react-router";
import { Button } from "@/shared/components/ui/button";
import { Badge } from "@/shared/components/ui/badge";
import { Avatar, AvatarFallback } from "@/shared/components/ui/avatar";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";
import { Checkbox } from "@/shared/components/ui/checkbox";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/shared/components/ui/table";
import { Server, Clock, ChevronRight, Filter, CheckCircle, XCircle, LayoutGrid, Table2 } from "lucide-react";
import { LoadingState } from "@/shared/components/loading-state";
import { ErrorState } from "@/shared/components/error-state";
import { EmptyState } from "@/shared/components/empty-state";
import { StatCard } from "@/shared/components/stat-card";
import { PageHeader } from "@/shared/components/page-header";
import { SearchInput } from "@/shared/components/search-input";
import type { IncidentListItem, IncidentFilter } from "../types/incident.types";
import { useIncidents, useBulkAcknowledge, useBulkResolve } from "../hooks/use-incidents";
import { useIncidentCounts } from "@/features/dashboard/hooks/use-dashboard";
import { getSeverityConfig, getStatusBadge } from "@/shared/utils/incident-styles";
import { getTimeAgo } from "@/shared/utils/time";
import { t } from "@/shared/locales/i18n";

const PAGE_SIZE_OPTIONS = [10, 25, 50, 100] as const;

const IncidentCard = memo(
  ({
    incident,
    isSelected,
    onToggleSelect,
  }: {
    incident: IncidentListItem;
    isSelected?: boolean;
    onToggleSelect?: (id: string) => void;
  }) => {
    const severityConfig = useMemo(() => getSeverityConfig(incident.severity), [incident.severity]);
    const statusBadge = useMemo(() => getStatusBadge(incident.status), [incident.status]);

    return (
      <div
        className={`flex items-center gap-3 p-4 ${severityConfig.border} hover:bg-surface-light/30 transition-all group ${severityConfig.glow} hover:shadow-lg ${isSelected ? "bg-brand-500/5 border-l-2 border-l-brand-500" : ""}`}
      >
        {onToggleSelect && (
          <Checkbox
            checked={isSelected}
            onCheckedChange={() => onToggleSelect(incident.id)}
            className="flex-shrink-0"
          />
        )}
        <Link
          to={`/incidents/${incident.id}`}
          className="flex-1 min-w-0 flex items-start gap-4"
        >
          <div className="flex-1 min-w-0 space-y-3">
            <div className="flex items-start justify-between gap-3">
              <h3
                style={{ fontSize: "0.9375rem", fontWeight: 600 }}
                className="group-hover:text-brand-500 transition-colors"
              >
                {incident.title}
              </h3>
              <div className="flex items-center gap-2 flex-shrink-0">
                <Badge className={`${severityConfig.badge} border text-xs`}>{incident.severity}</Badge>
                <Badge className={`${statusBadge} text-xs`}>{incident.status}</Badge>
              </div>
            </div>

            <div className="flex flex-wrap items-center gap-x-4 gap-y-2 text-sm text-muted-foreground">
              {incident.serviceName && (
                <span className="flex items-center gap-1.5">
                  <Server className="w-3.5 h-3.5" />
                  {incident.serviceName}
                </span>
              )}
              <span className="flex items-center gap-1.5">
                <Clock className="w-3.5 h-3.5" />
                {getTimeAgo(incident.startedAt)}
              </span>
              {incident.acknowledgedBy && (
                <span className="flex items-center gap-1.5">
                  <Avatar className="w-5 h-5">
                    <AvatarFallback className="text-[10px] bg-brand-500/10 text-brand-500">
                      {incident.acknowledgedBy
                        .split(" ")
                        .map((n) => n[0])
                        .join("")}
                    </AvatarFallback>
                  </Avatar>
                  {incident.acknowledgedBy}
                </span>
              )}
            </div>
          </div>

          <ChevronRight className="w-5 h-5 text-muted-foreground group-hover:text-brand-500 group-hover:translate-x-1 transition-all flex-shrink-0" />
        </Link>
      </div>
    );
  },
);

IncidentCard.displayName = "IncidentCard";

const IncidentTableRow = memo(
  ({
    incident,
    isSelected,
    onToggleSelect,
  }: {
    incident: IncidentListItem;
    isSelected?: boolean;
    onToggleSelect?: (id: string) => void;
  }) => {
    const severityConfig = useMemo(() => getSeverityConfig(incident.severity), [incident.severity]);
    const statusBadge = useMemo(() => getStatusBadge(incident.status), [incident.status]);

    return (
      <TableRow data-state={isSelected ? "selected" : undefined}>
        <TableCell className="w-10">
          {onToggleSelect && (
            <Checkbox
              checked={isSelected}
              onCheckedChange={() => onToggleSelect(incident.id)}
              onClick={(e) => e.stopPropagation()}
              aria-label={t("incidents.selectRow", { id: incident.id.slice(0, 8) })}
            />
          )}
        </TableCell>
        <TableCell className="max-w-[min(280px,40vw)] whitespace-normal">
          <Link
            to={`/incidents/${incident.id}`}
            className="font-medium text-sm hover:text-brand-500 transition-colors"
          >
            {incident.title}
          </Link>
        </TableCell>
        <TableCell>
          <Badge className={`${severityConfig.badge} border text-xs`}>{incident.severity}</Badge>
        </TableCell>
        <TableCell>
          <Badge className={`${statusBadge} text-xs`}>{incident.status}</Badge>
        </TableCell>
        <TableCell className="text-muted-foreground text-sm hidden md:table-cell">
          {incident.serviceName ? (
            <span className="flex items-center gap-1">
              <Server className="w-3.5 h-3.5 flex-shrink-0" />
              <span className="truncate max-w-[140px]">{incident.serviceName}</span>
            </span>
          ) : (
            "—"
          )}
        </TableCell>
        <TableCell className="text-muted-foreground text-sm whitespace-nowrap">
          {getTimeAgo(incident.startedAt)}
        </TableCell>
        <TableCell className="w-10 p-2">
          <Link
            to={`/incidents/${incident.id}`}
            className="inline-flex text-muted-foreground hover:text-brand-500"
            aria-label={t("common.details")}
          >
            <ChevronRight className="w-4 h-4" />
          </Link>
        </TableCell>
      </TableRow>
    );
  },
);

IncidentTableRow.displayName = "IncidentTableRow";

const EMPTY_INCIDENTS: IncidentListItem[] = [];

function isTypingTarget(el: EventTarget | null): boolean {
  if (!el || !(el instanceof HTMLElement)) return false;
  return Boolean(el.closest("input, textarea, select, [contenteditable=true]"));
}

export function IncidentsList() {
  const [searchQuery, setSearchQuery] = useState("");
  const [statusFilter, setStatusFilter] = useState("all");
  const [severityFilter, setSeverityFilter] = useState("all");
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [viewMode, setViewMode] = useState<"cards" | "table">("cards");
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());

  const bulkAck = useBulkAcknowledge();
  const bulkResolve = useBulkResolve();

  const filter: IncidentFilter = useMemo(
    () => ({
      status: statusFilter !== "all" ? statusFilter : undefined,
      severity: severityFilter !== "all" ? severityFilter : undefined,
      searchQuery: searchQuery || undefined,
      page,
      pageSize,
    }),
    [statusFilter, severityFilter, searchQuery, page, pageSize],
  );

  const { data: pagedResult, isLoading, isError } = useIncidents(filter);
  const { data: incidentCounts } = useIncidentCounts();

  const incidents = pagedResult?.items ?? EMPTY_INCIDENTS;
  const totalCount = pagedResult?.totalCount ?? 0;
  const totalPages = pagedResult?.totalPages ?? 0;

  const stats = useMemo(() => {
    if (!incidentCounts) {
      return { total: totalCount, open: 0, acknowledged: 0, resolved: 0 };
    }
    const counts = incidentCounts as Record<string, number>;
    return {
      total: Object.values(counts).reduce((sum, c) => sum + c, 0),
      open: counts["Open"] ?? 0,
      acknowledged: counts["Acknowledged"] ?? 0,
      resolved: counts["Resolved"] ?? 0,
    };
  }, [incidentCounts, totalCount]);

  const handleFilterChange = useCallback((setter: (v: string) => void, value: string) => {
    setter(value);
    setPage(1);
  }, []);

  const runBulkAck = useCallback(() => {
    if (selectedIds.size === 0) return;
    bulkAck.mutate([...selectedIds], { onSuccess: () => setSelectedIds(new Set()) });
  }, [bulkAck, selectedIds]);

  const runBulkResolve = useCallback(() => {
    if (selectedIds.size === 0) return;
    bulkResolve.mutate([...selectedIds], { onSuccess: () => setSelectedIds(new Set()) });
  }, [bulkResolve, selectedIds]);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.defaultPrevented || isTypingTarget(e.target)) return;
      if (e.metaKey || e.ctrlKey || e.altKey) return;
      if (selectedIds.size === 0) return;
      if (bulkAck.isPending || bulkResolve.isPending) return;

      const k = e.key.toLowerCase();
      if (k === "a") {
        e.preventDefault();
        runBulkAck();
      } else if (k === "r") {
        e.preventDefault();
        runBulkResolve();
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [selectedIds, bulkAck.isPending, bulkResolve.isPending, runBulkAck, runBulkResolve]);

  return (
    <div className="p-6 space-y-6">
      <PageHeader title={t("incidents.title")} subtitle={t("incidents.subtitle")} />

      <div className="flex flex-col sm:flex-row gap-4">
        <SearchInput
          placeholder={t("incidents.searchByTitle")}
          value={searchQuery}
          onChange={(v) => handleFilterChange(setSearchQuery, v)}
          className="flex-1"
        />

        <Select value={statusFilter} onValueChange={(v) => handleFilterChange(setStatusFilter, v)}>
          <SelectTrigger className="w-full sm:w-[180px] bg-input-background backdrop-blur-sm">
            <SelectValue placeholder={t("common.status")} />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">{t("incidents.allStatus")}</SelectItem>
            <SelectItem value="Open">{t("incidents.open")}</SelectItem>
            <SelectItem value="Acknowledged">{t("incidents.acknowledged")}</SelectItem>
            <SelectItem value="Investigating">{t("incidents.statusInvestigating")}</SelectItem>
            <SelectItem value="Mitigated">{t("incidents.statusMitigated")}</SelectItem>
            <SelectItem value="Resolved">{t("incidents.resolved")}</SelectItem>
            <SelectItem value="Closed">{t("incidents.statusClosed")}</SelectItem>
          </SelectContent>
        </Select>

        <Select
          value={severityFilter}
          onValueChange={(v) => handleFilterChange(setSeverityFilter, v)}
        >
          <SelectTrigger className="w-full sm:w-[180px] bg-input-background backdrop-blur-sm">
            <SelectValue placeholder={t("incidents.severity")} />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">{t("incidents.allSeverity")}</SelectItem>
            <SelectItem value="Critical">{t("incidents.critical")}</SelectItem>
            <SelectItem value="High">{t("incidents.high")}</SelectItem>
            <SelectItem value="Medium">{t("incidents.medium")}</SelectItem>
            <SelectItem value="Low">{t("incidents.low")}</SelectItem>
          </SelectContent>
        </Select>
      </div>

      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3">
        <p className="text-xs text-muted-foreground order-2 sm:order-1">{t("incidents.keyboardHint")}</p>
        <div className="flex flex-wrap items-center gap-2 order-1 sm:order-2">
          <div className="flex rounded-lg border border-border p-0.5 bg-input-background/50">
            <Button
              type="button"
              variant={viewMode === "cards" ? "secondary" : "ghost"}
              size="sm"
              className="h-8 px-2"
              onClick={() => setViewMode("cards")}
              aria-pressed={viewMode === "cards"}
              aria-label={t("incidents.viewCards")}
            >
              <LayoutGrid className="w-4 h-4 sm:mr-1" />
              <span className="hidden sm:inline">{t("incidents.viewCards")}</span>
            </Button>
            <Button
              type="button"
              variant={viewMode === "table" ? "secondary" : "ghost"}
              size="sm"
              className="h-8 px-2"
              onClick={() => setViewMode("table")}
              aria-pressed={viewMode === "table"}
              aria-label={t("incidents.viewTable")}
            >
              <Table2 className="w-4 h-4 sm:mr-1" />
              <span className="hidden sm:inline">{t("incidents.viewTable")}</span>
            </Button>
          </div>

          <Select
            value={String(pageSize)}
            onValueChange={(v) => {
              setPageSize(Number(v));
              setPage(1);
            }}
          >
            <SelectTrigger className="w-full sm:w-[130px] h-9 bg-input-background backdrop-blur-sm">
              <SelectValue placeholder={t("incidents.perPage")} />
            </SelectTrigger>
            <SelectContent>
              {PAGE_SIZE_OPTIONS.map((n) => (
                <SelectItem key={n} value={String(n)}>
                  {t("incidents.pageSizeOption", { count: String(n) })}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      </div>

      <div className="flex flex-wrap gap-2">
        <Button
          size="sm"
          variant={statusFilter === "Open" ? "default" : "outline"}
          onClick={() =>
            handleFilterChange(setStatusFilter, statusFilter === "Open" ? "all" : "Open")
          }
          className={
            statusFilter === "Open"
              ? "bg-error-500 hover:bg-error-600"
              : "bg-input-background"
          }
        >
          {t("incidents.open")}
        </Button>
        <Button
          size="sm"
          variant={severityFilter === "Critical" ? "default" : "outline"}
          onClick={() =>
            handleFilterChange(
              setSeverityFilter,
              severityFilter === "Critical" ? "all" : "Critical",
            )
          }
          className={
            severityFilter === "Critical"
              ? "bg-error-500 hover:bg-error-600"
              : "bg-input-background"
          }
        >
          {t("incidents.critical")}
        </Button>
        <Button
          size="sm"
          variant={severityFilter === "High" ? "default" : "outline"}
          onClick={() =>
            handleFilterChange(setSeverityFilter, severityFilter === "High" ? "all" : "High")
          }
          className={
            severityFilter === "High"
              ? "bg-warning-500 hover:bg-warning-600"
              : "bg-input-background"
          }
        >
          {t("incidents.highPriority")}
        </Button>
        <Button
          size="sm"
          variant="outline"
          onClick={() => {
            setStatusFilter("all");
            setSeverityFilter("all");
            setSearchQuery("");
            setPage(1);
          }}
          className="bg-input-background"
        >
          {t("incidents.clearFilters")}
        </Button>
      </div>

      <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
        <StatCard label={t("incidents.total")} value={stats.total} />
        <StatCard
          label={t("incidents.open").toUpperCase()}
          value={stats.open}
          color="#FF4D4D"
          borderColor="border-error-500/20"
        />
        <StatCard
          label={t("incidents.acknowledged").toUpperCase()}
          value={stats.acknowledged}
          color="#FB923C"
          borderColor="border-warning-500/20"
        />
        <StatCard
          label={t("incidents.resolved").toUpperCase()}
          value={stats.resolved}
          color="#22C55E"
          borderColor="border-success-500/20"
        />
      </div>

      <div className="bg-card/80 backdrop-blur-sm border border-border rounded-lg overflow-hidden">
        {incidents.length > 0 && viewMode === "cards" && (
          <div className="flex items-center gap-3 px-4 py-2 border-b border-border bg-muted/30">
            <Checkbox
              checked={selectedIds.size === incidents.length && incidents.length > 0}
              onCheckedChange={(checked) => {
                if (checked) {
                  setSelectedIds(new Set(incidents.map((i) => i.id)));
                } else {
                  setSelectedIds(new Set());
                }
              }}
            />
            <span className="text-xs text-muted-foreground font-medium">
              {selectedIds.size > 0
                ? t("incidents.selectedCount", { count: String(selectedIds.size) })
                : t("incidents.selectAll")}
            </span>
          </div>
        )}

        {incidents.length > 0 && viewMode === "table" && (
          <div className="border-b border-border bg-muted/30 px-2 py-2 flex items-center gap-2">
            <Checkbox
              checked={selectedIds.size === incidents.length && incidents.length > 0}
              onCheckedChange={(checked) => {
                if (checked) {
                  setSelectedIds(new Set(incidents.map((i) => i.id)));
                } else {
                  setSelectedIds(new Set());
                }
              }}
            />
            <span className="text-xs text-muted-foreground font-medium">
              {selectedIds.size > 0
                ? t("incidents.selectedCount", { count: String(selectedIds.size) })
                : t("incidents.selectAll")}
            </span>
          </div>
        )}

        {isLoading ? (
          <LoadingState message={t("incidents.loadingIncidents")} />
        ) : isError ? (
          <ErrorState title={t("incidents.loadFailed")} />
        ) : incidents.length === 0 ? (
          <EmptyState
            icon={Filter}
            title={t("incidents.noIncidentsTitle")}
            description={t("incidents.adjustFilters")}
          />
        ) : viewMode === "cards" ? (
          <div className="divide-y divide-border">
            {incidents.map((incident) => (
              <IncidentCard
                key={incident.id}
                incident={incident}
                isSelected={selectedIds.has(incident.id)}
                onToggleSelect={(id) => {
                  setSelectedIds((prev) => {
                    const next = new Set(prev);
                    if (next.has(id)) next.delete(id);
                    else next.add(id);
                    return next;
                  });
                }}
              />
            ))}
          </div>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead className="w-10" />
                <TableHead>{t("incidents.columnTitle")}</TableHead>
                <TableHead>{t("incidents.severity")}</TableHead>
                <TableHead>{t("common.status")}</TableHead>
                <TableHead className="hidden md:table-cell">{t("incidents.service")}</TableHead>
                <TableHead>{t("incidents.columnWhen")}</TableHead>
                <TableHead className="w-10" />
              </TableRow>
            </TableHeader>
            <TableBody>
              {incidents.map((incident) => (
                <IncidentTableRow
                  key={incident.id}
                  incident={incident}
                  isSelected={selectedIds.has(incident.id)}
                  onToggleSelect={(id) => {
                    setSelectedIds((prev) => {
                      const next = new Set(prev);
                      if (next.has(id)) next.delete(id);
                      else next.add(id);
                      return next;
                    });
                  }}
                />
              ))}
            </TableBody>
          </Table>
        )}
      </div>

      {selectedIds.size > 0 && (
        <div className="fixed bottom-6 left-1/2 -translate-x-1/2 z-50 bg-card/95 backdrop-blur-xl border border-border rounded-xl shadow-2xl px-6 py-3 flex items-center gap-4 animate-in slide-in-from-bottom-4 max-w-[calc(100vw-2rem)] flex-wrap justify-center">
          <span className="text-sm font-medium text-muted-foreground">
            {t("incidents.selectedCount", { count: String(selectedIds.size) })}
          </span>
          <div className="w-px h-6 bg-border hidden sm:block" />
          <Button
            size="sm"
            variant="outline"
            className="bg-warning-500/10 border-warning-500/30 text-warning-500 hover:bg-warning-500/20"
            onClick={() => runBulkAck()}
            disabled={bulkAck.isPending}
          >
            <CheckCircle className="w-4 h-4 mr-1.5" />
            {t("incidents.bulkAcknowledge")}
          </Button>
          <Button
            size="sm"
            variant="outline"
            className="bg-success-500/10 border-success-500/30 text-success-500 hover:bg-success-500/20"
            onClick={() => runBulkResolve()}
            disabled={bulkResolve.isPending}
          >
            <CheckCircle className="w-4 h-4 mr-1.5" />
            {t("incidents.bulkResolve")}
          </Button>
          <Button
            size="sm"
            variant="ghost"
            className="text-muted-foreground"
            onClick={() => setSelectedIds(new Set())}
          >
            <XCircle className="w-4 h-4 mr-1.5" />
            {t("incidents.clearSelection")}
          </Button>
        </div>
      )}

      <div className="flex items-center justify-between flex-wrap gap-3">
        <p className="text-sm text-muted-foreground">
          {t("incidents.pageOf", { page: String(page), total: String(totalPages || 1), count: String(totalCount) })}
        </p>
        <div className="flex gap-2">
          <Button
            variant="outline"
            size="sm"
            onClick={() => setPage((p) => Math.max(1, p - 1))}
            disabled={!pagedResult?.hasPreviousPage}
          >
            {t("common.previous")}
          </Button>
          <Button
            variant="outline"
            size="sm"
            onClick={() => setPage((p) => p + 1)}
            disabled={!pagedResult?.hasNextPage}
          >
            {t("common.next")}
          </Button>
        </div>
      </div>
    </div>
  );
}
