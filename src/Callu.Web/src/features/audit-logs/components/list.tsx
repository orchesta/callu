import { useEffect, useState, useId } from "react";
import { Link, useSearchParams } from "react-router";
import { t } from "@/shared/locales/i18n";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Card } from "@/shared/components/ui/card";
import { ScrollText, Download, ShieldCheck, ShieldAlert } from "lucide-react";
import { LoadingState } from "@/shared/components/loading-state";
import { ErrorState } from "@/shared/components/error-state";
import { EmptyState } from "@/shared/components/empty-state";
import { PageHeader } from "@/shared/components/page-header";
import { useAuditLogSearch, useVerifyAuditChain } from "../hooks/use-audit-logs";
import { useAuditFilterDraft, usePageWithinBounds } from "../hooks/use-filter-draft";
import { ANY_ACTION, chosenAction, dayEnd, dayStart, EMPTY_AUDIT_FILTER } from "../utils/filter-draft";
import type { AuditFilterDraft } from "../utils/filter-draft";
import { downloadAuditExport } from "../api/audit-log.api";
import { toast } from "@/shared/utils";
import type { AuditLogEntry } from "../types/audit-log.types";
import { AuditEntryRow } from "./entry-row";
import { AuditEntryDialog } from "./entry-dialog";
import { AuditFilterCard } from "./filter-card";

const PAGE_SIZE = 50;
const INCIDENT_ENTITY = "Incident";

interface Draft extends AuditFilterDraft {
  resourceType: string;
  resourceId: string;
  actorId: string;
  query: string;
}

const EMPTY_DRAFT: Draft = {
  ...EMPTY_AUDIT_FILTER, resourceType: "", resourceId: "", actorId: "", query: "",
};

/** Lets another screen hand the trail a record to open on, e.g. ?entityType=Incident&entityId=… */
function draftFromParams(params: URLSearchParams): Draft {
  return {
    ...EMPTY_DRAFT,
    resourceType: params.get("entityType") ?? "",
    resourceId: params.get("entityId") ?? "",
  };
}

