import { useState, useMemo, useCallback } from "react";
import { t } from "@/shared/locales/i18n";
import { Link } from "react-router";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Badge } from "@/shared/components/ui/badge";
import { Card } from "@/shared/components/ui/card";
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
  DialogFooter,
} from "@/shared/components/ui/dialog";
import {
  Phone,
  PhoneCall,
  PhoneMissed,
  PhoneOff,
  Clock,
  AlertCircle,
  CheckCircle,
  XCircle,
  RotateCcw,
  Info,
  ChevronLeft,
  ChevronRight,
  Video,
} from "lucide-react";
import { LoadingState } from "@/shared/components/loading-state";
import { ErrorState } from "@/shared/components/error-state";
import { EmptyState } from "@/shared/components/empty-state";
import { StatCard } from "@/shared/components/stat-card";
import { PageHeader } from "@/shared/components/page-header";
import { useCallLogs } from "../hooks/use-call-logs";
import type { CallLogDto } from "../types/call-log.types";

function getStatusIcon(status: string) {
  const s = status.toLowerCase();
  switch (s) {
    case "acknowledged":
      return <CheckCircle className="w-4 h-4" />;
    case "escalated":
      return <PhoneCall className="w-4 h-4" />;
    case "connected":
      return <Phone className="w-4 h-4" />;
    case "conferencecreated":
      return <Video className="w-4 h-4" />;
    case "failed":
    case "timeout":
      return <XCircle className="w-4 h-4" />;
    case "noanswer":
    case "no_answer":
      return <PhoneMissed className="w-4 h-4" />;
    case "voicemail":
      return <PhoneOff className="w-4 h-4" />;
    case "initiated":
      return <Phone className="w-4 h-4 animate-pulse" />;
    default:
      return <AlertCircle className="w-4 h-4" />;
  }
}

function getStatusBadgeClass(status: string) {
  const s = status.toLowerCase();
  if (s === "acknowledged") return "bg-success-500/10 text-success-500 border-success-500/20";
  if (s === "escalated") return "bg-warning-500/10 text-warning-500 border-warning-500/20";
  if (s === "connected" || s === "conferencecreated") return "bg-brand-500/10 text-brand-500 border-brand-500/20";
  if (s === "failed" || s === "timeout") return "bg-error-500/10 text-error-500 border-error-500/20";
  if (s === "noanswer" || s === "no_answer" || s === "voicemail") return "bg-warning-500/10 text-warning-500 border-warning-500/20";
  if (s === "initiated") return "bg-brand-500/10 text-brand-500 border-brand-500/20";
  return "bg-muted/10 text-muted-foreground border-muted/20";
}

function getStatusLabel(status: string) {
  const s = status.toLowerCase();
  if (s === "acknowledged") return "Acknowledged";
  if (s === "escalated") return "Escalated";
  if (s === "connected") return "Connected";
  if (s === "failed") return "Failed";
  if (s === "noanswer" || s === "no_answer") return "No Answer";
  if (s === "voicemail") return "Voicemail";
  if (s === "timeout") return "Timeout";
  if (s === "initiated") return "Initiated";
  if (s === "conferencecreated") return "Conference";
  return status;
}

function formatDuration(seconds: number) {
  if (seconds === 0) return "-";
  const mins = Math.floor(seconds / 60);
  const secs = seconds % 60;
  return `${mins}:${secs.toString().padStart(2, "0")}`;
}

