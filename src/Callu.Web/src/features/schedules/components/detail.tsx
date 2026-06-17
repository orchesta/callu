import { useState, useEffect, useRef, useMemo } from "react";
import { useParams, useNavigate, Link } from "react-router";
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
import { Tabs, TabsList, TabsTrigger, TabsContent } from "@/shared/components/ui/tabs";
import { TimezonePicker } from "@/shared/components/timezone-picker";
import { browserTimezone, formatForDisplay, zoneAbbreviation } from "@/shared/lib/timezone";
import {
  ChevronRight,
  Home,
  Save,
  Trash2,
  Calendar,
  Clock,
  Users,
  Plus,
  X,
  AlertCircle,
  Info,
  RotateCw,
  Phone,
  CheckCircle,
  Loader2,
  ChevronUp,
  ChevronDown,
} from "lucide-react";
import {
  useSchedule,
  useScheduleOccurrences,
  useCreateSchedule,
  useUpdateSchedule,
  useDeleteSchedule,
  useAddRotation,
  useUpdateRotation,
  useDeleteRotation,
} from "../hooks/use-schedules";
import { useUsers } from "@/features/users/hooks/use-users";
import { useTeams, useTeam } from "@/features/teams/hooks/use-teams";
import { useOrganizationSettings } from "@/features/settings/hooks/use-settings";
import { getLocale, onLocaleChange, t } from "@/shared/locales/i18n";

interface Member {
  id: string;
  name: string;
  initials: string;
  phone: string;
  email: string;
  color: string;
}

const AVATAR_COLORS = ["#3E7BFA", "#22C55E", "#FB923C", "#A855F7", "#EC4899", "#EF4444", "#14B8A6", "#F59E0B"];

function getInitials(displayName?: string, email?: string): string {
  if (displayName) {
    return displayName.split(" ").map(w => w[0]).join("").toUpperCase().slice(0, 2);
  }
  return (email ?? "?")[0].toUpperCase();
}

