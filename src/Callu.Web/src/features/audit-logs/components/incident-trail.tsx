import { useState } from "react";
import { Link, useParams } from "react-router";
import { t } from "@/shared/locales/i18n";
import { Button } from "@/shared/components/ui/button";
import { Card } from "@/shared/components/ui/card";
import { ScrollText, ExternalLink } from "lucide-react";
import { LoadingState } from "@/shared/components/loading-state";
import { ErrorState } from "@/shared/components/error-state";
import { EmptyState } from "@/shared/components/empty-state";
import { PageHeader } from "@/shared/components/page-header";
import { useAuditLogSearch } from "../hooks/use-audit-logs";
import { useAuditFilterDraft, usePageWithinBounds } from "../hooks/use-filter-draft";
import { chosenAction, dayEnd, dayStart, EMPTY_AUDIT_FILTER } from "../utils/filter-draft";
import type { AuditLogEntry } from "../types/audit-log.types";
import { AuditEntryRow } from "./entry-row";
import { AuditEntryDialog } from "./entry-dialog";
import { AuditFilterCard } from "./filter-card";

const PAGE_SIZE = 100;

/** Every entity type a row carrying this incident's id can be filed under. */
const TRAIL_ENTITY_TYPES = ["Incident", "Escalation", "NotificationChannel", "AuditLog"];

export function IncidentAuditTrail() {
  const { id = "" } = useParams();
  const { draft, applied, page, setPage, set, apply, clear } = useAuditFilterDraft(EMPTY_AUDIT_FILTER);
  const [selected, setSelected] = useState<AuditLogEntry | null>(null);

  // This is the story of one incident, so it reads the way it happened. Ordering is the server's
  // job: sorting one page here would only hide the pages before it.
  const { data, isLoading, error } = useAuditLogSearch({
    from: dayStart(applied.from),
    to: dayEnd(applied.to),
    action: chosenAction(applied.action),
    resourceId: id,
    resourceTypes: TRAIL_ENTITY_TYPES,
    sortAscending: true,
    page,
    pageSize: PAGE_SIZE,
  });

  const rows = data?.items ?? [];
  const totalPages = data?.totalPages ?? 0;

  // A narrower filter can leave the auditor past the end of a shorter trail.
  usePageWithinBounds(page, totalPages, setPage);

  if (isLoading) {
    return <LoadingState message={t("auditTrail.loading")} />;
  }

  if (error) {
    return (
      <ErrorState
        title={t("auditTrail.loadFailed")}
        message={error instanceof Error ? error.message : t("common.errorOccurred")}
      />
    );
  }

  return (
    <>
      <div className="p-6 space-y-6">
        <PageHeader
          title={t("auditTrail.title")}
          subtitle={t("auditTrail.subtitle", { id })}
          action={
            <div className="flex flex-wrap gap-2">
              <Link to={`/incidents/${id}`}>
                <Button variant="outline" className="bg-input-background">
                  <ExternalLink className="w-4 h-4 mr-2" />
                  {t("auditTrail.openIncident")}
                </Button>
              </Link>
              {/* No entity type: the flat list ANDs one, which would drop the trail's other types. */}
              <Link to={`/audit-logs?entityId=${encodeURIComponent(id)}`}>
                <Button variant="outline" className="bg-input-background">
                  <ScrollText className="w-4 h-4 mr-2" />
                  {t("auditTrail.openFullLog")}
                </Button>
              </Link>
            </div>
          }
        />

        <AuditFilterCard
          draft={draft}
          onChange={set}
          onApply={apply}
          onClear={clear}
          matchCount={data?.totalCount ?? 0}
        />

        <Card className="overflow-hidden bg-card/80 backdrop-blur-sm border-border">
          <div className="overflow-x-auto">
            <table className="w-full">
              <thead>
                <tr className="border-b border-border bg-surface-light/20 text-left text-xs font-semibold tracking-wide text-muted-foreground">
                  <th className="p-4">{t("auditLog.colTime")}</th>
                  <th className="p-4">{t("auditLog.colUser")}</th>
                  <th className="p-4">{t("auditLog.colAction")}</th>
                  <th className="p-4">{t("auditLog.colDescription")}</th>
                  <th className="p-4"></th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => (
                  <AuditEntryRow key={row.id} entry={row} onSelect={setSelected} />
                ))}
              </tbody>
            </table>
          </div>

          {rows.length === 0 && (
            <EmptyState
              icon={ScrollText}
              title={t("auditTrail.noEntries")}
              description={t("auditTrail.noEntriesDesc")}
            />
          )}
        </Card>

        {totalPages > 1 && (
          <div className="flex items-center justify-between">
            <span className="text-sm text-muted-foreground">
              {t("auditLog.pageOf", { page: String(page), total: String(totalPages) })}
            </span>
            <div className="flex gap-2">
              <Button
                variant="outline"
                size="sm"
                disabled={page <= 1}
                onClick={() => setPage((p) => Math.max(1, p - 1))}
                className="bg-input-background"
              >
                {t("common.previous")}
              </Button>
              <Button
                variant="outline"
                size="sm"
                disabled={page >= totalPages}
                onClick={() => setPage((p) => p + 1)}
                className="bg-input-background"
              >
                {t("common.next")}
              </Button>
            </div>
          </div>
        )}
      </div>

      <AuditEntryDialog entry={selected} onClose={() => setSelected(null)} />
    </>
  );
}
