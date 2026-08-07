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
import { AlertCircle, Clock, GripVertical, Trash2 } from "lucide-react";
import { t } from "@/shared/locales/i18n";
import type { LocalStep } from "../utils/step-mapping";

interface TargetOption {
  value: string;
  label: string;
}

interface TargetTypeOption {
  value: LocalStep["targetType"];
  label: string;
  icon: React.ComponentType<{ className?: string }>;
}

export interface StepCardProps {
  step: LocalStep;
  /** Position in the ladder. Only level 1 (index 0) hides the wait-duration hint. */
  index: number;
  /** Namespaces this card's control ids, so several cards on screen stay individually labelled. */
  formId: string;
  stepIssues: string[];
  targetTypeOptions: TargetTypeOption[];
  getTargetOptions: (type: string) => TargetOption[];
  draggedStepId: string | null;
  onDragStart: (stepId: string) => void;
  onDragOver: (e: React.DragEvent, targetStepId: string) => void;
  onDragEnd: () => void;
  onUpdate: (stepId: string, field: keyof LocalStep, value: LocalStep[keyof LocalStep]) => void;
  /**
   * Separate from onUpdate because switching type has to clear the old target in the same write —
   * onUpdate sets one field at a time, which would briefly leave a stale id under the new type.
   */
  onChangeTargetType: (stepId: string, targetType: LocalStep["targetType"]) => void;
  onRemove: (stepId: string) => void;
}

