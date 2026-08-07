import { useState, useEffect, useMemo, useId } from "react";
import { useParams, useNavigate, Link } from "react-router";
import { useForm, Controller } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { toast } from "@/shared/utils/toast";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Textarea } from "@/shared/components/ui/textarea";
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
  Users,
  UserCheck,
  Calendar,
  Zap,
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
import { useUsers } from "@/features/users/hooks/use-users";
import { useTeams } from "@/features/teams/hooks/use-teams";
import { useSchedules } from "@/features/schedules/hooks/use-schedules";
import { t } from "@/shared/locales/i18n";
import { useLocaleTick } from "@/shared/hooks/use-locale-tick";
import { policyFormSchema, type PolicyFormValues, type PolicyFormData } from "../utils/policy-form-schema";
import { stepPayload, stepErrorMessages, apiStepToLocal, type LocalStep } from "../utils/step-mapping";
import { describeTarget } from "../utils/describe-target";
import { StepCard } from "./step-card";
import { PolicyTimeline } from "./policy-timeline";

export function EscalationDetail() {
  const { id } = useParams();
  const navigate = useNavigate();
  const isNew = id === "new";
  const formId = useId();

  const { data: policy, isLoading, error } = useEscalationPolicy(isNew ? "" : id!);
  const createPolicyMutation = useCreatePolicy();
  const updatePolicyMutation = useUpdatePolicy();
  const deletePolicyMutation = useDeletePolicy();
  const addStepMutation = useAddStep();
  const updateStepMutation = useUpdateStep();
  const removeStepMutation = useRemoveStep();
  const reorderStepsMutation = useReorderSteps();

  const {
    register,
    control,
    handleSubmit,
    reset,
    setValue,
    watch,
    formState: { errors, isSubmitted },
  } = useForm<PolicyFormValues, unknown, PolicyFormData>({
    resolver: zodResolver(policyFormSchema),
    defaultValues: {
      name: "",
      description: "",
      teamId: "",
      isActive: true,
      exhaustionBehavior: "Stop",
      maxRepeatCycles: 3,
      maxRepeatDurationMinutes: 240,
      steps: [],
    },
  });

  const policyName = watch("name");
  const [steps, setSteps] = useState<LocalStep[]>([]);
  /** Persisted steps the user removed locally. Deleted server-side on save, not before. */
  const [removedStepIds, setRemovedStepIds] = useState<string[]>([]);
  const [orderDirty, setOrderDirty] = useState(false);
  /** Set once the policy exists server-side, so a retry after a failed save updates it
   *  instead of creating a second policy. */
  const [createdPolicyId, setCreatedPolicyId] = useState<string | null>(null);
  const [isSaving, setIsSaving] = useState(false);
  const [isDeleteModalOpen, setIsDeleteModalOpen] = useState(false);
  const [draggedStepId, setDraggedStepId] = useState<string | null>(null);

  const i18nTick = useLocaleTick();

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
    if (!policy) return;

    const loaded = (policy.steps ?? []).map(apiStepToLocal);
    setSteps(loaded);
    reset({
      name: policy.name,
      description: policy.description ?? "",
      teamId: policy.teamId ?? "",
      isActive: policy.isActive ?? true,
      exhaustionBehavior: policy.exhaustionBehavior ?? "Stop",
      maxRepeatCycles: policy.maxRepeatCycles ?? 3,
      maxRepeatDurationMinutes: policy.maxRepeatDurationMinutes ?? 240,
      steps: loaded.map(stepPayload),
    });
  }, [policy, reset]);

  // `steps` stays local state because the card owns drag-and-drop and the target-type toggle;
  // mirror it into the form so the schema is what decides whether the policy can be saved.
  useEffect(() => {
    setValue("steps", steps.map(stepPayload), {
      shouldValidate: steps.length > 0 || isSubmitted,
    });
  }, [steps, setValue, isSubmitted]);

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

  // Removal is local until save, so leaving the page without saving keeps the step.
  const removeStep = (stepId: string) => {
    const step = steps.find((s) => s.id === stepId);
    if (step && !step.isNew && !stepId.startsWith("temp-")) {
      setRemovedStepIds((prev) => (prev.includes(stepId) ? prev : [...prev, stepId]));
    }
    setSteps(
      steps
        .filter((s) => s.id !== stepId)
        .map((s, i) => ({ ...s, level: i + 1 }))
    );
    setOrderDirty(true);
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

  /** Clears targetValue in the same write as the type, so a schedule id can never sit under a team
   * target; targetUserIds is kept so switching away and back does not lose the selection. */
  const changeStepTargetType = (stepId: string, targetType: LocalStep["targetType"]) => {
    setSteps((prev) =>
      prev.map((s) => (s.id === stepId ? { ...s, targetType, targetValue: "" } : s))
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

  // The reorder endpoint requires the complete step set, which only exists once the pending
  // adds and removals have been flushed — so it runs at save time, not on drop.
  const handleDragEnd = () => {
    if (draggedStepId) setOrderDirty(true);
    setDraggedStepId(null);
  };

  const { data: apiUsers = [] } = useUsers();
  const { data: apiTeams = [] } = useTeams();
  const { data: apiSchedules = [] } = useSchedules();

  const describeStepTarget = (step: LocalStep): string =>
    describeTarget(step, { users: apiUsers, teams: apiTeams, schedules: apiSchedules });

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

  // There is no atomic create-with-steps endpoint, so a failure mid-way leaves the policy
  // behind. Each successful call is recorded in local state (policy id, per-step id) so a
  // retry resumes instead of creating a duplicate policy or duplicate steps.
  const handleSave = async (data: PolicyFormData) => {
    setIsSaving(true);
    try {
      const existingPolicyId = isNew ? createdPolicyId : id;
      let policyId: string;

      if (existingPolicyId) {
        policyId = existingPolicyId;
        await updatePolicyMutation.mutateAsync({
          id: policyId,
          name: data.name,
          description: data.description || undefined,
          teamId: data.teamId,
          exhaustionBehavior: data.exhaustionBehavior,
          maxRepeatCycles: data.maxRepeatCycles,
          maxRepeatDurationMinutes: data.maxRepeatDurationMinutes,
        });
      } else {
        const result = await createPolicyMutation.mutateAsync({
          name: data.name,
          description: data.description || undefined,
          teamId: data.teamId,
          exhaustionBehavior: data.exhaustionBehavior,
          maxRepeatCycles: data.maxRepeatCycles,
          maxRepeatDurationMinutes: data.maxRepeatDurationMinutes,
        });
        if (!result) return;
        policyId = result.id;
        setCreatedPolicyId(policyId);
      }

      // Removals first, so the reorder call below sees the final step set.
      for (const stepId of [...removedStepIds]) {
        await removeStepMutation.mutateAsync({ policyId, stepId });
        setRemovedStepIds((prev) => prev.filter((s) => s !== stepId));
      }

      // A background refetch can put a locally removed step back into `steps`; never write it.
      const activeSteps = steps.filter((s) => !removedStepIds.includes(s.id));
      const addedCount = activeSteps.filter((s) => s.isNew).length;
      const orderedStepIds: string[] = [];

      for (const step of activeSteps) {
        if (step.isNew) {
          const created = await addStepMutation.mutateAsync({ policyId, ...stepPayload(step) });
          const persistedId = created?.id ?? step.id;
          orderedStepIds.push(persistedId);
          setSteps((prev) =>
            prev.map((s) => (s.id === step.id ? { ...s, id: persistedId, isNew: false } : s))
          );
        } else {
          await updateStepMutation.mutateAsync({
            policyId,
            stepId: step.id,
            ...stepPayload(step),
          });
          orderedStepIds.push(step.id);
        }
      }

      const setChanged = addedCount > 0 || removedStepIds.length > 0;
      if (orderedStepIds.length > 1 && (orderDirty || setChanged)) {
        await reorderStepsMutation.mutateAsync({ policyId, stepIds: orderedStepIds });
      }
      setOrderDirty(false);

      navigate("/escalations");
    } catch {
      // The failing mutation already surfaced a toast. Stay on the page: the state written
      // above lets the user retry without duplicating anything.
    } finally {
      setIsSaving(false);
    }
  };

  const onSubmit = handleSubmit(handleSave, (formErrors) => {
    const stepMessages = Array.isArray(formErrors.steps)
      ? formErrors.steps.flatMap(stepErrorMessages)
      : [];
    const first =
      formErrors.name?.message ??
      formErrors.teamId?.message ??
      formErrors.description?.message ??
      formErrors.steps?.message ??
      stepMessages[0];

    toast.error(t("toast.validationError"), first);
  });

  const handleDelete = async () => {
    if (!id) return;
    deletePolicyMutation.mutate(id, {
      onSuccess: () => {
        setIsDeleteModalOpen(false);
        navigate("/escalations");
      },
    });
  };

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
            onClick={onSubmit}
            disabled={isSaving}
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
                  {...register("name")}
                  error={errors.name?.message}
                  className="bg-input-background"
                />
                {errors.name && (
                  <p style={{ fontSize: "0.8125rem", color: "#FF4D4D", marginTop: "0.375rem" }}>
                    {errors.name.message}
                  </p>
                )}
              </div>
              <div>
                <label
                  htmlFor={`${formId}-team`}
                  style={{
                    fontSize: "0.875rem",
                    fontWeight: 600,
                    marginBottom: "0.5rem",
                    display: "block",
                  }}
                >
                  {t("escalations.ownerTeamLabel")} <span className="text-error-500">*</span>
                </label>
                <Controller
                  control={control}
                  name="teamId"
                  render={({ field }) => (
                    <Select value={field.value ?? ""} onValueChange={field.onChange}>
                      <SelectTrigger id={`${formId}-team`} className="bg-input-background">
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
                  )}
                />
                {errors.teamId && (
                  <p style={{ fontSize: "0.8125rem", color: "#FF4D4D", marginTop: "0.375rem" }}>
                    {errors.teamId.message}
                  </p>
                )}
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
                  {...register("description")}
                  rows={3}
                  className="bg-input-background resize-none"
                />
                {errors.description && (
                  <p style={{ fontSize: "0.8125rem", color: "#FF4D4D", marginTop: "0.375rem" }}>
                    {errors.description.message}
                  </p>
                )}
              </div>
              <div>
                <label
                  htmlFor={`${formId}-exhaustion`}
                  style={{
                    fontSize: "0.875rem",
                    fontWeight: 600,
                    marginBottom: "0.5rem",
                    display: "block",
                  }}
                >
                  {t("escalations.exhaustionBehaviorLabel")}
                </label>
                <Controller
                  control={control}
                  name="exhaustionBehavior"
                  render={({ field }) => (
                    <Select value={field.value} onValueChange={field.onChange}>
                      <SelectTrigger id={`${formId}-exhaustion`} className="bg-input-background">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="Stop">{t("escalations.exhaustionStop")}</SelectItem>
                        <SelectItem value="Repeat">{t("escalations.exhaustionRepeat")}</SelectItem>
                      </SelectContent>
                    </Select>
                  )}
                />
                <p style={{ fontSize: "0.8125rem", color: "#94A3B8", marginTop: "0.375rem" }}>
                  {t("escalations.exhaustionBehaviorHint")}
                </p>
              </div>
              {watch("exhaustionBehavior") === "Repeat" && (
                <>
                  <div>
                    <label
                      htmlFor={`${formId}-max-cycles`}
                      style={{
                        fontSize: "0.875rem",
                        fontWeight: 600,
                        marginBottom: "0.5rem",
                        display: "block",
                      }}
                    >
                      {t("escalations.maxRepeatCyclesLabel")}
                    </label>
                    <Input
                      id={`${formId}-max-cycles`}
                      type="number"
                      min={1}
                      max={10}
                      {...register("maxRepeatCycles", { valueAsNumber: true })}
                      error={errors.maxRepeatCycles?.message}
                      className="bg-input-background"
                    />
                  </div>
                  <div>
                    <label
                      htmlFor={`${formId}-max-duration`}
                      style={{
                        fontSize: "0.875rem",
                        fontWeight: 600,
                        marginBottom: "0.5rem",
                        display: "block",
                      }}
                    >
                      {t("escalations.maxRepeatDurationLabel")}
                    </label>
                    <Input
                      id={`${formId}-max-duration`}
                      type="number"
                      min={60}
                      max={1440}
                      {...register("maxRepeatDurationMinutes", { valueAsNumber: true })}
                      error={errors.maxRepeatDurationMinutes?.message}
                      className="bg-input-background"
                    />
                    <p style={{ fontSize: "0.8125rem", color: "#94A3B8", marginTop: "0.375rem" }}>
                      {t("escalations.maxRepeatDurationHint")}
                    </p>
                  </div>
                </>
              )}
            </div>
          </Card>

          <PolicyTimeline steps={steps} describeTarget={describeStepTarget} />

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
        </div>

        <section className="lg:col-span-2 space-y-6" aria-labelledby={`${formId}-steps`}>
          <div className="flex items-center justify-between">
            <div>
              <h3 id={`${formId}-steps`} style={{ fontSize: "1.125rem", fontWeight: 600 }}>
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

          {errors.steps?.message && (
            <div className="p-3 rounded-lg bg-error-500/10 border border-error-500/20 flex items-start gap-2">
              <AlertCircle className="w-4 h-4 text-error-500 flex-shrink-0 mt-0.5" />
              <p style={{ fontSize: "0.8125rem", color: "#FF4D4D" }}>{errors.steps.message}</p>
            </div>
          )}

          {steps.length > 0 ? (
            <div className="space-y-4">
              {steps.map((step, index) => (
                <StepCard
                  key={step.id}
                  step={step}
                  index={index}
                  formId={formId}
                  stepIssues={stepErrorMessages(
                    Array.isArray(errors.steps) ? errors.steps[index] : undefined
                  )}
                  targetTypeOptions={targetTypeOptions}
                  getTargetOptions={getTargetOptions}
                  draggedStepId={draggedStepId}
                  onDragStart={handleDragStart}
                  onDragOver={handleDragOver}
                  onDragEnd={handleDragEnd}
                  onUpdate={updateLocalStep}
                  onChangeTargetType={changeStepTargetType}
                  onRemove={removeStep}
                />
              ))}
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
                {t("escalations.noStepsTitle")}
              </p>
              <p
                style={{
                  fontSize: "0.875rem",
                  color: "#94A3B8",
                  marginBottom: "1.5rem",
                }}
              >
                {t("escalations.noStepsBody")}
              </p>
              <Button onClick={addStep} className="bg-brand-500 hover:bg-brand-600">
                <Plus className="w-4 h-4 mr-2" />
                {t("escalations.addFirstStep")}
              </Button>
            </Card>
          )}
        </section>
      </div>

      <Dialog open={isDeleteModalOpen} onOpenChange={setIsDeleteModalOpen}>
        <DialogContent className="bg-card border-border sm:max-w-[500px]">
          <DialogHeader>
            <DialogTitle style={{ fontSize: "1.5rem", fontWeight: 600 }}>
              {t("escalations.deletePolicyTitle")}
            </DialogTitle>
            <DialogDescription style={{ fontSize: "0.875rem", color: "#94A3B8" }}>
              {t("escalations.deletePolicyDesc")}
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
    </div>
  );
}