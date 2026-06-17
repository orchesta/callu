import { useState } from "react";
import { Button } from "@/shared/components/ui/button";
import { Badge } from "@/shared/components/ui/badge";
import { Card } from "@/shared/components/ui/card";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from "@/shared/components/ui/dialog";
import { Plus, Trash2, Loader2, Zap, Power, PowerOff, Pencil } from "lucide-react";
import { LoadingState } from "@/shared/components/loading-state";
import { ErrorState } from "@/shared/components/error-state";
import { t } from "@/shared/locales/i18n";
import type { AlertRuleFormData } from "@/shared/validations";
import {
  useAlertRules,
  useCreateAlertRule,
  useUpdateAlertRule,
  useDeleteAlertRule,
  useToggleAlertRule,
  useAlertRuleMetadata,
} from "../hooks/use-alert-rules";
import type { AlertRuleDto } from "../types/alert-rules.types";
import { useTeams } from "@/features/teams/hooks/use-teams";
import { useUsers } from "@/features/users/hooks/use-users";
import { useEscalationPolicies } from "@/features/escalations/hooks/use-escalations";
import { AlertRuleEditor } from "./alert-rule-editor";

export function AlertRulesSettings() {
  const { data: rules, isLoading, error } = useAlertRules();
  const { data: metadata } = useAlertRuleMetadata();
  const createRule = useCreateAlertRule();
  const updateRule = useUpdateAlertRule();
  const deleteRule = useDeleteAlertRule();
  const toggleRule = useToggleAlertRule();

  const { data: teams } = useTeams();
  const { data: users } = useUsers();
  const { data: escalations } = useEscalationPolicies();

  const [isEditorOpen, setIsEditorOpen] = useState(false);
  const [editingRule, setEditingRule] = useState<AlertRuleDto | null>(null);
  const [isDeleteModalOpen, setIsDeleteModalOpen] = useState(false);
  const [deletingRule, setDeletingRule] = useState<AlertRuleDto | null>(null);

  const openCreate = () => {
    setEditingRule(null);
    setIsEditorOpen(true);
  };

  const openEdit = (rule: AlertRuleDto) => {
    setEditingRule(rule);
    setIsEditorOpen(true);
  };

  const handleSave = (data: AlertRuleFormData) => {
    const payload = {
      ...data,
      conditions: data.conditions.filter((c) => c.field && c.value),
      actions: data.actions.filter((a) => a.type),
    };

    if (editingRule) {
      updateRule.mutate(
        { id: editingRule.id, data: payload },
        { onSuccess: () => setIsEditorOpen(false) },
      );
    } else {
      createRule.mutate(payload, { onSuccess: () => setIsEditorOpen(false) });
    }
  };

  const handleDelete = () => {
    if (!deletingRule) return;
    deleteRule.mutate(deletingRule.id, {
      onSuccess: () => {
        setIsDeleteModalOpen(false);
        setDeletingRule(null);
      },
    });
  };

  if (isLoading) return <LoadingState message={t("settings.alertRules.loading")} />;
  if (error) return <ErrorState title={t("settings.alertRules.loadFailed")} message={error.message} />;

  return (
    <div className="space-y-6">
      <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
        <div className="flex items-center justify-between mb-6">
          <div>
            <h3 style={{ fontSize: "1.125rem", fontWeight: 600 }}>{t("alertRules.title")}</h3>
            <p style={{ fontSize: "0.875rem", color: "#94A3B8", marginTop: "0.25rem" }}>
              {t("alertRules.subtitle")}
            </p>
          </div>
          <Button onClick={openCreate} className="bg-brand-500 hover:bg-brand-600 text-white">
            <Plus className="w-4 h-4 mr-2" />
            Create Rule
          </Button>
        </div>

        {!rules || rules.length === 0 ? (
          <div className="text-center py-12">
            <Zap className="w-12 h-12 text-muted-foreground mx-auto mb-3 opacity-50" />
            <p style={{ fontSize: "0.9375rem", fontWeight: 600, marginBottom: "0.5rem" }}>
              {t("alertRules.noRulesTitle")}
            </p>
            <p style={{ fontSize: "0.875rem", color: "#94A3B8", marginBottom: "1.5rem" }}>
              {t("alertRules.noRulesDesc")}
            </p>
            <Button onClick={openCreate} className="bg-brand-500 hover:bg-brand-600 text-white">
              <Plus className="w-4 h-4 mr-2" />
              {t("alertRules.createFirstRule")}
            </Button>
          </div>
        ) : (
          <div className="space-y-3">
            {rules.map((rule) => (
              <div
                key={rule.id}
                className={`p-4 rounded-lg border transition-all ${
                  rule.isEnabled
                    ? "border-border bg-surface-light/10"
                    : "border-border/50 bg-surface-light/5 opacity-60"
                }`}
              >
                <div className="flex items-start justify-between gap-4">
                  <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-2 mb-1">
                      <span style={{ fontSize: "0.9375rem", fontWeight: 600 }}>{rule.name}</span>
                      <Badge
                        className={`text-xs ${
                          rule.isEnabled
                            ? "bg-success-500/10 text-success-500 border-success-500/20"
                            : "bg-muted/20 text-muted-foreground border-muted/30"
                        } border`}
                      >
                        {rule.isEnabled ? t("common.active") : t("alertRules.disabled")}
                      </Badge>
                      <Badge className="bg-brand-500/10 text-brand-400 border-brand-500/20 border text-xs">
                        {t("alertRules.priority")} {rule.priority}
                      </Badge>
                    </div>
                    {rule.description && (
                      <p style={{ fontSize: "0.8125rem", color: "#94A3B8", marginBottom: "0.5rem" }}>
                        {rule.description}
                      </p>
                    )}
                    <div className="flex items-center gap-4 text-xs" style={{ color: "#64748B" }}>
                      <span>
                        {rule.conditions.length} condition{rule.conditions.length !== 1 ? "s" : ""}
                      </span>
                      <span>&middot;</span>
                      <span>
                        {rule.actions.length} action{rule.actions.length !== 1 ? "s" : ""}
                      </span>
                      <span>&middot;</span>
                      <span>Triggered {rule.triggerCount}x</span>
                      {rule.lastTriggeredAt && (
                        <>
                          <span>&middot;</span>
                          <span>Last: {new Date(rule.lastTriggeredAt).toLocaleString()}</span>
                        </>
                      )}
                    </div>
                  </div>
                  <div className="flex items-center gap-1 flex-shrink-0">
                    <Button
                      size="sm"
                      variant="ghost"
                      onClick={() => toggleRule.mutate(rule.id)}
                      title={rule.isEnabled ? t("alertRules.disable") : t("alertRules.enable")}
                    >
                      {rule.isEnabled ? (
                        <PowerOff className="w-4 h-4 text-warning-500" />
                      ) : (
                        <Power className="w-4 h-4 text-success-500" />
                      )}
                    </Button>
                    <Button size="sm" variant="ghost" onClick={() => openEdit(rule)}>
                      <Pencil className="w-4 h-4" />
                    </Button>
                    <Button
                      size="sm"
                      variant="ghost"
                      onClick={() => {
                        setDeletingRule(rule);
                        setIsDeleteModalOpen(true);
                      }}
                      className="text-error-500 hover:text-error-400"
                    >
                      <Trash2 className="w-4 h-4" />
                    </Button>
                  </div>
                </div>
              </div>
            ))}
          </div>
        )}
      </Card>

      <AlertRuleEditor
        open={isEditorOpen}
        onOpenChange={setIsEditorOpen}
        editingRule={editingRule}
        metadata={metadata}
        teams={teams as Array<{ id: string; name: string }> | undefined}
        users={users as Array<{ id: string; firstName: string; lastName: string; email: string }> | undefined}
        escalations={escalations as Array<{ id: string; name: string }> | undefined}
        onSave={handleSave}
        isSaving={createRule.isPending || updateRule.isPending}
      />

      <Dialog open={isDeleteModalOpen} onOpenChange={setIsDeleteModalOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{t("alertRules.deleteTitle")}</DialogTitle>
            <DialogDescription>
              {t("alertRules.deleteConfirm").replace("{name}", deletingRule?.name ?? "")}
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button
              variant="outline"
              onClick={() => setIsDeleteModalOpen(false)}
              className="bg-input-background"
            >
              {t("common.cancel")}
            </Button>
            <Button
              onClick={handleDelete}
              disabled={deleteRule.isPending}
              className="bg-error-500 hover:bg-error-600 text-white"
            >
              {deleteRule.isPending ? (
                <>
                  <Loader2 className="w-4 h-4 mr-2 animate-spin" />
                  {t("common.deleting")}
                </>
              ) : (
                <>
                  <Trash2 className="w-4 h-4 mr-2" />
                  {t("alertRules.deleteRule")}
                </>
              )}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