export function ScheduleDetail() {
  const { id } = useParams();
  const navigate = useNavigate();
  const isNew = id === "new";

  const [previewDays, setPreviewDays] = useState<14 | 30 | 90>(14);

  const { data: schedule, isLoading, error } = useSchedule(isNew ? "" : id!);
  const { data: serverOccurrences } = useScheduleOccurrences(isNew ? "" : id ?? "", previewDays);
  const createScheduleMutation = useCreateSchedule();
  const updateScheduleMutation = useUpdateSchedule();
  const deleteScheduleMutation = useDeleteSchedule();
  const addRotationMutation = useAddRotation();
  const updateRotationMutation = useUpdateRotation();
  const deleteRotationMutation = useDeleteRotation();
  const { data: apiUsers = [] } = useUsers();
  const { data: apiTeams = [] } = useTeams();
  const { data: orgSettings } = useOrganizationSettings();

  const allMembers: Member[] = apiUsers.map((u, i) => ({
    id: u.id,
    name: u.displayName || `${u.firstName ?? ""} ${u.lastName ?? ""}`.trim() || u.email,
    initials: u.initials || getInitials(u.displayName, u.email),
    phone: u.phoneNumber || "",
    email: u.email,
    color: AVATAR_COLORS[i % AVATAR_COLORS.length],
  }));

  const [activeTab, setActiveTab] = useState("config");
  const [scheduleName, setScheduleName] = useState("");
  const [description, setDescription] = useState("");
  const [teamId, setTeamId] = useState("");
  const [scheduleTimezone, setScheduleTimezone] = useState("UTC");
  const [viewerTz, setViewerTz] = useState<"schedule" | "my" | "utc">("schedule");

  const [rotationType, setRotationType] = useState<"daily" | "weekly" | "custom">("weekly");
  const [shiftStart, setShiftStart] = useState("00:00");
  const [shiftEnd, setShiftEnd] = useState("23:59");
  const [rotationInterval, setRotationInterval] = useState("7");
  const [selectedMembers, setSelectedMembers] = useState<string[]>([]);
  const [pendingRotationDeletes, setPendingRotationDeletes] = useState<Set<string>>(new Set());
  const [isSaving, setIsSaving] = useState(false);
  const [isDeleteModalOpen, setIsDeleteModalOpen] = useState(false);
  const [selectedTeamFilter, setSelectedTeamFilter] = useState<string>("all");

  const [i18nTick, setI18nTick] = useState(0);
  useEffect(() => onLocaleChange(() => setI18nTick((n) => n + 1)), []);

  useEffect(() => {
    if (isNew && scheduleTimezone === "UTC") {
      const fallback = (orgSettings as { defaultTimezone?: string } | null | undefined)?.defaultTimezone
        ?? Intl.DateTimeFormat().resolvedOptions().timeZone;
      if (fallback && fallback !== "UTC") setScheduleTimezone(fallback);
    }
  }, [isNew, orgSettings, scheduleTimezone]);

  void i18nTick;
  const dateLocale = getLocale() === "tr" ? "tr-TR" : "en-US";

  const { data: selectedTeamDetail } = useTeam(
    selectedTeamFilter !== "all" ? selectedTeamFilter : ""
  );
  const selectedTeamMemberIds = selectedTeamDetail?.members.map((m) => m.userId) ?? null;

  const initialFormRef = useRef<{
    rotationType: string;
    rotationInterval: string;
    shiftStart: string;
    shiftEnd: string;
    memberIds: string;
    scheduleName: string;
    description: string;
    teamId: string;
    scheduleTimezone: string;
  } | null>(null);

  useEffect(() => {
    if (schedule) {
      setScheduleName(schedule.name);
      setDescription(schedule.description ?? "");
      setTeamId(schedule.teamId ?? "");
      setSelectedTeamFilter(schedule.teamId ?? "all");
      setScheduleTimezone(schedule.timezone ?? "UTC");
      const sortedRotations = [...schedule.rotations].sort((a, b) => a.order - b.order);
      const memberIds = sortedRotations.map((r) => r.userId);
      setSelectedMembers([...new Set(memberIds)]);

      if (sortedRotations.length > 0) {
        const firstRotation = sortedRotations[0];
        const handover = firstRotation.handoverStartLocal ? new Date(firstRotation.handoverStartLocal) : new Date();
        const shiftLen = firstRotation.shiftLengthMinutes ?? 1440;
        const pad = (n: number) => n.toString().padStart(2, "0");

        if (shiftLen >= 24 * 60) {
          setShiftStart("00:00");
          setShiftEnd("23:59");
        } else {
          const startH = handover.getHours();
          const startM = handover.getMinutes();
          setShiftStart(`${pad(startH)}:${pad(startM)}`);
          const totalEnd = startH * 60 + startM + shiftLen;
          const endH = Math.floor(totalEnd / 60) % 24;
          const endM = totalEnd % 60;
          setShiftEnd(`${pad(endH)}:${pad(endM)}`);
        }

        const n = Math.max(sortedRotations.length, 1);
        let derivedType: "daily" | "weekly" | "custom";
        let derivedInterval: string;
        if (firstRotation.recurrenceIntervalDays != null) {
          const daysPerMember = Math.max(1, Math.round(firstRotation.recurrenceIntervalDays / n));
          if (daysPerMember === 1) {
            derivedType = "daily";
            derivedInterval = "1";
          } else if (daysPerMember === 7) {
            derivedType = "weekly";
            derivedInterval = "7";
          } else {
            derivedType = "custom";
            derivedInterval = String(daysPerMember);
          }
        } else {
          const recType = firstRotation.recurrenceType ?? "Weekly";
          if (recType === "Daily") {
            derivedType = "daily";
            derivedInterval = "1";
          } else if (recType === "Weekly" || recType === "None") {
            derivedType = "weekly";
            derivedInterval = "7";
          } else if (recType === "Biweekly") {
            derivedType = "custom";
            derivedInterval = "14";
          } else {
            derivedType = "custom";
            derivedInterval = "30";
          }
        }
        setRotationType(derivedType);
        setRotationInterval(derivedInterval);

        initialFormRef.current = {
          rotationType: derivedType,
          rotationInterval: derivedInterval,
          shiftStart:
            (shiftLen >= 24 * 60) ? "00:00" : `${String(handover.getHours()).padStart(2, "0")}:${String(handover.getMinutes()).padStart(2, "0")}`,
          shiftEnd:
            (shiftLen >= 24 * 60) ? "23:59" : (() => {
              const startTotal = handover.getHours() * 60 + handover.getMinutes();
              const endTotal = startTotal + shiftLen;
              const eh = Math.floor(endTotal / 60) % 24;
              const em = endTotal % 60;
              return `${String(eh).padStart(2, "0")}:${String(em).padStart(2, "0")}`;
            })(),
          memberIds: memberIds.join(","),
          scheduleName: schedule.name ?? "",
          description: schedule.description ?? "",
          teamId: schedule.teamId ?? "",
          scheduleTimezone: schedule.timezone ?? "UTC",
        };
      }
    }
  }, [schedule]);

  const isFormDirty = useMemo(() => {
    const snap = initialFormRef.current;
    if (!snap) return false;
    return (
      rotationType !== snap.rotationType ||
      rotationInterval !== snap.rotationInterval ||
      shiftStart !== snap.shiftStart ||
      shiftEnd !== snap.shiftEnd ||
      selectedMembers.join(",") !== snap.memberIds ||
      scheduleName !== snap.scheduleName ||
      description !== snap.description ||
      teamId !== snap.teamId ||
      scheduleTimezone !== snap.scheduleTimezone ||
      pendingRotationDeletes.size > 0
    );
  }, [rotationType, rotationInterval, shiftStart, shiftEnd, selectedMembers,
      scheduleName, description, teamId, scheduleTimezone, pendingRotationDeletes]);

  const addMember = (memberId: string) => {
    if (!selectedMembers.includes(memberId)) {
      setSelectedMembers([...selectedMembers, memberId]);
    }
    setPendingRotationDeletes((prev) => {
      const next = new Set(prev);
      if (!schedule) return next;
      const rotation = schedule.rotations.find((r) => r.userId === memberId);
      if (rotation) next.delete(rotation.id);
      return next;
    });
  };

  const removeMember = (memberId: string) => {
    setSelectedMembers((prev) => prev.filter((mid) => mid !== memberId));
    if (!isNew && id && schedule) {
      const rotation = schedule.rotations.find((r) => r.userId === memberId);
      if (rotation) {
        setPendingRotationDeletes((prev) => new Set(prev).add(rotation.id));
      }
    }
  };

  const moveMemberUp = (index: number) => {
    if (index <= 0) return;
    const updated = [...selectedMembers];
    [updated[index - 1], updated[index]] = [updated[index], updated[index - 1]];
    setSelectedMembers(updated);
  };

  const moveMemberDown = (index: number) => {
    if (index >= selectedMembers.length - 1) return;
    const updated = [...selectedMembers];
    [updated[index], updated[index + 1]] = [updated[index + 1], updated[index]];
    setSelectedMembers(updated);
  };

  const getAvailableMembers = () => {
    return allMembers.filter((m) => {
      if (selectedMembers.includes(m.id)) return false;
      if (selectedTeamMemberIds !== null) {
        return selectedTeamMemberIds.includes(m.id);
      }
      return true;
    });
  };

  const getSelectedMemberObjects = () => {
    return selectedMembers.map((id) => allMembers.find((m) => m.id === id)).filter((m): m is Member => m !== undefined);
  };

  const handleSave = async () => {
    setIsSaving(true);
    try {
      const is247 = shiftStart === "00:00" && shiftEnd === "23:59";

      const daysPerMember =
        rotationType === "daily" ? 1 : rotationType === "weekly" ? 7 : parseInt(rotationInterval, 10) || 7;
      const n = Math.max(selectedMembers.length, 1);
      const totalCycleDays = n * daysPerMember;

      const cadenceForCycle = (days: number): "Daily" | "Weekly" | "Biweekly" | "Monthly" => {
        if (days <= 1) return "Daily";
        if (days <= 7) return "Weekly";
        if (days <= 14) return "Biweekly";
        return "Monthly";
      };
      const cadence = cadenceForCycle(totalCycleDays);
      const intervalDays = totalCycleDays;

      const buildRotationTemplate = (memberIndex: number): { handoverStartLocal: string; shiftLengthMinutes: number } => {
        const base = new Date();
        base.setHours(0, 0, 0, 0);
        base.setDate(base.getDate() + memberIndex * daysPerMember);

        if (!is247) {
          const [startH, startM] = shiftStart.split(":").map(Number);
          base.setHours(startH, startM, 0, 0);
        }

        const pad = (n: number) => String(n).padStart(2, "0");
        const handoverStartLocal =
          `${base.getFullYear()}-${pad(base.getMonth() + 1)}-${pad(base.getDate())}T` +
          `${pad(base.getHours())}:${pad(base.getMinutes())}:00`;

        let shiftLengthMinutes: number;
        if (is247) {
          shiftLengthMinutes = daysPerMember * 24 * 60;
        } else {
          const [sH, sM] = shiftStart.split(":").map(Number);
          const [eH, eM] = shiftEnd.split(":").map(Number);
          const dailyMinutes = (eH * 60 + eM) - (sH * 60 + sM);
          shiftLengthMinutes = Math.max(1, dailyMinutes);
        }

        return { handoverStartLocal, shiftLengthMinutes };
      };

      if (isNew) {
        const result = await createScheduleMutation.mutateAsync({
          name: scheduleName,
          description: description || undefined,
          teamId,
          timezone: scheduleTimezone,
        });
        if (result) {
          try {
            for (let i = 0; i < selectedMembers.length; i++) {
              const tpl = buildRotationTemplate(i);
              await addRotationMutation.mutateAsync({
                scheduleId: result.id,
                userId: selectedMembers[i],
                handoverStartLocal: tpl.handoverStartLocal,
                shiftLengthMinutes: tpl.shiftLengthMinutes,
                isPrimary: i === 0,
                order: i + 1,
                recurrenceType: cadence,
                recurrenceIntervalDays: intervalDays,
              });
            }
          } catch (rotationErr) {
            try {
              await deleteScheduleMutation.mutateAsync(result.id);
            } catch { /* empty */ }
            throw rotationErr;
          }
        }
      } else if (id) {
        await updateScheduleMutation.mutateAsync({
          id,
          name: scheduleName,
          description: description || undefined,
          timezone: scheduleTimezone,
          teamId: teamId || undefined,
        });

        for (const rotationId of pendingRotationDeletes) {
          await deleteRotationMutation.mutateAsync(rotationId);
        }
        setPendingRotationDeletes(new Set());

        if (schedule) {
          const sortedRotations = [...schedule.rotations].sort((a, b) => a.order - b.order);
          const baseline = sortedRotations[0];
          let timingChanged = false;
          if (baseline) {
            const baselineShiftLen = baseline.shiftLengthMinutes ?? 1440;
            const baselineTpl = buildRotationTemplate(0);
            const baselineInterval =
              baseline.recurrenceIntervalDays ??
              ({ Daily: 1, Weekly: 7, Biweekly: 14, Monthly: 30, None: 7 } as const)[
                baseline.recurrenceType ?? "Weekly"
              ];
            timingChanged =
              baselineTpl.shiftLengthMinutes !== baselineShiftLen ||
              baselineInterval !== intervalDays ||
              (baseline.recurrenceType ?? "Weekly") !== cadence;
          }

          for (let i = 0; i < selectedMembers.length; i++) {
            const userId = selectedMembers[i];
            const existingRotation = schedule.rotations.find((r) => r.userId === userId);

            if (existingRotation) {
              const newIsPrimary = i === 0;
              const newOrder = i + 1;
              const rotationChanged =
                timingChanged ||
                existingRotation.isPrimary !== newIsPrimary ||
                existingRotation.order !== newOrder;
              if (!rotationChanged) continue;

              await updateRotationMutation.mutateAsync({
                rotationId: existingRotation.id,
                handoverStartLocal: existingRotation.handoverStartLocal,
                shiftLengthMinutes: timingChanged
                  ? buildRotationTemplate(i).shiftLengthMinutes
                  : existingRotation.shiftLengthMinutes,
                recurrenceType: timingChanged
                  ? cadence
                  : (existingRotation.recurrenceType ?? cadence),
                recurrenceIntervalDays: timingChanged
                  ? intervalDays
                  : (existingRotation.recurrenceIntervalDays ?? intervalDays),
                isPrimary: newIsPrimary,
                order: newOrder,
              });
            } else {
              const tpl = buildRotationTemplate(i);
              await addRotationMutation.mutateAsync({
                scheduleId: id,
                userId: userId,
                handoverStartLocal: tpl.handoverStartLocal,
                shiftLengthMinutes: tpl.shiftLengthMinutes,
                isPrimary: i === 0,
                order: i + 1,
                recurrenceType: cadence,
                recurrenceIntervalDays: intervalDays,
              });
            }
          }
        }
      }
      navigate("/schedules");
    } finally {
      setIsSaving(false);
    }
  };

  const handleDelete = () => {
    if (!id) return;
    deleteScheduleMutation.mutate(id, {
      onSuccess: () => {
        setIsDeleteModalOpen(false);
        navigate("/schedules");
      },
    });
  };

  const isValid = scheduleName.trim().length > 0 && selectedMembers.length >= 1 && teamId.trim().length > 0;

  const generateCalendarDays = () => {
    const days = [];
    const today = new Date();
    const memberObjects = getSelectedMemberObjects();
    const daysPerMember =
      rotationType === "daily" ? 1 :
      rotationType === "weekly" ? 7 :
      Math.max(1, parseInt(rotationInterval, 10) || 7);
    for (let i = 0; i < previewDays; i++) {
      const date = new Date(today);
      date.setDate(today.getDate() + i);
      const memberIndex = Math.floor(i / daysPerMember);
      const member = memberObjects[memberIndex % memberObjects.length];
      days.push({ date, member, isToday: i === 0 });
    }
    return days;
  };

  if (!isNew && isLoading) {
    return (
      <div className="p-6 flex items-center justify-center min-h-[60vh]">
        <div className="text-center">
          <Loader2 className="w-8 h-8 animate-spin text-brand-500 mx-auto mb-3" />
          <p style={{ fontSize: "0.875rem", color: "#94A3B8" }}>
            {t("schedules.detailLoading")}
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
          <p style={{ fontSize: "1.125rem", fontWeight: 600, marginBottom: "0.5rem" }}>
            {t("schedules.loadScheduleFailed")}
          </p>
          <p style={{ fontSize: "0.875rem", color: "#94A3B8" }}>
            {error instanceof Error ? error.message : t("common.errorOccurred")}
          </p>
          <Button variant="outline" onClick={() => navigate("/schedules")} className="mt-4">
            {t("schedules.backToSchedules")}
          </Button>
        </div>
      </div>
    );
  }

  return (
    <>
      <div className="p-6 space-y-6">
        <nav className="flex items-center gap-2 text-sm">
          <Link to="/dashboard" className="text-muted-foreground hover:text-foreground transition-colors">
            <Home className="w-4 h-4" />
          </Link>
          <ChevronRight className="w-4 h-4 text-muted-foreground" />
          <Link to="/schedules" className="text-muted-foreground hover:text-foreground transition-colors">
            {t("schedules.breadcrumbShort")}
          </Link>
          <ChevronRight className="w-4 h-4 text-muted-foreground" />
          <span className="text-foreground font-medium">
            {isNew ? t("schedules.newSchedule") : scheduleName}
          </span>
        </nav>

        <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
          <div>
            <h1 style={{ fontSize: "1.875rem", fontWeight: 600 }}>
              {isNew ? t("schedules.detailPageTitleCreate") : t("schedules.detailPageTitleEdit")}
            </h1>
            <p style={{ fontSize: "0.875rem", color: "#94A3B8", marginTop: "0.25rem" }}>
              {t("schedules.detailPageSubtitle")}
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
                  {t("common.saving")}
                </>
              ) : (
                <>
                  <Save className="w-4 h-4 mr-2" />
                  {isNew ? t("schedules.createSchedule") : t("schedules.updateSchedule")}
                </>
              )}
            </Button>
          </div>
        </div>

        <Tabs value={activeTab} onValueChange={setActiveTab}>
          <TabsList className="bg-card/80 backdrop-blur-sm border border-border">
            <TabsTrigger value="config">
              <Clock className="w-4 h-4 mr-2" />
              {t("schedules.tabConfiguration")}
            </TabsTrigger>
            <TabsTrigger value="members">
              <Users className="w-4 h-4 mr-2" />
              {t("schedules.tabMembers")}
            </TabsTrigger>
            <TabsTrigger value="preview">
              <Calendar className="w-4 h-4 mr-2" />
              {t("schedules.tabPreview")}
            </TabsTrigger>
          </TabsList>

          <TabsContent value="config" className="space-y-6">
            <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
              <div className="lg:col-span-2 space-y-6">
                <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
                  <h3 style={{ fontSize: "1.125rem", fontWeight: 600, marginBottom: "1rem" }}>
                    {t("schedules.basicInformation")}
                  </h3>
                  <div className="space-y-4">
                    <div>
                      <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                        {t("schedules.labelScheduleName")} <span className="text-error-500">*</span>
                      </label>
                      <Input
                        placeholder={t("schedules.detailNameExamplePlaceholder")}
                        value={scheduleName}
                        onChange={(e) => setScheduleName(e.target.value)}
                        className="bg-input-background"
                      />
                    </div>
                    <div>
                      <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                        {t("schedules.labelTeam")} <span className="text-error-500">*</span>
                      </label>
                      <Select
                        value={teamId}
                        onValueChange={(v) => {
                          setTeamId(v);
                          setSelectedTeamFilter(v || "all");
                        }}
                      >
                        <SelectTrigger className="bg-input-background">
                          <SelectValue placeholder={t("schedules.selectTeamPlaceholder")} />
                        </SelectTrigger>
                        <SelectContent>
                          {apiTeams.filter((team) => team.id).map((team) => (
                            <SelectItem key={team.id} value={team.id}>
                              <div className="flex items-center gap-2">
                                <div className="w-2 h-2 rounded-full" style={{ backgroundColor: team.color || "#94A3B8" }} />
                                <span>{team.name}</span>
                              </div>
                            </SelectItem>
                          ))}
                        </SelectContent>
                      </Select>
                    </div>
                    <div>
                      <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                        Timezone <span className="text-error-500">*</span>
                      </label>
                      <TimezonePicker
                        value={scheduleTimezone}
                        onChange={setScheduleTimezone}
                        className="w-full bg-input-background"
                      />
                      <p className="mt-1 text-xs text-gray-500">
                        {t("schedules.timezoneHint")}
                      </p>
                    </div>
                    <div>
                      <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                        {t("schedules.labelDescription")}
                      </label>
                      <Input
                        placeholder={t("schedules.detailDescriptionPlaceholder")}
                        value={description}
                        onChange={(e) => setDescription(e.target.value)}
                        className="bg-input-background"
                      />
                    </div>
                    <div>
                      <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                        {t("schedules.labelRotationType")} <span className="text-error-500">*</span>
                      </label>
                      <Select
                        value={rotationType}
                        onValueChange={(value) =>
                          setRotationType(value as "daily" | "weekly" | "custom")
                        }
                      >
                        <SelectTrigger className="bg-input-background">
                          <SelectValue />
                        </SelectTrigger>
                        <SelectContent>
                          <SelectItem value="daily">
                            <div className="flex items-center gap-2">
                              <RotateCw className="w-4 h-4" />
                              <span>{t("schedules.rotationDaily")}</span>
                            </div>
                          </SelectItem>
                          <SelectItem value="weekly">
                            <div className="flex items-center gap-2">
                              <RotateCw className="w-4 h-4" />
                              <span>{t("schedules.rotationWeekly")}</span>
                            </div>
                          </SelectItem>
                          <SelectItem value="custom">
                            <div className="flex items-center gap-2">
                              <RotateCw className="w-4 h-4" />
                              <span>{t("schedules.rotationCustom")}</span>
                            </div>
                          </SelectItem>
                        </SelectContent>
                      </Select>
                    </div>
                  </div>
                </Card>

                <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
                  <h3 style={{ fontSize: "1.125rem", fontWeight: 600, marginBottom: "1rem" }}>
                    {t("schedules.shiftConfiguration")}
                  </h3>
                  <div className="space-y-4">
                    <div className="grid grid-cols-2 gap-4">
                      <div>
                        <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                          {t("schedules.shiftStartTime")}
                        </label>
                        <Input
                          type="time"
                          value={shiftStart}
                          onChange={(e) => setShiftStart(e.target.value)}
                          className="bg-input-background"
                        />
                      </div>
                      <div>
                        <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                          {t("schedules.shiftEndTime")}
                        </label>
                        <Input
                          type="time"
                          value={shiftEnd}
                          onChange={(e) => setShiftEnd(e.target.value)}
                          className="bg-input-background"
                        />
                      </div>
                    </div>

                    {rotationType === "custom" && (
                      <div>
                        <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                          {t("schedules.rotationIntervalDays")}
                        </label>
                        <Input
                          type="number"
                          min="1"
                          value={rotationInterval}
                          onChange={(e) => setRotationInterval(e.target.value)}
                          className="bg-input-background"
                        />
                      </div>
                    )}

                    <div className="flex items-center gap-2 p-3 rounded-lg bg-surface-light/20">
                      <input
                        type="checkbox"
                        id="24-7-toggle"
                        checked={shiftStart === "00:00" && shiftEnd === "23:59"}
                        onChange={(e) => {
                          if (e.target.checked) {
                            setShiftStart("00:00");
                            setShiftEnd("23:59");
                          } else {
                            setShiftStart("09:00");
                            setShiftEnd("17:00");
                          }
                        }}
                        className="w-4 h-4 rounded border-border bg-input-background"
                      />
                      <label htmlFor="24-7-toggle" style={{ fontSize: "0.875rem", cursor: "pointer" }}>
                        {t("schedules.coverage247")}
                        <span style={{ fontSize: "0.75rem", color: "#94A3B8", display: "block", marginTop: "0.125rem" }}>
                          {t("schedules.coverage247Hint")}
                        </span>
                      </label>
                    </div>
                  </div>
                </Card>
              </div>

              <div className="space-y-6">
                <Card className="p-6 bg-gradient-to-br from-brand-500/5 to-transparent border-brand-500/20">
                  <div className="flex items-start gap-3">
                    <Info className="w-5 h-5 text-brand-500 flex-shrink-0 mt-0.5" />
                    <div>
                      <h4 style={{ fontSize: "0.9375rem", fontWeight: 600, marginBottom: "0.5rem" }}>
                        {t("schedules.rotationTipsTitle")}
                      </h4>
                      <ul className="space-y-2" style={{ fontSize: "0.8125rem", color: "#94A3B8" }}>
                        <li className="flex gap-2">
                          <span className="text-brand-500">•</span>
                          <span>{t("schedules.tipDaily")}</span>
                        </li>
                        <li className="flex gap-2">
                          <span className="text-brand-500">•</span>
                          <span>{t("schedules.tipWeekly")}</span>
                        </li>
                        <li className="flex gap-2">
                          <span className="text-brand-500">•</span>
                          <span>{t("schedules.tipMinMembers")}</span>
                        </li>
                        <li className="flex gap-2">
                          <span className="text-brand-500">•</span>
                          <span>{t("schedules.tipOverrides")}</span>
                        </li>
                      </ul>
                    </div>
                  </div>
                </Card>

                <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
                  <h4 style={{ fontSize: "0.9375rem", fontWeight: 600, marginBottom: "1rem" }}>
                    {t("schedules.currentConfiguration")}
                  </h4>
                  <div className="space-y-3">
                    <div className="flex items-center justify-between">
                      <span style={{ fontSize: "0.8125rem", color: "#94A3B8" }}>{t("schedules.configType")}</span>
                      <Badge className="bg-brand-500/10 text-brand-500 border-brand-500/20 border text-xs">
                        {rotationType}
                      </Badge>
                    </div>
                    <div className="flex items-center justify-between">
                      <span style={{ fontSize: "0.8125rem", color: "#94A3B8" }}>{t("schedules.configCoverage")}</span>
                      <span style={{ fontSize: "0.8125rem", fontWeight: 600 }}>
                        {shiftStart === "00:00" && shiftEnd === "23:59"
                          ? "24/7"
                          : `${shiftStart} - ${shiftEnd}`}
                      </span>
                    </div>
                    <div className="flex items-center justify-between">
                      <span style={{ fontSize: "0.8125rem", color: "#94A3B8" }}>{t("schedules.configMembers")}</span>
                      <span style={{ fontSize: "0.8125rem", fontWeight: 600 }}>
                        {selectedMembers.length}
                      </span>
                    </div>
                  </div>
                </Card>
              </div>
            </div>
          </TabsContent>

          <TabsContent value="members" className="space-y-6">
            <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
              <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
                <div className="flex items-center justify-between mb-4">
                  <h3 style={{ fontSize: "1.125rem", fontWeight: 600 }}>
                    {t("schedules.rotationMembersTitle")}
                  </h3>
                  <Badge className="bg-brand-500/10 text-brand-500 border-brand-500/20 border">
                    {t("schedules.membersCountBadge", { count: selectedMembers.length })}
                  </Badge>
                </div>

                {selectedMembers.length > 0 ? (
                  <div className="space-y-2">
                    {getSelectedMemberObjects().map((member, index) => (
                      <div
                        key={member.id}
                        className="flex items-center gap-3 p-3 rounded-lg bg-surface-light/20 border border-border"
                      >
                        <div className="flex items-center gap-3 flex-1 min-w-0">
                          <div
                            className="w-10 h-10 rounded-full flex items-center justify-center text-white font-bold flex-shrink-0"
                            style={{ backgroundColor: member.color }}
                          >
                            {member.initials}
                          </div>
                          <div className="flex-1 min-w-0">
                            <p style={{ fontSize: "0.9375rem", fontWeight: 600 }}>
                              {member.name}
                            </p>
                            <div className="flex items-center gap-3 mt-0.5">
                              <div className="flex items-center gap-1">
                                <Phone className="w-3 h-3 text-muted-foreground" />
                                <span
                                  style={{ fontSize: "0.75rem", color: "#94A3B8" }}
                                  className="font-mono"
                                >
                                  {member.phone}
                                </span>
                              </div>
                            </div>
                          </div>
                          <Badge className={index === 0
                            ? "bg-brand-500/20 text-brand-400 border-brand-500/30 border text-xs"
                            : "bg-muted/20 text-muted-foreground border-border border text-xs"
                          }>
                            {index === 0 ? t("schedules.positionPrimary") : t("schedules.positionN", { n: String(index + 1) })}
                          </Badge>
                        </div>
                        <div className="flex items-center gap-1">
                          <Button
                            size="sm"
                            variant="ghost"
                            onClick={() => moveMemberUp(index)}
                            disabled={index === 0}
                            className="h-7 w-7 p-0 text-muted-foreground hover:text-foreground disabled:opacity-30"
                          >
                            <ChevronUp className="w-4 h-4" />
                          </Button>
                          <Button
                            size="sm"
                            variant="ghost"
                            onClick={() => moveMemberDown(index)}
                            disabled={index === selectedMembers.length - 1}
                            className="h-7 w-7 p-0 text-muted-foreground hover:text-foreground disabled:opacity-30"
                          >
                            <ChevronDown className="w-4 h-4" />
                          </Button>
                          <Button
                            size="sm"
                            variant="ghost"
                            onClick={() => removeMember(member.id)}
                            className="h-7 w-7 p-0 text-error-500 hover:bg-error-500/10"
                          >
                            <X className="w-4 h-4" />
                          </Button>
                        </div>
                      </div>
                    ))}
                  </div>
                ) : (
                  <div className="text-center py-8">
                    <Users className="w-12 h-12 text-muted-foreground mx-auto mb-3 opacity-50" />
                    <p style={{ fontSize: "0.9375rem", fontWeight: 600, marginBottom: "0.5rem" }}>
                      {t("schedules.noMembersSelectedTitle")}
                    </p>
                    <p style={{ fontSize: "0.8125rem", color: "#94A3B8" }}>
                      {t("schedules.noMembersSelectedHint")}
                    </p>
                  </div>
                )}

                {selectedMembers.length < 1 && (
                  <div className="mt-4 p-3 rounded-lg bg-warning-500/10 border border-warning-500/20 flex items-start gap-2">
                    <AlertCircle className="w-4 h-4 text-warning-500 flex-shrink-0 mt-0.5" />
                    <p style={{ fontSize: "0.8125rem", color: "#FB923C" }}>
                      {t("schedules.minMembersWarning")}
                    </p>
                  </div>
                )}
                {selectedMembers.length >= 1 && (
                  <div className="mt-4 p-3 rounded-lg bg-brand-500/5 border border-brand-500/20 flex items-start gap-2">
                    <Info className="w-4 h-4 text-brand-500 flex-shrink-0 mt-0.5" />
                    <div className="flex-1">
                      <p style={{ fontSize: "0.8125rem", color: "#94A3B8" }}>
                        {t("schedules.previewRotationHint")}
                      </p>
                      <button
                        type="button"
                        onClick={() => setActiveTab("preview")}
                        className="mt-1 text-xs font-medium text-brand-500 hover:text-brand-400"
                      >
                        {t("schedules.previewRotationLink")} →
                      </button>
                    </div>
                  </div>
                )}
              </Card>

              <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
                <div className="flex items-center justify-between mb-4">
                  <h3 style={{ fontSize: "1.125rem", fontWeight: 600 }}>
                    {t("schedules.availableMembersTitle")}
                  </h3>
                </div>

                <div className="mb-4">
                  <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                    {t("schedules.filterByTeam")}
                  </label>
                  <Select value={selectedTeamFilter} onValueChange={setSelectedTeamFilter}>
                    <SelectTrigger className="bg-input-background">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="all">
                        <div className="flex items-center gap-2">
                          <Users className="w-4 h-4" />
                          <span>{t("schedules.allTeams")}</span>
                        </div>
                      </SelectItem>
                      {apiTeams.filter((team) => team.id).map((team) => (
                        <SelectItem key={team.id} value={team.id}>
                          <div className="flex items-center gap-2">
                            <div className="w-2 h-2 rounded-full" style={{ backgroundColor: team.color || "#94A3B8" }} />
                            <span>{team.name}</span>
                          </div>
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>

                {getAvailableMembers().length > 0 ? (
                  <div className="space-y-2">
                    {getAvailableMembers().map((member) => (
                      <div
                        key={member.id}
                        className="flex items-center gap-3 p-3 rounded-lg bg-surface-light/20 border border-border hover:border-border-light transition-colors"
                      >
                        <div
                          className="w-10 h-10 rounded-full flex items-center justify-center text-white font-bold flex-shrink-0"
                          style={{ backgroundColor: member.color }}
                        >
                          {member.initials}
                        </div>
                        <div className="flex-1 min-w-0">
                          <p style={{ fontSize: "0.9375rem", fontWeight: 600 }}>
                            {member.name}
                          </p>
                          <p
                            style={{ fontSize: "0.75rem", color: "#94A3B8" }}
                            className="truncate"
                          >
                            {member.email}
                          </p>
                        </div>
                        <Button
                          size="sm"
                          onClick={() => addMember(member.id)}
                          className="bg-brand-500 hover:bg-brand-600 text-white"
                        >
                          <Plus className="w-4 h-4 mr-1" />
                          {t("common.add")}
                        </Button>
                      </div>
                    ))}
                  </div>
                ) : (
                  <div className="text-center py-8">
                    <CheckCircle className="w-12 h-12 text-success-500 mx-auto mb-3" />
                    <p style={{ fontSize: "0.9375rem", fontWeight: 600, marginBottom: "0.5rem" }}>
                      {t("schedules.allMembersAddedTitle")}
                    </p>
                    <p style={{ fontSize: "0.8125rem", color: "#94A3B8" }}>
                      {t("schedules.allMembersAddedHint")}
                    </p>
                  </div>
                )}
              </Card>
            </div>
          </TabsContent>

          <TabsContent value="preview" className="space-y-6">
            <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
              <div className="mb-4 flex flex-col sm:flex-row sm:items-start sm:justify-between gap-3">
                <h3 style={{ fontSize: "1.125rem", fontWeight: 600 }}>
                  {previewDays === 14 ? t("schedules.preview14DayTitle") : `${previewDays}-day preview`}
                </h3>
                <div className="flex flex-wrap gap-2">
                  <div className="flex overflow-hidden rounded-md border border-border text-xs">
                    {([14, 30, 90] as const).map((d) => (
                      <button
                        key={d}
                        type="button"
                        onClick={() => setPreviewDays(d)}
                        className={`px-3 py-1.5 transition-colors ${
                          previewDays === d
                            ? "bg-brand-500 text-white"
                            : "bg-surface-light/20 text-muted-foreground hover:bg-surface-light/40"
                        }`}
                      >
                        {d}d
                      </button>
                    ))}
                  </div>
                <div className="flex overflow-hidden rounded-md border border-border text-xs">
                  {(["schedule", "my", "utc"] as const).map((opt) => {
                    const label =
                      opt === "schedule"
                        ? `Schedule (${scheduleTimezone})`
                        : opt === "my"
                          ? `My TZ (${browserTimezone()})`
                          : "UTC";
                    return (
                      <button
                        key={opt}
                        type="button"
                        onClick={() => setViewerTz(opt)}
                        className={`px-3 py-1.5 transition-colors ${
                          viewerTz === opt
                            ? "bg-brand-500 text-white"
                            : "bg-surface-light/20 text-muted-foreground hover:bg-surface-light/40"
                        }`}
                      >
                        {label}
                      </button>
                    );
                  })}
                </div>
                </div>
              </div>

              {serverOccurrences && serverOccurrences.length > 0 && !isFormDirty ? (
                <OccurrenceCalendar
                  occurrences={serverOccurrences}
                  viewerTz={viewerTz}
                  scheduleTz={scheduleTimezone}
                  dateLocale={dateLocale}
                />
              ) : selectedMembers.length >= 1 ? (
                <>
                  {isFormDirty && serverOccurrences && serverOccurrences.length > 0 && (
                    <div className="mb-3 px-3 py-2 rounded-md bg-warning-500/10 border border-warning-500/30 text-xs text-warning-500">
                      {t("schedules.unsavedChangesPreview") ?? "Unsaved changes — showing a live preview based on the current form. The authoritative schedule updates after you save."}
                    </div>
                  )}
                <div className="space-y-2">
                  {generateCalendarDays().map((day, index) => (
                    <div
                      key={index}
                      className={`flex items-center gap-4 p-4 rounded-lg border transition-all ${day.isToday
                        ? "bg-brand-500/10 border-brand-500/30 shadow-lg shadow-brand-500/10"
                        : "bg-surface-light/20 border-border hover:border-border-light"
                        }`}
                    >
                      <div className="flex flex-col items-center w-16 flex-shrink-0">
                        <p style={{ fontSize: "0.75rem", color: "#94A3B8", fontWeight: 600 }}>
                          {day.date.toLocaleDateString(dateLocale, { weekday: "short" })}
                        </p>
                        <p style={{ fontSize: "1.25rem", fontWeight: 700 }}>
                          {day.date.getDate()}
                        </p>
                        <p style={{ fontSize: "0.75rem", color: "#94A3B8" }}>
                          {day.date.toLocaleDateString(dateLocale, { month: "short" })}
                        </p>
                      </div>

                      <div className="w-px h-12 bg-border" />

                      <div className="flex items-center gap-3 flex-1">
                        {day.member && (
                          <>
                            <div
                              className="w-12 h-12 rounded-full flex items-center justify-center text-white font-bold"
                              style={{ backgroundColor: day.member.color }}
                            >
                              {day.member.initials}
                            </div>
                            <div>
                              <p style={{ fontSize: "0.9375rem", fontWeight: 600 }}>
                                {day.member.name}
                              </p>
                              <div className="flex items-center gap-2 mt-0.5">
                                <Clock className="w-3 h-3 text-muted-foreground" />
                                <p style={{ fontSize: "0.75rem", color: "#94A3B8" }}>
                                  {shiftStart} - {shiftEnd}
                                </p>
                              </div>
                            </div>
                          </>
                        )}
                      </div>

                      {day.isToday && (
                        <Badge className="bg-brand-500 text-white border-0">
                          {t("statusPage.uptimeToday")}
                        </Badge>
                      )}
                    </div>
                  ))}
                </div>
                </>
              ) : (
                <div className="text-center py-12">
                  <Calendar className="w-12 h-12 text-muted-foreground mx-auto mb-3 opacity-50" />
                  <p style={{ fontSize: "0.9375rem", fontWeight: 600, marginBottom: "0.5rem" }}>
                    {t("schedules.previewNotAvailableTitle")}
                  </p>
                  <p style={{ fontSize: "0.8125rem", color: "#94A3B8" }}>
                    {t("schedules.detailPreviewNotAvailableHint")}
                  </p>
                </div>
              )}
            </Card>
          </TabsContent>
        </Tabs>

        <Dialog open={isDeleteModalOpen} onOpenChange={setIsDeleteModalOpen}>
          <DialogContent className="bg-card border-border sm:max-w-[500px]">
            <DialogHeader>
              <DialogTitle style={{ fontSize: "1.5rem", fontWeight: 600 }}>
                {t("schedules.deleteScheduleTitle")}
              </DialogTitle>
            </DialogHeader>
            <div className="py-4">
              <div className="flex gap-3 mb-4">
                <div className="w-10 h-10 rounded-full bg-error-500/10 flex items-center justify-center flex-shrink-0">
                  <AlertCircle className="w-5 h-5 text-error-500" />
                </div>
                <div>
                  <p style={{ fontSize: "0.875rem", marginBottom: "0.5rem" }}>
                    {t("schedules.deleteScheduleMsg", { name: scheduleName })}
                  </p>
                  <p style={{ fontSize: "0.8125rem", color: "#94A3B8" }}>
                    {t("schedules.deleteScheduleWarn")}
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
                disabled={deleteScheduleMutation.isPending}
                className="bg-error-500 hover:bg-error-600 text-white"
              >
                <Trash2 className="w-4 h-4 mr-2" />
                {deleteScheduleMutation.isPending ? t("schedules.deleting") : t("schedules.deleteSchedule")}
              </Button>
            </DialogFooter>
          </DialogContent>
        </Dialog>
      </div>
    </>
  );
}

/**
 * Renders materialized occurrences as a calendar list. Each row shows the shift in the
 * viewer's chosen TZ with a zone-abbreviation suffix (e.g. "EDT") so cross-zone teams
 * see both their local time AND the authoritative schedule zone context.
 */
function OccurrenceCalendar({
  occurrences,
  viewerTz,
  scheduleTz,
  dateLocale,
}: {
  occurrences: import("../types/schedule.types").ScheduleRotationDto[];
  viewerTz: "schedule" | "my" | "utc";
  scheduleTz: string;
  dateLocale: string;
}) {
  const effectiveTz =
    viewerTz === "schedule" ? scheduleTz : viewerTz === "utc" ? "UTC" : browserTimezone();
  const nowMs = Date.now();
  const rows = occurrences
    .filter((o) => !!o.startUtc)
    .map((o) => {
      const start = new Date(o.startUtc!);
      const end = new Date(o.endUtc!);
      const isActive = nowMs >= start.getTime() && nowMs < end.getTime();
      return { o, start, end, isActive };
    });

  if (rows.length === 0) return null;
  return (
    <div className="space-y-2">
      {rows.map((row, i) => (
        <div
          key={row.o.id + i}
          className={`flex items-center gap-4 rounded-lg border p-4 transition-all ${
            row.isActive
              ? "bg-brand-500/10 border-brand-500/30 shadow-lg shadow-brand-500/10"
              : "bg-surface-light/20 border-border"
          }`}
        >
          <div className="flex w-24 flex-shrink-0 flex-col items-center">
            <p style={{ fontSize: "0.75rem", color: "#94A3B8", fontWeight: 600 }}>
              {row.start.toLocaleDateString(dateLocale, { weekday: "short", timeZone: effectiveTz })}
            </p>
            <p style={{ fontSize: "1.125rem", fontWeight: 700 }}>
              {row.start.toLocaleDateString(dateLocale, { day: "numeric", month: "short", timeZone: effectiveTz })}
            </p>
          </div>

          <div className="w-px h-12 bg-border" />

          <div className="flex-1 min-w-0">
            <p style={{ fontSize: "0.9375rem", fontWeight: 600 }}>
              {row.o.userName ?? row.o.userId}
            </p>
            <div className="mt-0.5 flex items-center gap-2">
              <Clock className="h-3 w-3 text-muted-foreground" />
              <p style={{ fontSize: "0.75rem", color: "#94A3B8" }}>
                {formatForDisplay(row.start, effectiveTz, dateLocale)} — {formatForDisplay(row.end, effectiveTz, dateLocale)}{" "}
                <span className="opacity-70">({zoneAbbreviation(row.start, effectiveTz)})</span>
              </p>
            </div>
          </div>

          {row.isActive && <Badge className="bg-brand-500 text-white border-0">Live</Badge>}
        </div>
      ))}
    </div>
  );
}