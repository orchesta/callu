import { useState, useEffect, useMemo } from "react";
import { useParams, useNavigate, Link } from "react-router";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Textarea } from "@/shared/components/ui/textarea";
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
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from "@/shared/components/ui/dialog";
import {
  ChevronRight,
  Home,
  Save,
  Trash2,
  Plus,
  GripVertical,
  Clock,
  Users,
  UserCheck,
  Calendar,
  Zap,
  Play,
  AlertCircle,
  Info,
  Loader2,
} from "lucide-react";
import {
  useEscalationPolicy,
  useCreatePolicy,
  useUpdatePolicy,
  useDeletePolicy,
  useAddStep,
  useUpdateStep,
  useRemoveStep,
  useReorderSteps,
} from "../hooks/use-escalations";
import type { EscalationStepDto } from "../types/escalation.types";
import { useUsers } from "@/features/users/hooks/use-users";
import { useTeams } from "@/features/teams/hooks/use-teams";
import { useSchedules } from "@/features/schedules/hooks/use-schedules";
import { onLocaleChange, t } from "@/shared/locales/i18n";

interface LocalStep {
  id: string;
  level: number;
  delayMinutes: number;
  title: string;
  description: string;
  targetType: "schedule" | "team" | "user";
  /** Single target id (schedule, team, or first user). Use `targetUserIds` when targetType==="user". */
  targetValue: string;
  /** Multi-user list when targetType==="user". Empty for schedule/team targets. */
  targetUserIds: string[];
  /** Team-target only: notify every team member, not just on-call. */
  notifyAll: boolean;
  /** Schedule-target only: page both primary and secondary on-call. */
  notifyBothOnCall: boolean;
  isNew?: boolean;
}

function apiStepToLocal(step: EscalationStepDto): LocalStep {
  let targetType: "schedule" | "team" | "user" = "schedule";
  let targetValue = "";
  let targetUserIds: string[] = [];

  if (step.teamId) {
    targetType = "team";
    targetValue = step.teamId;
  } else if (step.notifyUserIds && step.notifyUserIds.length > 0) {
    targetType = "user";
    targetValue = step.notifyUserIds[0];
    targetUserIds = [...step.notifyUserIds];
  } else if (step.scheduleId) {
    targetType = "schedule";
    targetValue = step.scheduleId;
  }

  return {
    id: step.id,
    level: step.level,
    delayMinutes: step.delayMinutes,
    title: step.title,
    description: step.description ?? "",
    targetType,
    targetValue,
    targetUserIds,
    notifyAll: step.notifyAllTeamMembers ?? false,
    notifyBothOnCall: step.notifyBothOnCall ?? false,
  };
}

