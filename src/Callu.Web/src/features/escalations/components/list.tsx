import { useState, useMemo } from "react";
import { t } from "@/shared/locales/i18n";
import { Link, useNavigate } from "react-router";
import { Button } from "@/shared/components/ui/button";
import { Badge } from "@/shared/components/ui/badge";
import { Card } from "@/shared/components/ui/card";
import {
  Plus,
  Edit,
  Trash2,
  Workflow,
  Users,
  Clock,
  ArrowRight,
} from "lucide-react";
import { LoadingState } from "@/shared/components/loading-state";
import { ErrorState } from "@/shared/components/error-state";
import { EmptyState } from "@/shared/components/empty-state";
import { StatCard } from "@/shared/components/stat-card";
import { PageHeader } from "@/shared/components/page-header";
import { SearchInput } from "@/shared/components/search-input";
import { DeleteConfirmDialog } from "@/shared/components/delete-confirm-dialog";
import {
  useEscalationPolicies,
  useDeletePolicy,
} from "../hooks/use-escalations";
import type { EscalationPolicyDto } from "../types/escalation.types";
import React from "react";

const EscalationStats = React.memo(function EscalationStats({ policies }: { policies: EscalationPolicyDto[] }) {
  const { totalSteps, activeCount, avgSteps } = useMemo(() => {
    let steps = 0;
    let active = 0;
    for (const p of policies) {
      steps += p.stepCount;
      if (p.isActive) active++;
    }
    return {
      totalSteps: steps,
      activeCount: active,
      avgSteps: policies.length > 0 ? (steps / policies.length).toFixed(1) : "0",
    };
  }, [policies]);

  return (
    <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
      <StatCard label={t("escalations.totalPolicies")} value={policies.length} />
      <StatCard label={t("escalations.totalSteps")} value={totalSteps} color="#3E7BFA" borderColor="border-brand-500/20" />
      <StatCard label={t("common.active").toUpperCase()} value={activeCount} color="#22C55E" borderColor="border-success-500/20" />
      <StatCard label={t("escalations.avgSteps")} value={avgSteps} color="#FB923C" borderColor="border-warning-500/20" />
    </div>
  );
});