function formatTimestamp(dateString: string) {
  const date = new Date(dateString);
  const now = new Date();
  const diffMs = now.getTime() - date.getTime();
  const diffMins = Math.floor(diffMs / 1000 / 60);

  if (diffMins < 1) return t("callLogs.justNow");
  if (diffMins < 60) return `${diffMins}m ago`;
  if (diffMins < 1440) return `${Math.floor(diffMins / 60)}h ago`;
  return date.toLocaleDateString("en-US", {
    month: "short",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}

function getInitials(name?: string) {
  if (!name) return "?";
  return name
    .split(" ")
    .map((n) => n.charAt(0))
    .join("")
    .toUpperCase()
    .slice(0, 2);
}

export function CallLogsList() {
  const [page, setPage] = useState(1);
  const pageSize = 25;

  const { data, isLoading, error } = useCallLogs(page, pageSize);

  const total = data?.total ?? 0;
  const totalPages = Math.ceil(total / pageSize);

  const [searchQuery, setSearchQuery] = useState("");
  const [statusFilter, setStatusFilter] = useState<string>("all");

  const filteredLogs = useMemo(() => {
    const logs = data?.items ?? [];
    return logs.filter((log) => {
      const query = searchQuery.toLowerCase();
      const matchesSearch =
        !searchQuery ||
        (log.calledPersonName?.toLowerCase() ?? "").includes(query) ||
        log.phoneNumber.includes(searchQuery) ||
        log.incidentTitle.toLowerCase().includes(query);

      const matchesStatus =
        statusFilter === "all" || log.status.toLowerCase() === statusFilter.toLowerCase();

      return matchesSearch && matchesStatus;
    });
  }, [data?.items, searchQuery, statusFilter]);

  const stats = useMemo(() => {
    let completed = 0;
    let failed = 0;
    let durationSum = 0;
    let durationCount = 0;

    for (const l of filteredLogs) {
      const status = l.status.toLowerCase();
      if (status === "acknowledged") completed++;
      if (["failed", "noanswer", "no_answer", "voicemail", "timeout"].includes(status)) failed++;
      if (l.durationSeconds > 0) {
        durationSum += l.durationSeconds;
        durationCount++;
      }
    }

    return {
      totalBackend: total,
      pageCount: filteredLogs.length,
      completed,
      failed,
      avgDuration: durationCount > 0 ? Math.round(durationSum / durationCount) : 0,
    };
  }, [filteredLogs, total]);

  const [selectedLog, setSelectedLog] = useState<CallLogDto | null>(null);
  const [isDetailModalOpen, setIsDetailModalOpen] = useState(false);

  const handleViewDetails = useCallback((log: CallLogDto) => {
    setSelectedLog(log);
    setIsDetailModalOpen(true);
  }, []);

  if (isLoading) {
    return <LoadingState message={t("callLogs.loading")} />;
  }

  if (error) {
    return (
      <ErrorState
        title={t("callLogs.loadFailed")}
        message={error instanceof Error ? error.message : t("common.errorOccurred")}
      />
    );
  }

  return (
    <>
      <div className="p-6 space-y-6">
        <PageHeader
          title={t("callLogs.title")}
          subtitle={t("callLogs.subtitle")}
        />

        <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
          <StatCard label={t("callLogs.totalCalls")} value={stats.totalBackend} />
          <StatCard
            label={`${t("callLogs.successful")} (${t("callLogs.thisPage")})`}
            value={stats.completed}
            color="#22C55E"
            borderColor="border-success-500/20"
          />
          <StatCard
            label={`${t("callLogs.failed")} (${t("callLogs.thisPage")})`}
            value={stats.failed}
            color="#FF4D4D"
            borderColor="border-error-500/20"
          />
          <StatCard
            label={`${t("callLogs.avgDuration")} (${t("callLogs.thisPage")})`}
            value={formatDuration(stats.avgDuration)}
            color="#3E7BFA"
            borderColor="border-brand-500/20"
          />
        </div>

        <Card className="p-4 bg-card/80 backdrop-blur-sm border-border">
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
            <div>
              <label
                style={{
                  fontSize: "0.75rem",
                  fontWeight: 600,
                  marginBottom: "0.5rem",
                  display: "block",
                  color: "#94A3B8",
                }}
              >
                {t("common.status").toUpperCase()}
              </label>
              <Select value={statusFilter} onValueChange={setStatusFilter}>
                <SelectTrigger className="bg-input-background">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">{t("callLogs.allStatuses")}</SelectItem>
                  <SelectItem value="acknowledged">Acknowledged</SelectItem>
                  <SelectItem value="escalated">Escalated</SelectItem>
                  <SelectItem value="connected">Connected</SelectItem>
                  <SelectItem value="failed">Failed</SelectItem>
                  <SelectItem value="noanswer">No Answer</SelectItem>
                  <SelectItem value="voicemail">Voicemail</SelectItem>
                  <SelectItem value="timeout">Timeout</SelectItem>
                </SelectContent>
              </Select>
            </div>

            <div className="lg:col-span-2">
              <label
                style={{
                  fontSize: "0.75rem",
                  fontWeight: 600,
                  marginBottom: "0.5rem",
                  display: "block",
                  color: "#94A3B8",
                }}
              >
                {t("common.search").toUpperCase()}
              </label>
              <Input
                placeholder={t("callLogs.searchPlaceholder")}
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                className="bg-input-background"
              />
            </div>
          </div>
        </Card>

        <Card className="overflow-hidden bg-card/80 backdrop-blur-sm border-border">
          <div className="overflow-x-auto">
            <table className="w-full">
              <thead>
                <tr className="border-b border-border bg-surface-light/20">
                  <th
                    className="text-left p-4"
                    style={{
                      fontSize: "0.75rem",
                      fontWeight: 600,
                      color: "#94A3B8",
                      letterSpacing: "0.05em",
                    }}
                  >
                    {t("callLogs.colRecipient")}
                  </th>
                  <th
                    className="text-left p-4"
                    style={{
                      fontSize: "0.75rem",
                      fontWeight: 600,
                      color: "#94A3B8",
                      letterSpacing: "0.05em",
                    }}
                  >
                    {t("callLogs.colIncident")}
                  </th>
                  <th
                    className="text-left p-4"
                    style={{
                      fontSize: "0.75rem",
                      fontWeight: 600,
                      color: "#94A3B8",
                      letterSpacing: "0.05em",
                    }}
                  >
                    {t("common.status").toUpperCase()}
                  </th>
                  <th
                    className="text-left p-4"
                    style={{
                      fontSize: "0.75rem",
                      fontWeight: 600,
                      color: "#94A3B8",
                      letterSpacing: "0.05em",
                    }}
                  >
                    {t("callLogs.colDuration")}
                  </th>
                  <th
                    className="text-left p-4"
                    style={{
                      fontSize: "0.75rem",
                      fontWeight: 600,
                      color: "#94A3B8",
                      letterSpacing: "0.05em",
                    }}
                  >
                    {t("callLogs.colAttempt")}
                  </th>
                  <th
                    className="text-left p-4"
                    style={{
                      fontSize: "0.75rem",
                      fontWeight: 600,
                      color: "#94A3B8",
                      letterSpacing: "0.05em",
                    }}
                  >
                    {t("callLogs.colTimestamp")}
                  </th>
                  <th className="p-4"></th>
                </tr>
              </thead>
              <tbody>
                {filteredLogs.map((log) => (
                  <tr
                    key={log.id}
                    className="border-b border-border hover:bg-surface-light/20 transition-colors cursor-pointer"
                    tabIndex={0}
                    role="button"
                    onClick={() => handleViewDetails(log)}
                    onKeyDown={(e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); handleViewDetails(log); } }}
                  >
                    <td className="p-4">
                      <div className="flex items-center gap-3">
                        <div className="w-9 h-9 rounded-full flex items-center justify-center text-white text-xs font-bold flex-shrink-0 bg-brand-500">
                          {getInitials(log.calledPersonName)}
                        </div>
                        <div className="min-w-0">
                          <p style={{ fontSize: "0.875rem", fontWeight: 600 }}>
                            {log.calledPersonName || t("common.unknown")}
                          </p>
                          <p
                            style={{ fontSize: "0.75rem", color: "#94A3B8" }}
                            className="font-mono"
                          >
                            {log.phoneNumber}
                          </p>
                        </div>
                      </div>
                    </td>

                    <td className="p-4">
                      <Link
                        to={`/incidents/${log.incidentId}`}
                        onClick={(e) => e.stopPropagation()}
                        className="text-brand-500 hover:underline"
                        style={{ fontSize: "0.875rem" }}
                      >
                        {log.incidentTitle}
                      </Link>
                    </td>

                    <td className="p-4">
                      <Badge
                        className={`${getStatusBadgeClass(log.status)} border text-xs`}
                      >
                        <span className="flex items-center gap-1.5">
                          {getStatusIcon(log.status)}
                          {getStatusLabel(log.status)}
                        </span>
                      </Badge>
                    </td>

                    <td className="p-4">
                      <div className="flex items-center gap-2">
                        <span
                          style={{ fontSize: "0.875rem" }}
                          className={`font-mono ${log.durationSeconds > 300 ? "text-warning-500 font-semibold" : ""
                            }`}
                        >
                          {log.formattedDuration || formatDuration(log.durationSeconds)}
                        </span>
                        {log.durationSeconds > 300 && (
                          <Badge className="bg-warning-500/10 text-warning-500 border-warning-500/20 border text-xs">
                            {t("callLogs.long")}
                          </Badge>
                        )}
                      </div>
                    </td>

                    <td className="p-4">
                      {log.attemptNumber > 1 ? (
                        <Badge className="bg-warning-500/10 text-warning-500 border-warning-500/20 border text-xs">
                          <RotateCcw className="w-3 h-3 mr-1" />
                          {t("callLogs.attemptN").replace("{n}", String(log.attemptNumber))}
                        </Badge>
                      ) : (
                        <span style={{ fontSize: "0.875rem", color: "#94A3B8" }}>
                          1st
                        </span>
                      )}
                    </td>

                    <td className="p-4">
                      <div className="flex items-center gap-1.5">
                        <Clock className="w-3.5 h-3.5 text-muted-foreground" />
                        <span style={{ fontSize: "0.875rem", color: "#94A3B8" }}>
                          {formatTimestamp(log.initiatedAt)}
                        </span>
                      </div>
                    </td>

                    <td className="p-4">
                      <Button
                        size="sm"
                        variant="ghost"
                        onClick={(e) => {
                          e.stopPropagation();
                          handleViewDetails(log);
                        }}
                      >
                        <Info className="w-4 h-4" />
                      </Button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {filteredLogs.length === 0 && (
            <EmptyState
              icon={Phone}
              title={t("callLogs.noCallLogs")}
              description={t("callLogs.adjustFilters")}
            />
          )}

          {totalPages > 1 && (
            <div className="flex items-center justify-between p-4 border-t border-border">
              <p style={{ fontSize: "0.875rem", color: "#94A3B8" }}>
                Page {page} of {totalPages} ({total} total)
              </p>
              <div className="flex items-center gap-2">
                <Button
                  variant="outline"
                  size="sm"
                  disabled={page <= 1}
                  onClick={() => setPage((p) => p - 1)}
                  className="bg-input-background"
                >
                  <ChevronLeft className="w-4 h-4" />
                </Button>
                <Button
                  variant="outline"
                  size="sm"
                  disabled={page >= totalPages}
                  onClick={() => setPage((p) => p + 1)}
                  className="bg-input-background"
                >
                  <ChevronRight className="w-4 h-4" />
                </Button>
              </div>
            </div>
          )}
        </Card>
      </div>

      <Dialog open={isDetailModalOpen} onOpenChange={setIsDetailModalOpen}>
        <DialogContent className="bg-card border-border sm:max-w-[700px]">
          <DialogHeader>
            <DialogTitle style={{ fontSize: "1.5rem", fontWeight: 600 }}>
              {t("callLogs.callDetails")}
            </DialogTitle>
          </DialogHeader>

          {selectedLog && (
            <div className="space-y-5 py-4">
              <div className="flex items-center gap-4 p-4 rounded-lg bg-surface-light/20 border border-border">
                <div className="w-14 h-14 rounded-full flex items-center justify-center text-white font-bold text-lg bg-brand-500">
                  {getInitials(selectedLog.calledPersonName)}
                </div>
                <div>
                  <p style={{ fontSize: "1.0625rem", fontWeight: 600 }}>
                    {selectedLog.calledPersonName || t("common.unknown")}
                  </p>
                  <p
                    style={{ fontSize: "0.875rem", color: "#94A3B8" }}
                    className="font-mono"
                  >
                    {selectedLog.phoneNumber}
                  </p>
                </div>
                <Badge
                  className={`${getStatusBadgeClass(selectedLog.status)} border ml-auto`}
                >
                  <span className="flex items-center gap-1.5">
                    {getStatusIcon(selectedLog.status)}
                    {getStatusLabel(selectedLog.status)}
                  </span>
                </Badge>
              </div>

              <div className="grid grid-cols-2 gap-4">
                <div className="p-4 rounded-lg bg-surface-light/20">
                  <p style={{ fontSize: "0.75rem", color: "#94A3B8", marginBottom: "0.5rem" }}>{t("callLogs.colDuration")}</p>
                  <p style={{ fontSize: "0.9375rem", fontWeight: 600 }} className="font-mono">
                    {selectedLog.formattedDuration || formatDuration(selectedLog.durationSeconds)}
                  </p>
                </div>

                <div className="p-4 rounded-lg bg-surface-light/20">
                  <p style={{ fontSize: "0.75rem", color: "#94A3B8", marginBottom: "0.5rem" }}>{t("callLogs.colAttempt")}</p>
                  <p style={{ fontSize: "0.9375rem", fontWeight: 600 }}>
                    #{selectedLog.attemptNumber}
                  </p>
                </div>

                <div className="p-4 rounded-lg bg-surface-light/20">
                  <p style={{ fontSize: "0.75rem", color: "#94A3B8", marginBottom: "0.5rem" }}>{t("callLogs.initiatedAt")}</p>
                  <p style={{ fontSize: "0.9375rem", fontWeight: 600 }}>
                    {new Date(selectedLog.initiatedAt).toLocaleString("en-US", {
                      month: "short",
                      day: "numeric",
                      year: "numeric",
                      hour: "2-digit",
                      minute: "2-digit",
                    })}
                  </p>
                </div>

                {selectedLog.completedAt && (
                  <div className="p-4 rounded-lg bg-surface-light/20">
                    <p style={{ fontSize: "0.75rem", color: "#94A3B8", marginBottom: "0.5rem" }}>{t("callLogs.completedAt")}</p>
                    <p style={{ fontSize: "0.9375rem", fontWeight: 600 }}>
                      {new Date(selectedLog.completedAt).toLocaleString("en-US", {
                        month: "short",
                        day: "numeric",
                        year: "numeric",
                        hour: "2-digit",
                        minute: "2-digit",
                      })}
                    </p>
                  </div>
                )}
              </div>

              <div className="p-4 rounded-lg bg-brand-500/5 border border-brand-500/20">
                <p style={{ fontSize: "0.75rem", color: "#94A3B8", marginBottom: "0.5rem" }}>
                  {t("callLogs.relatedIncident")}
                </p>
                <Link
                  to={`/incidents/${selectedLog.incidentId}`}
                  className="text-brand-500 hover:underline font-semibold"
                  style={{ fontSize: "0.9375rem" }}
                >
                  {selectedLog.incidentTitle}
                </Link>
              </div>

              {selectedLog.failureReason && (
                <div className="p-4 rounded-lg bg-error-500/10 border border-error-500/20">
                  <div className="flex items-start gap-2">
                    <AlertCircle className="w-4 h-4 text-error-500 flex-shrink-0 mt-0.5" />
                    <div>
                      <p style={{ fontSize: "0.75rem", color: "#94A3B8", marginBottom: "0.25rem" }}>
                        {t("callLogs.failureReason")}
                      </p>
                      <p style={{ fontSize: "0.875rem", color: "#FF4D4D" }}>
                        {selectedLog.failureReason}
                      </p>
                    </div>
                  </div>
                </div>
              )}
            </div>
          )}

          <DialogFooter>
            <Button
              onClick={() => setIsDetailModalOpen(false)}
              className="bg-brand-500 hover:bg-brand-600 text-white"
            >
              {t("common.close")}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}