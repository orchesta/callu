import { useEffect } from "react";
import { useFieldArray } from "react-hook-form";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectLabel,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from "@/shared/components/ui/dialog";
import { Plus, Loader2, X } from "lucide-react";
import { t } from "@/shared/locales/i18n";
import { useForm } from "@/shared/hooks/use-form";
import { alertRuleSchema, type AlertRuleFormData } from "@/shared/validations";
import type { AlertRuleDto, AlertRuleMetadata } from "../types/alert-rules.types";

interface AlertRuleEditorProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  editingRule: AlertRuleDto | null;
  metadata: AlertRuleMetadata | undefined;
  teams: Array<{ id: string; name: string }> | undefined;
  users: Array<{ id: string; firstName: string; lastName: string; email: string }> | undefined;
  escalations: Array<{ id: string; name: string }> | undefined;
  onSave: (data: AlertRuleFormData) => void;
  isSaving: boolean;
}

export function AlertRuleEditor({
  open,
  onOpenChange,
  editingRule,
  metadata,
  teams,
  users,
  escalations,
  onSave,
  isSaving,
}: AlertRuleEditorProps) {
  const conditionFields = metadata?.conditionFields ?? [];
  const conditionOperators = metadata?.conditionOperators ?? [];
  const actionTypes = metadata?.actionTypes ?? [];
  const severityValues = metadata?.severityValues ?? [];

  const form = useForm<AlertRuleFormData>(alertRuleSchema, {
    defaultValues: {
      name: "",
      description: "",
      priority: 100,
      isEnabled: true,
      conditions: [{ field: "Severity", operator: "Equals", value: "Critical" }],
      actions: [{ type: "AutoEscalate", target: "", value: "" }],
    },
  });

  const conditionsField = useFieldArray({ control: form.control, name: "conditions" });
  const actionsField = useFieldArray({ control: form.control, name: "actions" });

  useEffect(() => {
    if (!open) return;
    if (editingRule) {
      form.reset({
        name: editingRule.name,
        description: editingRule.description ?? "",
        priority: editingRule.priority,
        isEnabled: editingRule.isEnabled,
        conditions: editingRule.conditions.length > 0
          ? editingRule.conditions
          : [{ field: "Severity", operator: "Equals", value: "" }],
        actions: editingRule.actions.length > 0
          ? editingRule.actions
          : [{ type: "AutoEscalate", target: "", value: "" }],
      });
    } else {
      form.reset({
        name: "",
        description: "",
        priority: 100,
        isEnabled: true,
        conditions: [{ field: "Severity", operator: "Equals", value: "Critical" }],
        actions: [{ type: "AutoEscalate", target: "", value: "" }],
      });
    }
  }, [open, editingRule]); // eslint-disable-line react-hooks/exhaustive-deps

  const onSubmit = form.handleSubmit((data) => {
    onSave(data);
  });

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-4xl max-h-[85vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>{editingRule ? t("alertRules.editRule") : t("alertRules.createAlertRule")}</DialogTitle>
          <DialogDescription>{t("alertRules.dialogDesc")}</DialogDescription>
        </DialogHeader>

        <form onSubmit={onSubmit} className="space-y-5 py-4">
          <div className="grid grid-cols-3 gap-4">
            <div className="col-span-2">
              <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                {t("alertRules.ruleName")}
              </label>
              <Input
                {...form.register("name")}
                placeholder={t("alertRules.nameExamplePlaceholder")}
                className="bg-input-background"
              />
              {form.formState.errors.name && (
                <p className="text-xs text-error-500 mt-1">{form.formState.errors.name.message}</p>
              )}
            </div>
            <div>
              <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                {t("alertRules.priority")}
              </label>
              <Input
                type="number"
                {...form.register("priority", { valueAsNumber: true })}
                min={1}
                max={999}
                className="bg-input-background"
              />
            </div>
          </div>

          <div>
            <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
              {t("common.description")}
            </label>
            <Input
              {...form.register("description")}
              placeholder={t("alertRules.optionalDescriptionPlaceholder")}
              className="bg-input-background"
            />
          </div>

          <div className="flex items-center gap-2 p-3 rounded-lg bg-surface-light/20">
            <input
              type="checkbox"
              id="rule-enabled"
              {...form.register("isEnabled")}
              className="w-4 h-4 rounded border-border bg-input-background"
            />
            <label htmlFor="rule-enabled" style={{ fontSize: "0.875rem", cursor: "pointer" }}>
              {t("alertRules.ruleIsActive")}
            </label>
          </div>

          <div>
            <div className="flex items-center justify-between mb-3">
              <label style={{ fontSize: "0.875rem", fontWeight: 600 }}>
                {t("alertRules.conditions")}{" "}
                <span style={{ fontSize: "0.75rem", color: "#94A3B8", fontWeight: 400 }}>
                  ({t("alertRules.allMustMatch")})
                </span>
              </label>
              <Button
                type="button"
                size="sm"
                variant="outline"
                onClick={() => conditionsField.append({ field: "Severity", operator: "Equals", value: "" })}
                className="bg-input-background"
              >
                <Plus className="w-3 h-3 mr-1" /> {t("common.add")}
              </Button>
            </div>
            {form.formState.errors.conditions?.root && (
              <p className="text-xs text-error-500 mb-2">{form.formState.errors.conditions.root.message}</p>
            )}
            <div className="space-y-2">
              {conditionsField.fields.map((field, i) => {
                const fieldValue = form.watch(`conditions.${i}.field`);
                return (
                  <div key={field.id} className="flex items-center gap-2">
                    <Select
                      value={form.watch(`conditions.${i}.field`)}
                      onValueChange={(v) => form.setValue(`conditions.${i}.field`, v)}
                    >
                      <SelectTrigger className="bg-input-background w-[140px]">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        {conditionFields.map((f) => (
                          <SelectItem key={f.value} value={f.value}>{f.label}</SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                    <Select
                      value={form.watch(`conditions.${i}.operator`)}
                      onValueChange={(v) => form.setValue(`conditions.${i}.operator`, v)}
                    >
                      <SelectTrigger className="bg-input-background w-[130px]">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        {conditionOperators.map((o) => (
                          <SelectItem key={o.value} value={o.value}>{o.label}</SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                    {fieldValue === "Severity" || fieldValue === "Status" ? (
                      <Select
                        value={form.watch(`conditions.${i}.value`)}
                        onValueChange={(v) => form.setValue(`conditions.${i}.value`, v)}
                      >
                        <SelectTrigger className="bg-input-background flex-1">
                          <SelectValue placeholder={t("alertRules.selectValuePlaceholder")} />
                        </SelectTrigger>
                        <SelectContent>
                          {fieldValue === "Severity"
                            ? severityValues.map((s) => <SelectItem key={s} value={s}>{s}</SelectItem>)
                            : ["Open", "Acknowledged", "Resolved"].map((s) => <SelectItem key={s} value={s}>{s}</SelectItem>)
                          }
                        </SelectContent>
                      </Select>
                    ) : (
                      <Input
                        {...form.register(`conditions.${i}.value`)}
                        placeholder={t("alertRules.valueFieldPlaceholder")}
                        className="bg-input-background flex-1"
                      />
                    )}
                    <Button type="button" size="sm" variant="ghost" onClick={() => conditionsField.remove(i)}>
                      <X className="w-4 h-4 text-muted-foreground" />
                    </Button>
                  </div>
                );
              })}
            </div>
          </div>

          <div>
            <div className="flex items-center justify-between mb-3">
              <label style={{ fontSize: "0.875rem", fontWeight: 600 }}>
                {t("alertRules.actions")}{" "}
                <span style={{ fontSize: "0.75rem", color: "#94A3B8", fontWeight: 400 }}>
                  ({t("alertRules.executedInOrder")})
                </span>
              </label>
              <Button
                type="button"
                size="sm"
                variant="outline"
                onClick={() => actionsField.append({ type: "AddNote", value: "" })}
                className="bg-input-background"
              >
                <Plus className="w-3 h-3 mr-1" /> {t("common.add")}
              </Button>
            </div>
            {form.formState.errors.actions?.root && (
              <p className="text-xs text-error-500 mb-2">{form.formState.errors.actions.root.message}</p>
            )}
            <div className="space-y-2">
              {actionsField.fields.map((field, i) => {
                const actionType = form.watch(`actions.${i}.type`);
                return (
                  <div key={field.id} className="flex items-center gap-2">
                    <Select
                      value={actionType}
                      onValueChange={(v) => form.setValue(`actions.${i}.type`, v)}
                    >
                      <SelectTrigger className="bg-input-background w-[180px]">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        {actionTypes.map((a) => (
                          <SelectItem key={a.value} value={a.value}>{a.label}</SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                    {actionType === "SetSeverity" ? (
                      <Select
                        value={form.watch(`actions.${i}.value`) ?? ""}
                        onValueChange={(v) => form.setValue(`actions.${i}.value`, v)}
                      >
                        <SelectTrigger className="bg-input-background flex-1">
                          <SelectValue placeholder={t("alertRules.selectSeverityPlaceholder")} />
                        </SelectTrigger>
                        <SelectContent>
                          {severityValues.map((s) => <SelectItem key={s} value={s}>{s}</SelectItem>)}
                        </SelectContent>
                      </Select>
                    ) : actionType === "AddNote" ? (
                      <Input
                        {...form.register(`actions.${i}.value`)}
                        placeholder={t("alertRules.noteMessagePlaceholder")}
                        className="bg-input-background flex-1"
                      />
                    ) : actionType === "SuppressNotification" ? (
                      <span style={{ fontSize: "0.8125rem", color: "#94A3B8", flex: 1 }}>
                        {t("alertRules.noAdditionalConfig")}
                      </span>
                    ) : (
                      <Select
                        value={form.watch(`actions.${i}.target`) ?? ""}
                        onValueChange={(v) => form.setValue(`actions.${i}.target`, v)}
                      >
                        <SelectTrigger className="bg-input-background flex-1">
                          <SelectValue placeholder={t("alertRules.selectTargetPlaceholder")} />
                        </SelectTrigger>
                        <SelectContent>
                          {escalations && escalations.length > 0 && (
                            <SelectGroup>
                              <SelectLabel className="text-xs text-muted-foreground ml-2 my-1">{t("alertRules.selectGroupEscalations")}</SelectLabel>
                              {escalations.map((ep) => (
                                <SelectItem key={ep.id} value={ep.id}>{ep.name}</SelectItem>
                              ))}
                            </SelectGroup>
                          )}
                          {teams && teams.length > 0 && (
                            <SelectGroup>
                              <SelectLabel className="text-xs text-muted-foreground ml-2 my-1">{t("alertRules.selectGroupTeams")}</SelectLabel>
                              {teams.map((team) => (
                                <SelectItem key={team.id} value={team.id}>{team.name}</SelectItem>
                              ))}
                            </SelectGroup>
                          )}
                          {users && users.length > 0 && (
                            <SelectGroup>
                              <SelectLabel className="text-xs text-muted-foreground ml-2 my-1">{t("alertRules.selectGroupUsers")}</SelectLabel>
                              {users.map((u) => (
                                <SelectItem key={u.id} value={u.id}>{u.firstName} {u.lastName} ({u.email})</SelectItem>
                              ))}
                            </SelectGroup>
                          )}
                        </SelectContent>
                      </Select>
                    )}
                    <Button type="button" size="sm" variant="ghost" onClick={() => actionsField.remove(i)}>
                      <X className="w-4 h-4 text-muted-foreground" />
                    </Button>
                  </div>
                );
              })}
            </div>
          </div>

          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)} className="bg-input-background">
              {t("common.cancel")}
            </Button>
            <Button
              type="submit"
              disabled={isSaving}
              className="bg-brand-500 hover:bg-brand-600 text-white"
            >
              {isSaving ? (
                <><Loader2 className="w-4 h-4 mr-2 animate-spin" />{t("common.saving")}</>
              ) : (
                editingRule ? t("alertRules.updateRule") : t("alertRules.createRule")
              )}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