export function EscalationDetail() {
  const { id } = useParams();
  const navigate = useNavigate();
  const isNew = id === "new";

  const { data: policy, isLoading, error } = useEscalationPolicy(isNew ? "" : id!);
  const createPolicyMutation = useCreatePolicy();
  const updatePolicyMutation = useUpdatePolicy();
  const deletePolicyMutation = useDeletePolicy();
  const addStepMutation = useAddStep();
  const updateStepMutation = useUpdateStep();
  const removeStepMutation = useRemoveStep();
  const reorderStepsMutation = useReorderSteps();

  const [policyName, setPolicyName] = useState("");
  const [description, setDescription] = useState("");
  const [teamId, setTeamId] = useState<string>("");
  const [steps, setSteps] = useState<LocalStep[]>([]);
  const [isSaving, setIsSaving] = useState(false);
  const [isDeleteModalOpen, setIsDeleteModalOpen] = useState(false);
  const [isSimulationOpen, setIsSimulationOpen] = useState(false);
  const [draggedStepId, setDraggedStepId] = useState<string | null>(null);

  const [i18nTick, setI18nTick] = useState(0);
  useEffect(() => onLocaleChange(() => setI18nTick((n) => n + 1)), []);

  const targetTypeOptions = useMemo(
    () => [
      { value: "schedule" as const, label: t("escalations.targetTypeSchedule"), icon: Calendar },
      { value: "team" as const, label: t("escalations.team"), icon: Users },
      { value: "user" as const, label: t("escalations.targetTypeUser"), icon: UserCheck },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [i18nTick],
  );

  useEffect(() => {
    if (policy) {
      setPolicyName(policy.name);
      setDescription(policy.description ?? "");
      setTeamId(policy.teamId ?? "");
      setSteps((policy.steps ?? []).map(apiStepToLocal));
    }
  }, [policy]);

  const addStep = () => {
    const newStep: LocalStep = {
      id: `temp-${Date.now()}`,
      level: steps.length + 1,
      delayMinutes: steps.length === 0 ? 0 : 5,
      title: t("escalations.level", { level: String(steps.length + 1) }),
      description: "",
      targetType: "schedule",
      targetValue: "",
      targetUserIds: [],
      notifyAll: false,
      notifyBothOnCall: false,
      isNew: true,
    };
    setSteps([...steps, newStep]);
  };

  const removeStep = async (stepId: string) => {
    if (stepId.startsWith("temp-")) {
      const newSteps = steps
        .filter((s) => s.id !== stepId)
        .map((step, i) => ({ ...step, level: i + 1 }));
      setSteps(newSteps);
      return;
    }
    if (!isNew && id) {
      removeStepMutation.mutate({ policyId: id, stepId });
    }
    const newSteps = steps
      .filter((s) => s.id !== stepId)
      .map((step, i) => ({ ...step, level: i + 1 }));
    setSteps(newSteps);
  };

  const updateLocalStep = (
    stepId: string,
    field: keyof LocalStep,
    value: LocalStep[keyof LocalStep]
  ) => {
    setSteps(
      steps.map((step) =>
        step.id === stepId ? { ...step, [field]: value } : step
      )
    );
  };

  const handleDragStart = (stepId: string) => {
    setDraggedStepId(stepId);
  };

  const handleDragOver = (e: React.DragEvent, targetStepId: string) => {
    e.preventDefault();
    if (!draggedStepId || draggedStepId === targetStepId) return;

    const draggedIndex = steps.findIndex((s) => s.id === draggedStepId);
    const targetIndex = steps.findIndex((s) => s.id === targetStepId);

    const newSteps = [...steps];
    const [draggedStep] = newSteps.splice(draggedIndex, 1);
    newSteps.splice(targetIndex, 0, draggedStep);

    const reorderedSteps = newSteps.map((step, index) => ({
      ...step,
      level: index + 1,
    }));

    setSteps(reorderedSteps);
  };

  const handleDragEnd = () => {
    setDraggedStepId(null);
    if (!isNew && id) {
      const realStepIds = steps
        .filter((s) => !s.id.startsWith("temp-"))
        .map((s) => s.id);
      if (realStepIds.length > 0) {
        reorderStepsMutation.mutate({ policyId: id, stepIds: realStepIds });
      }
    }
  };

  const { data: apiUsers = [] } = useUsers();
  const { data: apiTeams = [] } = useTeams();
  const { data: apiSchedules = [] } = useSchedules();

  const getTargetOptions = (type: string): { value: string; label: string }[] => {
    switch (type) {
      case "schedule":
        return apiSchedules.map(s => ({ value: s.id, label: s.name }));
      case "team":
        return apiTeams.map((team) => ({ value: team.id, label: team.name }));
      case "user":
        return apiUsers.map(u => ({ value: u.id, label: u.displayName || u.email }));
      default:
        return [];
    }
  };

  const validateStep = (step: LocalStep) => {
    const errors = [];
    if (!step.targetValue) {
      errors.push(t("escalations.validationNoTarget"));
    }
    if (step.level === 1 && step.delayMinutes > 0) {
      errors.push(t("escalations.validationFirstStepDelay"));
    }
    return errors;
  };

  const hasValidationErrors = () => {
    return steps.some((step) => validateStep(step).length > 0);
  };

  const handleSave = async () => {
    setIsSaving(true);
    try {
      if (isNew) {
        const result = await createPolicyMutation.mutateAsync({
          name: policyName,
          description: description || undefined,
          teamId: teamId ? teamId : undefined,
        });
        if (result && steps.length > 0) {
          for (const step of steps) {
            await addStepMutation.mutateAsync({
              policyId: result.id,
              title: step.title,
              description: step.description || undefined,
              delayMinutes: step.delayMinutes,
              scheduleId: step.targetType === "schedule" ? step.targetValue : null,
              teamId: step.targetType === "team" ? step.targetValue : null,
              notifyUserIds: step.targetType === "user"
                ? (step.targetUserIds.length > 0 ? step.targetUserIds : [step.targetValue])
                : [],
              notifyAllTeamMembers: step.notifyAll,
              notifyBothOnCall: step.notifyBothOnCall,
            });
          }
        }
      } else if (id) {
        await updatePolicyMutation.mutateAsync({
          id,
          name: policyName,
          description: description || undefined,
          teamId: teamId ? teamId : undefined,
        });
        for (const step of steps.filter((s) => s.isNew)) {
          await addStepMutation.mutateAsync({
            policyId: id,
            title: step.title,
            description: step.description || undefined,
            delayMinutes: step.delayMinutes,
            scheduleId: step.targetType === "schedule" ? step.targetValue : null,
            teamId: step.targetType === "team" ? step.targetValue : null,
            notifyUserIds: step.targetType === "user"
              ? (step.targetUserIds.length > 0 ? step.targetUserIds : [step.targetValue])
              : [],
            notifyAllTeamMembers: step.notifyAll,
            notifyBothOnCall: step.notifyBothOnCall,
          });
        }
        for (const step of steps.filter((s) => !s.isNew)) {
          await updateStepMutation.mutateAsync({
            policyId: id,
            stepId: step.id,
            title: step.title,
            description: step.description || undefined,
            delayMinutes: step.delayMinutes,
            scheduleId: step.targetType === "schedule" ? step.targetValue : null,
            teamId: step.targetType === "team" ? step.targetValue : null,
            notifyUserIds: step.targetType === "user"
              ? (step.targetUserIds.length > 0 ? step.targetUserIds : [step.targetValue])
              : [],
            notifyAllTeamMembers: step.notifyAll,
            notifyBothOnCall: step.notifyBothOnCall,
          });
        }
      }
      navigate("/escalations");
    } finally {
      setIsSaving(false);
    }
  };

  const handleDelete = async () => {
    if (!id) return;
    deletePolicyMutation.mutate(id, {
      onSuccess: () => {
        setIsDeleteModalOpen(false);
        navigate("/escalations");
      },
    });
  };

  const isValid =
    policyName.trim().length > 0 && teamId.trim().length > 0 && steps.length > 0 && !hasValidationErrors();

  if (!isNew && isLoading) {
    return (
      <div className="p-6 flex items-center justify-center min-h-[60vh]">
        <div className="text-center">
          <Loader2 className="w-8 h-8 animate-spin text-brand-500 mx-auto mb-3" />
          <p style={{ fontSize: "0.875rem", color: "#94A3B8" }}>
            Loading escalation policy...
          </p>
        </div>
      </div>
    );
  }

  if (!isNew && error) {
    return (
      <div className="p-6 flex items-center justify-center min-h-[60vh]">
        <div className="text-center">
          <AlertCircle className="w-8 h-8 text-error-500 mx-auto mb-3" />
          <p
            style={{
              fontSize: "1.125rem",
              fontWeight: 600,
              marginBottom: "0.5rem",
            }}
          >
            {t("escalations.loadPolicyFailed")}
          </p>
          <p style={{ fontSize: "0.875rem", color: "#94A3B8" }}>
            {error instanceof Error ? error.message : t("common.errorOccurred")}
          </p>
          <Button
            variant="outline"
            onClick={() => navigate("/escalations")}
            className="mt-4"
          >
            {t("escalations.backToPolicies")}
          </Button>
        </div>
      </div>
    );
  }

  return (
    <div className="p-6 space-y-6">
      <nav className="flex items-center gap-2 text-sm">
        <Link
          to="/dashboard"
          className="text-muted-foreground hover:text-foreground transition-colors"
        >
          <Home className="w-4 h-4" />
        </Link>
        <ChevronRight className="w-4 h-4 text-muted-foreground" />
        <Link
          to="/escalations"
          className="text-muted-foreground hover:text-foreground transition-colors"
        >
          {t("escalations.title")}
        </Link>
        <ChevronRight className="w-4 h-4 text-muted-foreground" />
        <span className="text-foreground font-medium">
          {isNew ? t("escalations.breadcrumbNewPolicy") : policyName}
        </span>
      </nav>

      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
        <div>
          <h1 style={{ fontSize: "1.875rem", fontWeight: 600 }}>
            {isNew ? t("escalations.pageTitleCreate") : t("escalations.pageTitleEdit")}
          </h1>
          <p
            style={{
              fontSize: "0.875rem",
              color: "#94A3B8",
              marginTop: "0.25rem",
            }}
          >
            {t("escalations.pageSubtitle")}
          </p>
        </div>
        <div className="flex gap-2">
          {!isNew && (
            <Button
              variant="outline"
              onClick={() => setIsDeleteModalOpen(true)}
              className="bg-input-background hover:bg-error-500/10 hover:text-error-500"
            >
              <Trash2 className="w-4 h-4 mr-2" />
              {t("common.delete")}
            </Button>
          )}
          <Button
            onClick={handleSave}
            disabled={!isValid || isSaving}
            className="bg-brand-500 hover:bg-brand-600 text-white"
          >
            {isSaving ? (
              <>
                <div className="w-4 h-4 border-2 border-white/30 border-t-white rounded-full animate-spin mr-2" />
                {t("escalations.saving")}
              </>
            ) : (
              <>
                <Save className="w-4 h-4 mr-2" />
                {t("escalations.savePolicy")}
              </>
            )}
          </Button>
        </div>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        <div className="lg:col-span-1 space-y-6">
          <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
            <h3
              style={{
                fontSize: "1.125rem",
                fontWeight: 600,
                marginBottom: "1rem",
              }}
            >
              {t("escalations.policyInformation")}
            </h3>
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
                  {t("escalations.policyNameLabel")} <span className="text-error-500">*</span>
                </label>
                <Input
                  placeholder={t("escalations.detailPolicyNamePlaceholder")}
                  value={policyName}
                  onChange={(e) => setPolicyName(e.target.value)}
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
                  {t("escalations.ownerTeamLabel")} <span className="text-error-500">*</span>
                </label>
                <Select value={teamId} onValueChange={setTeamId}>
                  <SelectTrigger className="bg-input-background">
                    <SelectValue placeholder={t("escalations.selectTeamForPolicyPlaceholder")} />
                  </SelectTrigger>
                  <SelectContent>
                    {apiTeams.map((team) => (
                      <SelectItem key={team.id} value={team.id}>
                        {team.name}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
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
                  {t("common.description")}
                </label>
                <Textarea
                  placeholder={t("escalations.detailPolicyDescPlaceholder")}
                  value={description}
                  onChange={(e) => setDescription(e.target.value)}
                  rows={3}
                  className="bg-input-background resize-none"
                />
              </div>
            </div>
          </Card>

          <Card className="p-6 bg-gradient-to-br from-brand-500/5 to-transparent border-brand-500/20">
            <div className="flex items-start gap-3">
              <Info className="w-5 h-5 text-brand-500 flex-shrink-0 mt-0.5" />
              <div>
                <h4
                  style={{
                    fontSize: "0.9375rem",
                    fontWeight: 600,
                    marginBottom: "0.5rem",
                  }}
                >
                  {t("escalations.howItWorksTitle")}
                </h4>
                <ul
                  className="space-y-2"
                  style={{ fontSize: "0.8125rem", color: "#94A3B8" }}
                >
                  <li className="flex gap-2">
                    <span className="text-brand-500">•</span>
                    <span>{t("escalations.howItWorksL1")}</span>
                  </li>
                  <li className="flex gap-2">
                    <span className="text-brand-500">•</span>
                    <span>{t("escalations.howItWorksL2")}</span>
                  </li>
                  <li className="flex gap-2">
                    <span className="text-brand-500">•</span>
                    <span>{t("escalations.howItWorksL3")}</span>
                  </li>
                  <li className="flex gap-2">
                    <span className="text-brand-500">•</span>
                    <span>{t("escalations.howItWorksL4")}</span>
                  </li>
                </ul>
              </div>
            </div>
          </Card>

          <Button
            variant="outline"
            onClick={() => setIsSimulationOpen(true)}
            disabled={steps.length === 0}
            className="w-full bg-input-background"
          >
            <Play className="w-4 h-4 mr-2" />
            {t("escalations.runSimulation")}
          </Button>
        </div>

        <div className="lg:col-span-2 space-y-6">
          <div className="flex items-center justify-between">
            <div>
              <h3 style={{ fontSize: "1.125rem", fontWeight: 600 }}>
                {t("escalations.stepsSectionTitle")}
              </h3>
              <p
                style={{
                  fontSize: "0.875rem",
                  color: "#94A3B8",
                  marginTop: "0.25rem",
                }}
              >
                {steps.length === 0
                  ? t("escalations.stepsEmptySubtitle")
                  : steps.length === 1
                    ? t("escalations.stepsLevelsOne")
                    : t("escalations.stepsLevelsMany", { count: steps.length })}
              </p>
            </div>
            <Button
              onClick={addStep}
              variant="outline"
              className="bg-input-background"
            >
              <Plus className="w-4 h-4 mr-2" />
              {t("escalations.addStep")}
            </Button>
          </div>

          {steps.length > 0 ? (
            <div className="space-y-4">
              {steps.map((step, index) => {
                const errors = validateStep(step);
                const hasErrors = errors.length > 0;

                return (
                  <Card
                    key={step.id}
                    draggable
                    onDragStart={() => handleDragStart(step.id)}
                    onDragOver={(e) => handleDragOver(e, step.id)}
                    onDragEnd={handleDragEnd}
                    className={`p-5 bg-card/80 backdrop-blur-sm transition-all cursor-move ${hasErrors
                      ? "border-error-500/50 shadow-lg shadow-error-500/10"
                      : "border-border hover:border-border-light"
                      } ${draggedStepId === step.id ? "opacity-50" : ""}`}
                  >
                    <div className="flex items-center gap-3 mb-4">
                      <GripVertical className="w-5 h-5 text-muted-foreground cursor-grab active:cursor-grabbing" />
                      <Badge className="bg-brand-500/10 text-brand-500 border-brand-500/20 border">
                        Level {step.level}
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
                        onClick={() => removeStep(step.id)}
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
                            updateLocalStep(step.id, "title", e.target.value)
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
                              updateLocalStep(
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
                          style={{
                            fontSize: "0.875rem",
                            fontWeight: 600,
                            marginBottom: "0.5rem",
                            display: "block",
                          }}
                        >
                          Notify
                        </label>
                        <Select
                          value={step.targetType}
                          onValueChange={(value: string) => {
                            setSteps((prev) =>
                              prev.map((s) =>
                                s.id === step.id
                                  ? {
                                      ...s,
                                      targetType: value as LocalStep["targetType"],
                                      targetValue: "",
                                    }
                                  : s
                              )
                            );
                          }}
                        >
                          <SelectTrigger className="bg-input-background">
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
                          style={{
                            fontSize: "0.875rem",
                            fontWeight: 600,
                            marginBottom: "0.5rem",
                            display: "block",
                          }}
                        >
                          {step.targetType === "schedule"
                            ? "Schedule"
                            : step.targetType === "team"
                              ? "Team"
                              : "Users"}
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
                                      updateLocalStep(step.id, "targetUserIds", next);
                                      updateLocalStep(step.id, "targetValue", next[0] ?? "");
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
                              updateLocalStep(step.id, "targetValue", value)
                            }
                          >
                            <SelectTrigger className="bg-input-background">
                              <SelectValue
                                placeholder={`Select a ${step.targetType}...`}
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
                              updateLocalStep(
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
                            Notify all team members
                            <span
                              style={{
                                fontSize: "0.75rem",
                                color: "#94A3B8",
                                display: "block",
                                marginTop: "0.125rem",
                              }}
                            >
                              If unchecked, only the on-call member is notified
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
                              updateLocalStep(
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
                            Page both primary and secondary on-call
                            <span
                              style={{
                                fontSize: "0.75rem",
                                color: "#94A3B8",
                                display: "block",
                                marginTop: "0.125rem",
                              }}
                            >
                              No-op for schedules without a secondary slot
                            </span>
                          </label>
                        </div>
                      )}

                      {hasErrors && (
                        <div className="p-3 rounded-lg bg-error-500/10 border border-error-500/20 flex items-start gap-2">
                          <AlertCircle className="w-4 h-4 text-error-500 flex-shrink-0 mt-0.5" />
                          <div>
                            {errors.map((error, idx) => (
                              <p
                                key={idx}
                                style={{
                                  fontSize: "0.8125rem",
                                  color: "#FF4D4D",
                                }}
                              >
                                {error}
                              </p>
                            ))}
                          </div>
                        </div>
                      )}
                    </div>
                  </Card>
                );
              })}
            </div>
          ) : (
            <Card className="p-12 bg-card/80 backdrop-blur-sm border-border border-dashed text-center">
              <Zap className="w-12 h-12 text-muted-foreground mx-auto mb-3 opacity-50" />
              <p
                style={{
                  fontSize: "1.125rem",
                  fontWeight: 600,
                  marginBottom: "0.5rem",
                }}
              >
                No escalation steps yet
              </p>
              <p
                style={{
                  fontSize: "0.875rem",
                  color: "#94A3B8",
                  marginBottom: "1.5rem",
                }}
              >
                Add your first escalation step to get started
              </p>
              <Button onClick={addStep} className="bg-brand-500 hover:bg-brand-600">
                <Plus className="w-4 h-4 mr-2" />
                Add First Step
              </Button>
            </Card>
          )}
        </div>
      </div>

      <Dialog open={isDeleteModalOpen} onOpenChange={setIsDeleteModalOpen}>
        <DialogContent className="bg-card border-border sm:max-w-[500px]">
          <DialogHeader>
            <DialogTitle style={{ fontSize: "1.5rem", fontWeight: 600 }}>
              {t("escalations.deletePolicyTitle")}
            </DialogTitle>
            <DialogDescription style={{ fontSize: "0.875rem", color: "#94A3B8" }}>
              Services using this policy will need to be reassigned
            </DialogDescription>
          </DialogHeader>
          <div className="py-4">
            <div className="flex gap-3 mb-4">
              <div className="w-10 h-10 rounded-full bg-error-500/10 flex items-center justify-center flex-shrink-0">
                <AlertCircle className="w-5 h-5 text-error-500" />
              </div>
              <div>
                <p style={{ fontSize: "0.875rem", marginBottom: "0.5rem" }}>
                  {t("escalations.deletePolicyMsg", { name: policyName })}
                </p>
                <p style={{ fontSize: "0.8125rem", color: "#94A3B8" }}>
                  {t("escalations.deletePolicyWarn")}
                </p>
              </div>
            </div>
          </div>
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
              disabled={deletePolicyMutation.isPending}
              className="bg-error-500 hover:bg-error-600 text-white"
            >
              <Trash2 className="w-4 h-4 mr-2" />
              {deletePolicyMutation.isPending ? t("escalations.deleting") : t("escalations.deletePolicy")}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog open={isSimulationOpen} onOpenChange={setIsSimulationOpen}>
        <DialogContent className="bg-card border-border sm:max-w-[700px]">
          <DialogHeader>
            <DialogTitle style={{ fontSize: "1.5rem", fontWeight: 600 }}>
              {t("escalations.simulationTitle")}
            </DialogTitle>
            <DialogDescription style={{ fontSize: "0.875rem", color: "#94A3B8" }}>
              {t("escalations.simulationDesc")}
            </DialogDescription>
          </DialogHeader>
          <div className="py-4 space-y-4">
            {steps.map((step, index) => (
              <div
                key={step.id}
                className="flex gap-4 items-start p-4 rounded-lg bg-surface-light/20 border border-border"
              >
                <div className="flex flex-col items-center gap-2">
                  <div className="w-8 h-8 rounded-full bg-brand-500 text-white flex items-center justify-center text-sm font-bold">
                    {step.level}
                  </div>
                  {index < steps.length - 1 && (
                    <div className="w-0.5 h-8 bg-border" />
                  )}
                </div>
                <div className="flex-1">
                  <div className="flex items-center gap-2 mb-1">
                    <p style={{ fontSize: "0.9375rem", fontWeight: 600 }}>
                      {step.title || t("escalations.level", { level: String(step.level) })}
                    </p>
                    {step.delayMinutes > 0 && (
                      <Badge className="bg-warning-500/10 text-warning-500 border-warning-500/20 border text-xs">
                        {t("escalations.afterMinutes", { minutes: String(step.delayMinutes) })}
                      </Badge>
                    )}
                  </div>
                  <p style={{ fontSize: "0.875rem", color: "#94A3B8" }}>
                    {t("escalations.notifyLabel")}{" "}
                    <span className="text-foreground font-medium">
                      {step.targetType === "user" && step.targetUserIds.length > 0
                        ? step.targetUserIds
                            .map((uid) => {
                              const u = apiUsers.find((x) => x.id === uid);
                              return u ? `${u.firstName} ${u.lastName}`.trim() || u.email : uid.slice(0, 8);
                            })
                            .join(", ")
                        : step.targetType === "team"
                          ? apiTeams.find((tm) => tm.id === step.targetValue)?.name ?? step.targetValue
                          : step.targetType === "schedule"
                            ? apiSchedules.find((s) => s.id === step.targetValue)?.name ?? step.targetValue
                            : step.targetValue || t("escalations.notConfigured")}
                    </span>
                  </p>
                  {step.targetType === "team" && step.notifyAll && (
                    <p
                      style={{
                        fontSize: "0.75rem",
                        color: "#94A3B8",
                        marginTop: "0.25rem",
                      }}
                    >
                      {t("escalations.allTeamMembers")}
                    </p>
                  )}
                  {step.targetType === "schedule" && step.notifyBothOnCall && (
                    <p
                      style={{
                        fontSize: "0.75rem",
                        color: "#94A3B8",
                        marginTop: "0.25rem",
                      }}
                    >
                      {t("escalations.pageBothOnCall")}
                    </p>
                  )}
                </div>
              </div>
            ))}
          </div>
          <DialogFooter>
            <Button
              onClick={() => setIsSimulationOpen(false)}
              className="bg-brand-500 hover:bg-brand-600 text-white"
            >
              {t("escalations.simulationClose")}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}