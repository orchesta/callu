import { useId, type ReactNode } from "react";
import { t } from "@/shared/locales/i18n";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Card } from "@/shared/components/ui/card";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";
import { Search } from "lucide-react";
import { AUDIT_ACTIONS } from "../types/audit-log.types";
import { ANY_ACTION } from "../utils/filter-draft";
import type { AuditFilterDraft } from "../utils/filter-draft";

interface AuditFilterCardProps {
  draft: AuditFilterDraft;
  onChange: (key: keyof AuditFilterDraft, value: string) => void;
  onApply: () => void;
  onClear: () => void;
  matchCount: number;
  /** Fields added after the shared three; the flat list puts entity, record, actor and text there. */
  extraFields?: ReactNode;
  /** Controls added at the end of the button row, such as verify and export. */
  actions?: ReactNode;
  /** Anything below the button row, such as the chain-verification banner. */
  children?: ReactNode;
}

export function AuditFilterCard({
  draft,
  onChange,
  onApply,
  onClear,
  matchCount,
  extraFields,
  actions,
  children,
}: AuditFilterCardProps) {
  const formId = useId();
  const FIELD_FROM = formId + "-from";
  const FIELD_TO = formId + "-to";
  const FIELD_ACTION = formId + "-action";

  return (
    <Card className="p-4 bg-card/80 backdrop-blur-sm border-border">
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        <div>
          <label htmlFor={FIELD_FROM} className="mb-2 block text-xs font-semibold tracking-wide text-muted-foreground">
            {t("auditLog.fromDate")}
          </label>
          <Input
            id={FIELD_FROM}
            type="date"
            value={draft.from}
            onChange={(e) => onChange("from", e.target.value)}
            className="bg-input-background"
          />
        </div>
        <div>
          <label htmlFor={FIELD_TO} className="mb-2 block text-xs font-semibold tracking-wide text-muted-foreground">
            {t("auditLog.toDate")}
          </label>
          <Input
            id={FIELD_TO}
            type="date"
            value={draft.to}
            onChange={(e) => onChange("to", e.target.value)}
            className="bg-input-background"
          />
        </div>
        <div>
          <label htmlFor={FIELD_ACTION} className="mb-2 block text-xs font-semibold tracking-wide text-muted-foreground">
            {t("auditLog.colAction")}
          </label>
          <Select value={draft.action} onValueChange={(v) => onChange("action", v)}>
            <SelectTrigger id={FIELD_ACTION} className="bg-input-background">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={ANY_ACTION}>{t("auditLog.anyAction")}</SelectItem>
              {AUDIT_ACTIONS.map((a) => (
                <SelectItem key={a} value={a}>{a}</SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        {extraFields}
      </div>

      <div className="mt-4 flex flex-wrap items-center gap-3">
        <Button type="button" variant="secondary" onClick={onApply}>
          <Search className="w-4 h-4 mr-2" />
          {t("auditLog.filter")}
        </Button>
        <Button type="button" variant="outline" onClick={onClear} className="bg-input-background">
          {t("auditLog.clearFilters")}
        </Button>
        <span className="text-xs text-muted-foreground">
          {t("auditLog.matchCount", { count: String(matchCount) })}
        </span>
        {actions}
      </div>

      {children}
    </Card>
  );
}