export function EscalationList() {
  const navigate = useNavigate();
  const [searchQuery, setSearchQuery] = useState("");
  const [deleteTarget, setDeleteTarget] = useState<EscalationPolicyDto | null>(null);

  const { data: policies = [], isLoading, error } = useEscalationPolicies();
  const deletePolicy = useDeletePolicy();

  const filteredPolicies = useMemo(() =>
    policies.filter(
      (policy) =>
        policy.name.toLowerCase().includes(searchQuery.toLowerCase()) ||
        (policy.description ?? "").toLowerCase().includes(searchQuery.toLowerCase())
    ),
    [policies, searchQuery],
  );

  const handleDelete = async () => {
    if (!deleteTarget) return;
    deletePolicy.mutate(deleteTarget.id, {
      onSuccess: () => setDeleteTarget(null),
    });
  };

  if (isLoading) {
    return <LoadingState message={t("escalations.loading")} />;
  }

  if (error) {
    return (
      <ErrorState
        title={t("escalations.loadFailed")}
        message={error instanceof Error ? error.message : t("common.errorOccurred")}
      />
    );
  }

  return (
    <div className="p-6 space-y-6">
      <PageHeader
        title={t("escalations.title")}
        subtitle={t("escalations.subtitle")}
        action={
          <Button
            onClick={() => navigate("/escalations/new")}
            className="bg-brand-500 hover:bg-brand-600 text-white shadow-lg shadow-brand-500/20"
          >
            <Plus className="w-4 h-4 mr-2" />
            {t("escalations.createPolicy")}
          </Button>
        }
      />

      <SearchInput
        placeholder={t("escalations.searchPolicies")}
        value={searchQuery}
        onChange={setSearchQuery}
      />

      <EscalationStats policies={filteredPolicies} />

      {filteredPolicies.length > 0 ? (
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
          {filteredPolicies.map((policy) => (
            <Card
              key={policy.id}
              className="p-6 bg-card/80 backdrop-blur-sm border-border hover:border-border-light transition-all hover:shadow-lg"
            >
              <div className="flex items-start justify-between gap-3 mb-4">
                <div className="flex items-start gap-3 flex-1 min-w-0">
                  <div className="w-10 h-10 rounded-lg bg-brand-500/10 flex items-center justify-center flex-shrink-0">
                    <Workflow className="w-5 h-5 text-brand-500" />
                  </div>
                  <div className="flex-1 min-w-0">
                    <Link
                      to={`/escalations/${policy.id}`}
                      className="font-semibold hover:text-brand-500 transition-colors block truncate"
                      style={{ fontSize: "1.0625rem" }}
                    >
                      {policy.name}
                    </Link>
                    <p
                      style={{ fontSize: "0.875rem", color: "#94A3B8" }}
                      className="line-clamp-2 mt-1"
                    >
                      {policy.description || t("escalations.noDescription")}
                    </p>
                  </div>
                </div>
                <Badge
                  className={
                    policy.isActive
                      ? "bg-success-500/10 text-success-500 border-success-500/20 border"
                      : "bg-muted/20 text-muted-foreground border-border border"
                  }
                >
                  {policy.isActive ? t("common.active") : t("common.inactive")}
                </Badge>
              </div>

              <div className="grid grid-cols-2 gap-4 mb-4 p-3 rounded-lg bg-surface-light/20">
                <div>
                  <p
                    style={{
                      fontSize: "0.75rem",
                      color: "#94A3B8",
                      marginBottom: "0.25rem",
                    }}
                  >
                    {t("escalations.escalationSteps")}
                  </p>
                  <p style={{ fontSize: "0.9375rem", fontWeight: 600 }}>
                    {policy.stepCount} {t("escalations.levels").replace("{count}", "").trim()}
                  </p>
                </div>
                <div>
                  <p
                    style={{
                      fontSize: "0.75rem",
                      color: "#94A3B8",
                      marginBottom: "0.25rem",
                    }}
                  >
                    {t("escalations.team")}
                  </p>
                  <p style={{ fontSize: "0.9375rem", fontWeight: 600 }}>
                    {policy.teamName || "—"}
                  </p>
                </div>
              </div>

              <div className="mb-4 p-4 rounded-lg bg-gradient-to-r from-brand-500/5 to-transparent border border-brand-500/20">
                <p
                  style={{
                    fontSize: "0.75rem",
                    color: "#94A3B8",
                    marginBottom: "0.75rem",
                    fontWeight: 600,
                  }}
                >
                  {t("escalations.escalationFlow")}
                </p>
                <div className="flex items-center gap-2 overflow-x-auto pb-2">
                  {policy.steps && policy.steps.length > 0 ? (
                    policy.steps.slice(0, 6).map((step, i) => (
                      <div key={i} className="flex items-center gap-2 flex-shrink-0">
                        <div className="flex flex-col items-center gap-1 min-w-[60px]">
                          <Badge className="bg-brand-500/10 text-brand-500 border-brand-500/20 border text-[10px] px-2 py-0">
                            L{step.level}
                          </Badge>
                          <div className="w-8 h-8 rounded-full bg-brand-500/5 border border-brand-500/10 flex items-center justify-center relative group">
                            {step.scheduleName ? (
                              <Clock className="w-3.5 h-3.5 text-brand-500" />
                            ) : step.teamName ? (
                              <Users className="w-3.5 h-3.5 text-brand-500" />
                            ) : (
                              <Users className="w-3.5 h-3.5 text-brand-500" />
                            )}
                          </div>
                          <span className="text-[10px] text-muted-foreground whitespace-nowrap px-1 max-w-[80px] truncate" title={step.scheduleName || step.teamName || (step.notifyUserIds?.length ? t("escalations.listUsersCount", { count: step.notifyUserIds.length }) : t("escalations.listUnknown"))}>
                            {step.scheduleName || step.teamName || (step.notifyUserIds?.length ? t("escalations.listUsersCount", { count: step.notifyUserIds.length }) : t("escalations.listUnknown"))}
                          </span>
                        </div>
                        {i < Math.min(policy.steps!.length, 6) - 1 && (
                          <div className="flex flex-col items-center justify-center -mt-4">
                            <span className="text-[10px] text-muted-foreground/60 mb-0.5">{policy.steps![i+1]?.delayMinutes || step.delayMinutes}m</span>
                            <ArrowRight className="w-3 h-3 text-muted-foreground/40 flex-shrink-0" />
                          </div>
                        )}
                      </div>
                    ))
                  ) : policy.stepCount > 0 ? (
                    <span className="text-sm text-muted-foreground">{t("escalations.listLegacyHint")}</span>
                  ) : (
                    <span className="text-sm text-muted-foreground">{t("escalations.listNoSteps")}</span>
                  )}
                </div>
              </div>

              {policy.teamName && (
                <div className="mb-4">
                  <p
                    style={{
                      fontSize: "0.75rem",
                      color: "#94A3B8",
                      marginBottom: "0.5rem",
                    }}
                  >
                    {t("escalations.assignedTeam")}
                  </p>
                  <div className="flex flex-wrap gap-2">
                    <Badge className="bg-muted/20 text-foreground border-border border text-xs">
                      <Users className="w-3 h-3 mr-1" />
                      {policy.teamName}
                    </Badge>
                  </div>
                </div>
              )}

              <div className="flex gap-2 pt-4 border-t border-border">
                <Link to={`/escalations/${policy.id}`} className="flex-1">
                  <Button variant="outline" className="w-full bg-input-background">
                    <Edit className="w-4 h-4 mr-2" />
                    {t("escalations.editPolicyBtn")}
                  </Button>
                </Link>
                <Button
                  variant="outline"
                  size="sm"
                  className="bg-input-background hover:bg-error-500/10 hover:text-error-500"
                  onClick={() => setDeleteTarget(policy)}
                >
                  <Trash2 className="w-4 h-4" />
                </Button>
              </div>
            </Card>
          ))}
        </div>
      ) : (
        <EmptyState
          icon={Workflow}
          title={t("escalations.noPoliciesFound")}
          description={searchQuery ? t("escalations.adjustSearch") : t("escalations.createFirstPolicy")}
          action={
            <Button onClick={() => navigate("/escalations/new")} className="bg-brand-500 hover:bg-brand-600">
              <Plus className="w-4 h-4 mr-2" />
              {t("escalations.createPolicy")}
            </Button>
          }
        />
      )}

      <DeleteConfirmDialog
        open={!!deleteTarget}
        onOpenChange={(open) => !open && setDeleteTarget(null)}
        title={t("escalations.deletePolicyTitle")}
        message={t("escalations.deletePolicyMsg").replace("{name}", deleteTarget?.name ?? "")}
        warning={t("escalations.deletePolicyWarn")}
        onConfirm={handleDelete}
        isLoading={deletePolicy.isPending}
        confirmLabel={t("escalations.deletePolicy")}
        cancelLabel={t("common.cancel")}
      />
    </div>
  );
}