export function AuditLogList() {
  const formId = useId();
  const FIELD_ENTITY = formId + "-entity";
  const FIELD_ENTITY_ID = formId + "-entity-id";
  const FIELD_USER = formId + "-user";
  const FIELD_QUERY = formId + "-query";
  const [searchParams, setSearchParams] = useSearchParams();
  const { draft, applied, page, setPage, set, apply, applyNow, clear } =
    useAuditFilterDraft<Draft>(EMPTY_DRAFT, () => draftFromParams(searchParams));
  const [selected, setSelected] = useState<AuditLogEntry | null>(null);

  // The URL owns the narrowing, so arriving here again on the same route replaces it instead of
  // leaving the screen asking for whatever the previous visit named.
  useEffect(() => { applyNow(draftFromParams(searchParams)); }, [searchParams, applyNow]);

  const { data, isLoading, error } = useAuditLogSearch({
    from: dayStart(applied.from),
    to: dayEnd(applied.to),
    action: chosenAction(applied.action),
    resourceType: applied.resourceType.trim() || undefined,
    resourceId: applied.resourceId.trim() || undefined,
    actorId: applied.actorId.trim() || undefined,
    query: applied.query.trim() || undefined,
    page,
    pageSize: PAGE_SIZE,
  });

  const rows = data?.items ?? [];
  const totalCount = data?.totalCount ?? 0;
  const totalPages = data?.totalPages ?? 0;

  usePageWithinBounds(page, totalPages, setPage);

  const verify = useVerifyAuditChain();
  const [exporting, setExporting] = useState<"csv" | "jsonl" | null>(null);

  /// Narrows the whole trail to one record — the question an auditor asks after reading a row.
  // No entity type: the backend ANDs one, and a record's rows span several. Applied here as well as
  // pushed, because pushing the URL it already has is not a change and would do nothing.
  const filterToEntity = (resourceId: string) => {
    applyNow({ ...EMPTY_DRAFT, resourceId });
    setSearchParams({ entityId: resourceId }, { replace: draftFromParams(searchParams).resourceId === resourceId });
  };

  // Clearing has to drop the narrowing the URL carries too, or a reload puts it straight back.
  const clearAll = () => { clear(); setSearchParams({}, { replace: true }); };

  // The export must send the filters the screen is showing, so it is built from `applied`.
  const exportParams = () => {
    const params = new URLSearchParams();
    const from = dayStart(applied.from);
    const to = dayEnd(applied.to);
    if (from) params.set("from", from);
    if (to) params.set("to", to);
    if (applied.action !== ANY_ACTION) params.set("action", applied.action);
    if (applied.resourceType.trim()) params.set("resourceType", applied.resourceType.trim());
    if (applied.resourceId.trim()) params.set("resourceId", applied.resourceId.trim());
    if (applied.actorId.trim()) params.set("actorId", applied.actorId.trim());
    if (applied.query.trim()) params.set("query", applied.query.trim());
    return params;
  };

  const download = async (format: "csv" | "jsonl") => {
    setExporting(format);
    try {
      await downloadAuditExport(format, exportParams());
    } catch {
      toast.error(t("auditLog.exportFailed"));
    } finally {
      setExporting(null);
    }
  };

  if (isLoading) {
    return <LoadingState message={t("auditLog.loading")} />;
  }

  if (error) {
    return (
      <ErrorState
        title={t("auditLog.loadFailed")}
        message={error instanceof Error ? error.message : t("common.errorOccurred")}
      />
    );
  }

  return (
    <>
      <div className="p-6 space-y-6">
        <PageHeader title={t("auditLog.title")} subtitle={t("auditLog.subtitle")} />

        <AuditFilterCard
          draft={draft}
          onChange={set}
          onApply={apply}
          onClear={clearAll}
          matchCount={totalCount}
          extraFields={
            <>
              <div>
                <label htmlFor={FIELD_ENTITY} className="mb-2 block text-xs font-semibold tracking-wide text-muted-foreground">
                  {t("auditLog.entityFilter")}
                </label>
                <Input
                  id={FIELD_ENTITY}
                  placeholder={t("auditLog.entityPlaceholder")}
                  value={draft.resourceType}
                  onChange={(e) => set("resourceType", e.target.value)}
                  onKeyDown={(e) => { if (e.key === "Enter") apply(); }}
                  className="bg-input-background"
                />
              </div>
              <div>
                <label htmlFor={FIELD_ENTITY_ID} className="mb-2 block text-xs font-semibold tracking-wide text-muted-foreground">
                  {t("auditLog.entityIdFilter")}
                </label>
                <Input
                  id={FIELD_ENTITY_ID}
                  placeholder={t("auditLog.entityIdPlaceholder")}
                  value={draft.resourceId}
                  onChange={(e) => set("resourceId", e.target.value)}
                  onKeyDown={(e) => { if (e.key === "Enter") apply(); }}
                  className="bg-input-background font-mono text-xs"
                />
              </div>
              <div>
                <label htmlFor={FIELD_USER} className="mb-2 block text-xs font-semibold tracking-wide text-muted-foreground">
                  {t("auditLog.userFilter")}
                </label>
                <Input
                  id={FIELD_USER}
                  placeholder={t("auditLog.userPlaceholder")}
                  value={draft.actorId}
                  onChange={(e) => set("actorId", e.target.value)}
                  onKeyDown={(e) => { if (e.key === "Enter") apply(); }}
                  className="bg-input-background font-mono text-xs"
                />
              </div>
              <div className="sm:col-span-2">
                <label htmlFor={FIELD_QUERY} className="mb-2 block text-xs font-semibold tracking-wide text-muted-foreground">
                  {t("auditLog.freeText")}
                </label>
                <Input
                  id={FIELD_QUERY}
                  placeholder={t("auditLog.freeTextPlaceholder")}
                  value={draft.query}
                  onChange={(e) => set("query", e.target.value)}
                  onKeyDown={(e) => { if (e.key === "Enter") apply(); }}
                  className="bg-input-background"
                />
              </div>
            </>
          }
          actions={
            <div className="ml-auto flex gap-2">
              <Button
                type="button"
                variant="outline"
                className="bg-input-background"
                title={t("auditLog.verifyHint")}
                disabled={verify.isPending}
                onClick={() => verify.mutate(undefined)}
              >
                <ShieldCheck className="w-4 h-4 mr-2" />
                {verify.isPending ? t("auditLog.verifying") : t("auditLog.verify")}
              </Button>
              <Button
                type="button"
                variant="outline"
                className="bg-input-background"
                disabled={exporting !== null}
                onClick={() => void download("csv")}
              >
                <Download className="w-4 h-4 mr-2" />
                {exporting === "csv" ? t("auditLog.exporting") : t("auditLog.exportCsv")}
              </Button>
              <Button
                type="button"
                variant="outline"
                className="bg-input-background"
                disabled={exporting !== null}
                onClick={() => void download("jsonl")}
              >
                <Download className="w-4 h-4 mr-2" />
                {exporting === "jsonl" ? t("auditLog.exporting") : t("auditLog.exportJsonl")}
              </Button>
            </div>
          }
        >
          {verify.data && (
            <div
              role="status"
              className={`mt-4 flex items-start gap-2 rounded-md border p-3 text-sm ${verify.data.intact
                ? "border-success-500/30 bg-success-500/10"
                : "border-error-500/30 bg-error-500/10"}`}
            >
              {verify.data.intact
                ? <ShieldCheck className="mt-0.5 h-4 w-4 flex-shrink-0 text-success-500" />
                : <ShieldAlert className="mt-0.5 h-4 w-4 flex-shrink-0 text-error-500" />}
              <span>
                {verify.data.intact
                  ? t("auditLog.verifyIntact", { count: String(verify.data.checkedCount) })
                  : t("auditLog.verifyBroken", {
                      sequence: String(verify.data.firstBrokenSequence ?? "?"),
                      reason: verify.data.reason ?? "",
                    })}
              </span>
            </div>
          )}
        </AuditFilterCard>

        <Card className="overflow-hidden bg-card/80 backdrop-blur-sm border-border">
          <div className="overflow-x-auto">
            <table className="w-full">
              <thead>
                <tr className="border-b border-border bg-surface-light/20 text-left text-xs font-semibold tracking-wide text-muted-foreground">
                  <th className="p-4">{t("auditLog.colTime")}</th>
                  <th className="p-4">{t("auditLog.colUser")}</th>
                  <th className="p-4">{t("auditLog.colAction")}</th>
                  <th className="p-4">{t("auditLog.colEntity")}</th>
                  <th className="p-4">{t("auditLog.colDescription")}</th>
                  <th className="p-4"></th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => (
                  <AuditEntryRow
                    key={row.id}
                    entry={row}
                    onSelect={setSelected}
                    entityCell={
                      <td className="p-4 text-sm">
                        <span className="font-medium">{row.resourceType}</span>
                        {row.resourceId && (
                          <span className="ml-1 inline-flex items-center gap-1">
                            <button
                              type="button"
                              className="font-mono text-xs text-brand-400 underline-offset-2 hover:underline"
                              title={t("auditLog.filterToThisEntity", { id: row.resourceId })}
                              onClick={(e) => { e.stopPropagation(); filterToEntity(row.resourceId!); }}
                            >
                              {row.resourceId.slice(0, 8)}
                            </button>
                            {row.resourceType === INCIDENT_ENTITY && (
                              <Link
                                to={`/audit-logs/incident/${row.resourceId}`}
                                className="text-brand-400 hover:text-brand-300"
                                title={t("auditLog.openIncidentTrail")}
                                onClick={(e) => e.stopPropagation()}
                              >
                                <ScrollText className="h-3.5 w-3.5" />
                              </Link>
                            )}
                          </span>
                        )}
                      </td>
                    }
                  />
                ))}
              </tbody>
            </table>
          </div>

          {rows.length === 0 && (
            <EmptyState
              icon={ScrollText}
              title={t("auditLog.noEntries")}
              description={t("auditLog.noEntriesDesc")}
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