export function StepCard({
  step,
  index,
  formId,
  stepIssues,
  targetTypeOptions,
  getTargetOptions,
  draggedStepId,
  onDragStart,
  onDragOver,
  onDragEnd,
  onUpdate,
  onChangeTargetType,
  onRemove,
}: StepCardProps) {
  const hasErrors = stepIssues.length > 0;

  return (
                  <Card
                    draggable
                    onDragStart={() => onDragStart(step.id)}
                    onDragOver={(e) => onDragOver(e, step.id)}
                    onDragEnd={onDragEnd}
                    className={`p-5 bg-card/80 backdrop-blur-sm transition-all cursor-move ${hasErrors
                      ? "border-error-500/50 shadow-lg shadow-error-500/10"
                      : "border-border hover:border-border-light"
                      } ${draggedStepId === step.id ? "opacity-50" : ""}`}
                  >
                    <div className="flex items-center gap-3 mb-4">
                      <GripVertical className="w-5 h-5 text-muted-foreground cursor-grab active:cursor-grabbing" />
                      <Badge className="bg-brand-500/10 text-brand-500 border-brand-500/20 border">
                        {t("escalations.level", { level: String(step.level) })}
                      </Badge>
                      {index > 0 && (
                        <div className="flex items-center gap-2 text-sm text-muted-foreground">
                          <Clock className="w-4 h-4" />
                          <span className="font-mono">
                            Wait {step.delayMinutes} min
                          </span>
                        </div>
                      )}
                      <Button
                        size="sm"
                        variant="ghost"
                        onClick={() => onRemove(step.id)}
                        className="ml-auto text-error-500 hover:bg-error-500/10"
                      >
                        <Trash2 className="w-4 h-4" />
                      </Button>
                    </div>

                    <div className="space-y-4">
                      <div>
                        <label
                          style={{
                            fontSize: "0.875rem",
                            fontWeight: 600,
                            marginBottom: "0.5rem",
                            display: "block",
                          }}
                        >
                          {t("escalations.stepTitleLabel")}
                        </label>
                        <Input
                          placeholder={t("escalations.stepTitlePlaceholder")}
                          value={step.title}
                          onChange={(e) =>
                            onUpdate(step.id, "title", e.target.value)
                          }
                          className="bg-input-background"
                        />
                      </div>

                      <div>
                        <label
                          style={{
                            fontSize: "0.875rem",
                            fontWeight: 600,
                            marginBottom: "0.5rem",
                            display: "block",
                          }}
                        >
                          {t("escalations.waitDurationLabel")}
                        </label>
                        <div className="flex items-center gap-2">
                          <Input
                            type="number"
                            min="0"
                            value={step.delayMinutes}
                            onChange={(e) =>
                              onUpdate(
                                step.id,
                                "delayMinutes",
                                parseInt(e.target.value) || 0
                              )
                            }
                            className="bg-input-background"
                            disabled={step.level === 1}
                          />
                          {step.level === 1 && (
                            <span
                              style={{ fontSize: "0.75rem", color: "#94A3B8" }}
                            >
                              {t("escalations.firstStepImmediateHint")}
                            </span>
                          )}
                        </div>
                      </div>

                      <div>
                        <label
                          htmlFor={`${formId}-step-${step.id}-notify`}
                          style={{
                            fontSize: "0.875rem",
                            fontWeight: 600,
                            marginBottom: "0.5rem",
                            display: "block",
                          }}
                        >
                          {t("escalations.stepNotifyLabel")}
                        </label>
                        <Select
                          value={step.targetType}
                          onValueChange={(value: string) =>
                            onChangeTargetType(step.id, value as LocalStep["targetType"])
                          }
                        >
                          <SelectTrigger
                            id={`${formId}-step-${step.id}-notify`}
                            className="bg-input-background"
                          >
                            <SelectValue />
                          </SelectTrigger>
                          <SelectContent>
                            {targetTypeOptions.map((option) => (
                              <SelectItem key={option.value} value={option.value}>
                                <div className="flex items-center gap-2">
                                  <option.icon className="w-4 h-4" />
                                  <span>{option.label}</span>
                                </div>
                              </SelectItem>
                            ))}
                          </SelectContent>
                        </Select>
                      </div>

                      <div>
                        <label
                          htmlFor={
                            step.targetType === "user"
                              ? undefined
                              : `${formId}-step-${step.id}-target`
                          }
                          style={{
                            fontSize: "0.875rem",
                            fontWeight: 600,
                            marginBottom: "0.5rem",
                            display: "block",
                          }}
                        >
                          {step.targetType === "schedule"
                            ? t("escalations.targetSchedule")
                            : step.targetType === "team"
                              ? t("escalations.team")
                              : t("escalations.targetUsers")}
                        </label>
                        {step.targetType === "user" ? (
                          <div className="max-h-64 overflow-auto rounded-lg border border-border bg-input-background p-2 space-y-1">
                            {getTargetOptions("user").map((option) => {
                              const selected = step.targetUserIds.includes(option.value);
                              return (
                                <label
                                  key={option.value}
                                  className="flex items-center gap-2 px-2 py-1 rounded hover:bg-surface-light/30 cursor-pointer"
                                >
                                  <input
                                    type="checkbox"
                                    checked={selected}
                                    onChange={(e) => {
                                      const next = e.target.checked
                                        ? [...step.targetUserIds, option.value]
                                        : step.targetUserIds.filter((id) => id !== option.value);
                                      onUpdate(step.id, "targetUserIds", next);
                                      onUpdate(step.id, "targetValue", next[0] ?? "");
                                    }}
                                    className="w-4 h-4 rounded border-border"
                                  />
                                  <span style={{ fontSize: "0.875rem" }}>{option.label}</span>
                                </label>
                              );
                            })}
                            {getTargetOptions("user").length === 0 && (
                              <p style={{ fontSize: "0.75rem", color: "#64748B", padding: "0.5rem" }}>
                                No users available.
                              </p>
                            )}
                          </div>
                        ) : (
                          <Select
                            value={step.targetValue}
                            onValueChange={(value) =>
                              onUpdate(step.id, "targetValue", value)
                            }
                          >
                            <SelectTrigger
                              id={`${formId}-step-${step.id}-target`}
                              className="bg-input-background"
                            >
                              <SelectValue
                                placeholder={step.targetType === "schedule" ? t("escalations.selectSchedulePlaceholder") : t("escalations.selectTeamPlaceholder")}
                              />
                            </SelectTrigger>
                            <SelectContent>
                              {getTargetOptions(step.targetType).map((option) => (
                                <SelectItem key={option.value} value={option.value}>
                                  {option.label}
                                </SelectItem>
                              ))}
                            </SelectContent>
                          </Select>
                        )}
                      </div>

                      {step.targetType === "team" && (
                        <div className="flex items-center gap-2 p-3 rounded-lg bg-surface-light/20">
                          <input
                            type="checkbox"
                            id={`notify-all-${step.id}`}
                            checked={step.notifyAll}
                            onChange={(e) =>
                              onUpdate(
                                step.id,
                                "notifyAll",
                                e.target.checked
                              )
                            }
                            className="w-4 h-4 rounded border-border bg-input-background"
                          />
                          <label
                            htmlFor={`notify-all-${step.id}`}
                            style={{ fontSize: "0.875rem", cursor: "pointer" }}
                          >
                            {t("escalations.notifyAllTeamMembers")}
                            <span
                              style={{
                                fontSize: "0.75rem",
                                color: "#94A3B8",
                                display: "block",
                                marginTop: "0.125rem",
                              }}
                            >
                              {t("escalations.notifyAllTeamMembersHint")}
                            </span>
                          </label>
                        </div>
                      )}

                      {step.targetType === "schedule" && (
                        <div className="flex items-center gap-2 p-3 rounded-lg bg-surface-light/20">
                          <input
                            type="checkbox"
                            id={`notify-both-${step.id}`}
                            checked={step.notifyBothOnCall}
                            onChange={(e) =>
                              onUpdate(
                                step.id,
                                "notifyBothOnCall",
                                e.target.checked
                              )
                            }
                            className="w-4 h-4 rounded border-border bg-input-background"
                          />
                          <label
                            htmlFor={`notify-both-${step.id}`}
                            style={{ fontSize: "0.875rem", cursor: "pointer" }}
                          >
                            {t("escalations.pageBothOnCall")}
                            <span
                              style={{
                                fontSize: "0.75rem",
                                color: "#94A3B8",
                                display: "block",
                                marginTop: "0.125rem",
                              }}
                            >
                              {t("escalations.pageBothOnCallHint")}
                            </span>
                          </label>
                        </div>
                      )}

                      {hasErrors && (
                        <div className="p-3 rounded-lg bg-error-500/10 border border-error-500/20 flex items-start gap-2">
                          <AlertCircle className="w-4 h-4 text-error-500 flex-shrink-0 mt-0.5" />
                          <div>
                            {stepIssues.map((issue, idx) => (
                              <p
                                key={idx}
                                style={{
                                  fontSize: "0.8125rem",
                                  color: "#FF4D4D",
                                }}
                              >
                                {issue}
                              </p>
                            ))}
                          </div>
                        </div>
                      )}
                    </div>
                  </Card>
  );
}
