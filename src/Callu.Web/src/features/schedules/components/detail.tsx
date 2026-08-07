import { useState, useEffect, useRef, useMemo, useId } from "react";
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
  buildRotationTemplate,
  isRotationBlockSupported,
  maxBlockDays,
  resolveCycleAnchor,
} from "@/features/schedules/utils/rotation-template";
import {
  buildSchedulePlanRotations,
} from "@/features/schedules/utils/rotation-plan";
import type { SchedulePlanRotation, OnCallOverrideDto } from "../types/schedule.types";
import { OnCallStrip } from "./on-call-strip";
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
  UserCog,
} from "lucide-react";
import {
  useSchedule,
  useScheduleOccurrences,
  useScheduleCoverage,
  useCreateSchedule,
  useDeleteSchedule,
  useSaveSchedulePlan,
  useDeleteOverride,
} from "../hooks/use-schedules";
import { useUsers } from "@/features/users/hooks/use-users";
import { useTeams, useTeam } from "@/features/teams/hooks/use-teams";
import { useOrganizationSettings } from "@/features/settings/hooks/use-settings";
import { getLocale, t } from "@/shared/locales/i18n";
import { deriveFormFromSchedule } from "../utils/schedule-form";
import { useLocaleTick } from "@/shared/hooks/use-locale-tick";
import { DeleteConfirmDialog } from "@/shared/components/delete-confirm-dialog";
import { formatDateTime } from "@/shared/utils/time";
import { dateLocale as appDateLocale } from "@/shared/utils/datetime";

interface Member {
  id: string;
  name: string;
  initials: string;
  phone: string;
  email: string;
  color: string;
}

const AVATAR_COLORS = ["#3E7BFA", "#22C55E", "#FB923C", "#A855F7", "#EC4899", "#EF4444", "#14B8A6", "#F59E0B"];

// Copy for keys not yet present in the locale files. `t()` echoes the key back when it is
// missing, so fall back explicitly instead of rendering a raw key.
const FALLBACK_COPY: Record<string, { en: string; tr: string }> = {
  "schedules.handoverRecalcWarning": {
    en: "Rotation order, cadence or shift times changed — handover times will be recalculated when you save.",
    tr: "Sıra, döngü veya vardiya saatleri değişti — kaydettiğinizde devir saatleri yeniden hesaplanacak.",
  },
  "schedules.handoverRecalcRow": {
    en: "{name}: {from} → {to}",
    tr: "{name}: {from} → {to}",
  },
  "schedules.intervalTooLongError": {
    en: "A shift window shorter than 24h can cover at most {max} days per member. Reduce the interval or switch to 24/7 coverage.",
    tr: "24 saatten kısa vardiya penceresi üye başına en fazla {max} gün kapsayabilir. Aralığı düşürün ya da 24/7 kapsamaya geçin.",
  },
  "schedules.interval247TooLongError": {
    en: "A 24/7 block can cover at most {max} days per member. Reduce the interval.",
    tr: "24/7 blok üye başına en fazla {max} gün kapsayabilir. Aralığı düşürün.",
  },
  "schedules.handoverConfirmTitle": {
    en: "Handover times will move",
    tr: "Devir saatleri değişecek",
  },
  "schedules.handoverConfirmBody": {
    en: "Saving re-phases the rotation. Who is on call at a given time changes as listed below.",
    tr: "Kaydetmek rotasyonu yeniden fazlar. Aşağıdaki gibi, hangi saatte kimin nöbette olduğu değişir.",
  },
  "schedules.handoverConfirmSave": {
    en: "Save anyway",
    tr: "Yine de kaydet",
  },
  // The API applies the plan in one transaction, so a rejected save left the schedule exactly as
  // it was. Nothing to reconcile, nothing to warn about beyond the reason.
  "schedules.saveFailed": {
    en: "Save failed — nothing was changed. {reason}",
    tr: "Kayıt başarısız — hiçbir şey değişmedi. {reason}",
  },
};

function tf(key: string, params?: Record<string, string>): string {
  const value = t(key, params);
  if (value !== key) return value;
  const fallback = FALLBACK_COPY[key];
  if (!fallback) return key;
  let text = fallback[getLocale() === "tr" ? "tr" : "en"];
  for (const [k, v] of Object.entries(params ?? {})) {
    text = text.split(`{${k}}`).join(v);
  }
  return text;
}

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
  const formId = useId();

  const [previewDays, setPreviewDays] = useState<14 | 30 | 90>(14);

  const { data: schedule, isLoading, error } = useSchedule(isNew ? "" : id!);
  const { data: serverOccurrences } = useScheduleOccurrences(isNew ? "" : id ?? "", previewDays);
  // Matches the post-save LogWarning horizon (materializer default = 30 days).
  const { data: coverage } = useScheduleCoverage(isNew ? "" : id ?? "", 30);
  const createScheduleMutation = useCreateSchedule();
  const deleteScheduleMutation = useDeleteSchedule();
  const saveSchedulePlanMutation = useSaveSchedulePlan();
  const deleteOverrideMutation = useDeleteOverride();
  const [cancellingOverride, setCancellingOverride] = useState<OnCallOverrideDto | null>(null);
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
  const [isSaving, setIsSaving] = useState(false);
  const [isDeleteModalOpen, setIsDeleteModalOpen] = useState(false);
  const [isHandoverConfirmOpen, setIsHandoverConfirmOpen] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [selectedTeamFilter, setSelectedTeamFilter] = useState<string>("all");

  const i18nTick = useLocaleTick();

  useEffect(() => {
    if (isNew && scheduleTimezone === "UTC") {
      const fallback = (orgSettings as { defaultTimezone?: string } | null | undefined)?.defaultTimezone
        ?? Intl.DateTimeFormat().resolvedOptions().timeZone;
      if (fallback && fallback !== "UTC") setScheduleTimezone(fallback);
    }
  }, [isNew, orgSettings, scheduleTimezone]);

  void i18nTick;
  const dateLocale = appDateLocale();

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
    if (!schedule) return;

    // One derivation feeds both the inputs and the snapshot, so "unchanged" always means exactly
    // the values the form was seeded with.
    const form = deriveFormFromSchedule(schedule);

    setScheduleName(form.scheduleName);
    setDescription(form.description);
    setTeamId(form.teamId);
    setSelectedTeamFilter(schedule.teamId ?? "all");
    setScheduleTimezone(form.scheduleTimezone);
    setSelectedMembers(form.memberIds);
    setShiftStart(form.shiftStart);
    setShiftEnd(form.shiftEnd);
    setRotationType(form.rotationType);
    setRotationInterval(form.rotationInterval);

    initialFormRef.current = { ...form, memberIds: form.memberIds.join(",") };
  }, [schedule]);

  // Every rotation is phased off this one anchor, so member order alone decides who is
  // on call when. Derived from stored data, never from `new Date()`, so re-saving an
  // unchanged schedule does not drift the grid.
  const cycleAnchor = useMemo(
    () =>
      resolveCycleAnchor(
        schedule?.rotations.map((r) => r.handoverStartLocal) ?? [],
        schedule?.createdAt,
      ),
    [schedule],
  );

  const is247 = shiftStart === "00:00" && shiftEnd === "23:59";
  const daysPerMember =
    rotationType === "daily" ? 1 :
    rotationType === "weekly" ? 7 :
    Math.max(1, parseInt(rotationInterval, 10) || 7);
  const blockLimit = maxBlockDays(is247);
  const blockSupported = isRotationBlockSupported(daysPerMember, is247);
  const blockLimitMessage = tf(
    is247 ? "schedules.interval247TooLongError" : "schedules.intervalTooLongError",
    { max: String(blockLimit) },
  );

  // Only these fields decide when each member hands over; the form derives them from the first
  // rotation alone, so an unrelated save must leave the stored timing untouched.
  const rotationLayoutChanged = useMemo(() => {
    const snap = initialFormRef.current;
    if (!snap) return false;
    return (
      rotationType !== snap.rotationType ||
      rotationInterval !== snap.rotationInterval ||
      shiftStart !== snap.shiftStart ||
      shiftEnd !== snap.shiftEnd ||
      selectedMembers.join(",") !== snap.memberIds
    );
  }, [rotationType, rotationInterval, shiftStart, shiftEnd, selectedMembers]);

  const isFormDirty = useMemo(() => {
    const snap = initialFormRef.current;
    if (!snap) return false;
    return (
      rotationLayoutChanged ||
      scheduleName !== snap.scheduleName ||
      description !== snap.description ||
      teamId !== snap.teamId ||
      scheduleTimezone !== snap.scheduleTimezone
    );
  }, [rotationLayoutChanged, scheduleName, description, teamId, scheduleTimezone]);

  // Removals are derived at save time from the member list (a rotation whose user is no longer
  // selected is deleted), so nothing here can go stale against a refetched schedule.
  const addMember = (memberId: string) => {
    if (!selectedMembers.includes(memberId)) {
      setSelectedMembers([...selectedMembers, memberId]);
    }
  };

  const removeMember = (memberId: string) => {
    setSelectedMembers((prev) => prev.filter((mid) => mid !== memberId));
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

  const templateFor = (memberIndex: number) =>
    buildRotationTemplate({
      anchor: cycleAnchor,
      memberIndex,
      daysPerMember,
      shiftStart,
      shiftEnd,
      is247,
    });

  /**
   * Handovers that a save would move, so the operator can be told before committing. Empty
   * unless the rotation layout actually changed.
   */
  const pendingHandoverMoves = (): { name: string; from: Date; to: Date }[] => {
    if (isNew || !schedule || !rotationLayoutChanged) return [];
    const moves: { name: string; from: Date; to: Date }[] = [];
    selectedMembers.forEach((userId, i) => {
      const existing = schedule.rotations.find((r) => r.userId === userId);
      if (!existing?.handoverStartLocal) return;
      const from = new Date(existing.handoverStartLocal);
      const to = new Date(templateFor(i).handoverStartLocal);
      if (!Number.isFinite(from.getTime()) || from.getTime() === to.getTime()) return;
      moves.push({
        name: allMembers.find((m) => m.id === userId)?.name ?? userId,
        from,
        to,
      });
    });
    return moves;
  };

  /** The rotations this save wants the schedule to end up with, or undefined when the layout is
   * untouched — the request omits the field, which tells the API to leave the stored rota alone. */
  const buildPlanRotations = (): SchedulePlanRotation[] | undefined => {
    const rotations = buildSchedulePlanRotations({
      rotations: schedule?.rotations ?? [],
      selectedMemberIds: selectedMembers,
      rotationLayoutChanged: isNew || rotationLayoutChanged,
      anchor: cycleAnchor,
      daysPerMember,
      shiftStart,
      shiftEnd,
      is247,
    });
    return rotations ?? undefined;
  };

  const performSave = async () => {
    if (!blockSupported) return;
    setSaveError(null);
    setIsSaving(true);
    try {
      // Built from this render's state before any await, so a refetch cannot change what is sent.
      const rotations = buildPlanRotations();

      if (isNew) {
        const result = await createScheduleMutation.mutateAsync({
          name: scheduleName,
          description: description || undefined,
          teamId,
          timezone: scheduleTimezone,
        });
        if (result && rotations?.length) {
          try {
            await saveSchedulePlanMutation.mutateAsync({ id: result.id, rotations });
          } catch (rotationErr) {
            // The schedule exists but has no rota, so it would page nobody. Nothing depends on
            // it yet — take it back out rather than leave an empty on-call schedule behind.
            try {
              await deleteScheduleMutation.mutateAsync(result.id);
            } catch { /* empty */ }
            throw rotationErr;
          }
        }
      } else if (id) {
        // One request, one transaction on the server: the settings and the whole rota move
        // together, so there is no window in which half the members sit on a new handover grid.
        await saveSchedulePlanMutation.mutateAsync({
          id,
          name: scheduleName,
          description: description || "",
          timezone: scheduleTimezone,
          teamId: teamId || undefined,
          rotations,
        });
      }
      navigate("/schedules");
    } catch (err) {
      const reason = err instanceof Error ? err.message : t("common.errorOccurred");
      setSaveError(tf("schedules.saveFailed", { reason }));
    } finally {
      setIsSaving(false);
    }
  };

  // The handover diff is a save-time decision, not a Preview-tab one: confirm it from whichever
  // tab the operator pressed Save on.
  const handleSave = () => {
    setSaveError(null);
    if (pendingHandoverMoves().length > 0) {
      setIsHandoverConfirmOpen(true);
      return;
    }
    void performSave();
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

  const isValid =
    scheduleName.trim().length > 0 &&
    selectedMembers.length >= 1 &&
    teamId.trim().length > 0 &&
    blockSupported;

  const canSave = isValid && !isSaving && (isNew || isFormDirty);

  // Local preview of the unsaved form. Phased off the same anchor the save path uses, so
  // it lines up with the occurrences the backend will materialize. Day ownership is a
  // calendar-day question, so the anchor is compared at midnight.
  const generateCalendarDays = () => {
    const memberObjects = getSelectedMemberObjects();
    if (memberObjects.length === 0) return [];

    const anchorDay = new Date(cycleAnchor);
    anchorDay.setHours(0, 0, 0, 0);

    const today = new Date();
    today.setHours(0, 0, 0, 0);
    const msPerDay = 24 * 60 * 60 * 1000;

    const days = [];
    for (let i = 0; i < previewDays; i++) {
      const date = new Date(today);
      date.setDate(today.getDate() + i);
      const dayOffset = Math.round((date.getTime() - anchorDay.getTime()) / msPerDay);
      const slot = Math.floor(dayOffset / daysPerMember) % memberObjects.length;
      const member = memberObjects[(slot + memberObjects.length) % memberObjects.length];
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
              disabled={!canSave}
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

        {saveError && (
          <div className="flex items-start gap-2 rounded-lg border border-error-500/30 bg-error-500/10 p-3">
            <AlertCircle className="w-4 h-4 text-error-500 flex-shrink-0 mt-0.5" />
            <p style={{ fontSize: "0.8125rem" }} className="text-error-500">
              {saveError}
            </p>
          </div>
        )}

        {!isNew && coverage && !coverage.hasFullCoverage && (
          <div
            className="flex items-start gap-2 rounded-lg border border-warning-500/30 bg-warning-500/10 p-3"
            role="status"
          >
            <AlertCircle className="w-4 h-4 text-warning-500 flex-shrink-0 mt-0.5" />
            <div className="min-w-0 space-y-1.5">
              <p className="text-sm font-medium text-warning-500">
                {t("schedules.coverageGapTitle", {
                  percent: coverage.coveragePercent,
                  hours: coverage.gapHours,
                  days: 30,
                })}
              </p>
              <p className="text-xs text-muted-foreground">
                {t("schedules.coverageGapHint")}
              </p>
              {coverage.gaps.length > 0 && (
                <ul className="text-xs text-warning-500/90 space-y-0.5">
                  {coverage.gaps.slice(0, 5).map((gap) => (
                    <li key={`${gap.start}-${gap.end}`}>
                      {t("schedules.coverageGapRow", {
                        start: formatDateTime(gap.start),
                        end: formatDateTime(gap.end),
                      })}
                    </li>
                  ))}
                  {coverage.gaps.length > 5 && (
                    <li>
                      {t("schedules.coverageGapMore", {
                        count: coverage.gaps.length - 5,
                      })}
                    </li>
                  )}
                </ul>
              )}
            </div>
          </div>
        )}

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
            <TabsTrigger value="overrides">
              <UserCog className="w-4 h-4 mr-2" />
              {t("schedules.tabOverrides")}
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
                      <label htmlFor={`${formId}-team`} style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                        {t("schedules.labelTeam")} <span className="text-error-500">*</span>
                      </label>
                      <Select
                        value={teamId}
                        onValueChange={(v) => {
                          setTeamId(v);
                          setSelectedTeamFilter(v || "all");
                        }}
                      >
                        <SelectTrigger id={`${formId}-team`} className="bg-input-background">
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
                      <label htmlFor={`${formId}-timezone`} style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                        Timezone <span className="text-error-500">*</span>
                      </label>
                      <TimezonePicker
                        id={`${formId}-timezone`}
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
                      <label htmlFor={`${formId}-rotation-type`} style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                        {t("schedules.labelRotationType")} <span className="text-error-500">*</span>
                      </label>
                      <Select
                        value={rotationType}
                        onValueChange={(value) =>
                          setRotationType(value as "daily" | "weekly" | "custom")
                        }
                      >
                        <SelectTrigger id={`${formId}-rotation-type`} className="bg-input-background">
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
                          max={String(blockLimit)}
                          value={rotationInterval}
                          onChange={(e) => setRotationInterval(e.target.value)}
                          className="bg-input-background"
                        />
                      </div>
                    )}

                    {!blockSupported && (
                      <div className="flex items-start gap-2 rounded-lg border border-error-500/30 bg-error-500/10 p-3">
                        <AlertCircle className="w-4 h-4 text-error-500 flex-shrink-0 mt-0.5" />
                        <p style={{ fontSize: "0.8125rem" }} className="text-error-500">
                          {blockLimitMessage}
                        </p>
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
                  <label htmlFor={`${formId}-team-filter`} style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                    {t("schedules.filterByTeam")}
                  </label>
                  <Select value={selectedTeamFilter} onValueChange={setSelectedTeamFilter}>
                    <SelectTrigger id={`${formId}-team-filter`} className="bg-input-background">
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

              {!blockSupported && (
                <div className="mb-3 flex items-start gap-2 rounded-md border border-error-500/30 bg-error-500/10 px-3 py-2">
                  <AlertCircle className="w-4 h-4 text-error-500 flex-shrink-0 mt-0.5" />
                  <p className="text-xs text-error-500">{blockLimitMessage}</p>
                </div>
              )}

              {serverOccurrences && serverOccurrences.length > 0 && !rotationLayoutChanged ? (
                <div className="space-y-5">
                  <OnCallStrip
                    occurrences={serverOccurrences}
                    overrides={schedule?.overrides ?? []}
                    days={previewDays}
                    timeZone={
                      viewerTz === "schedule"
                        ? scheduleTimezone
                        : viewerTz === "utc"
                          ? "UTC"
                          : browserTimezone()
                    }
                    dateLocale={dateLocale}
                  />
                  <details className="group">
                    <summary className="cursor-pointer text-xs text-muted-foreground hover:text-foreground">
                      {t("onCallStrip.listView")}
                    </summary>
                    <div className="mt-3">
                      <OccurrenceCalendar
                        occurrences={serverOccurrences}
                        viewerTz={viewerTz}
                        scheduleTz={scheduleTimezone}
                        dateLocale={dateLocale}
                      />
                    </div>
                  </details>
                </div>
              ) : selectedMembers.length >= 1 && blockSupported ? (
                <>
                  {rotationLayoutChanged && serverOccurrences && serverOccurrences.length > 0 && (
                    <div className="mb-3 px-3 py-2 rounded-md bg-warning-500/10 border border-warning-500/30 text-xs text-warning-500">
                      {t("schedules.unsavedChangesPreview")}
                    </div>
                  )}
                  {(() => {
                    const moves = pendingHandoverMoves();
                    if (moves.length === 0) return null;
                    return (
                      <div className="mb-3 rounded-md border border-warning-500/30 bg-warning-500/10 px-3 py-2">
                        <div className="flex items-start gap-2">
                          <AlertCircle className="w-4 h-4 text-warning-500 flex-shrink-0 mt-0.5" />
                          <p className="text-xs font-semibold text-warning-500">
                            {tf("schedules.handoverRecalcWarning")}
                          </p>
                        </div>
                        <ul className="mt-2 space-y-1 pl-6">
                          {moves.map((m) => (
                            <li key={m.name} className="text-xs text-warning-500">
                              {tf("schedules.handoverRecalcRow", {
                                name: m.name,
                                from: m.from.toLocaleString(dateLocale),
                                to: m.to.toLocaleString(dateLocale),
                              })}
                            </li>
                          ))}
                        </ul>
                      </div>
                    );
                  })()}
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

          <TabsContent value="overrides" className="space-y-6">
            <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
              <h3 style={{ fontSize: "1.125rem", fontWeight: 600, marginBottom: "0.25rem" }}>
                {t("schedules.overridesTitle")}
              </h3>
              <p style={{ fontSize: "0.875rem", color: "#94A3B8", marginBottom: "1.25rem" }}>
                {t("schedules.overridesSubtitle")}
              </p>

              {(schedule?.overrides ?? []).length === 0 ? (
                <p style={{ fontSize: "0.875rem", color: "#94A3B8" }}>{t("schedules.overridesEmpty")}</p>
              ) : (
                <div className="space-y-3">
                  {(schedule?.overrides ?? []).map((o) => (
                    <div
                      key={o.id}
                      className="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-border bg-surface-light/10 p-4"
                    >
                      <div className="min-w-0">
                        <div className="flex flex-wrap items-center gap-2">
                          <span style={{ fontSize: "0.9375rem", fontWeight: 600 }}>
                            {o.overrideUserName ?? o.overrideUserId}
                          </span>
                          {o.isActive && (
                            <Badge className="border border-success-500/20 bg-success-500/10 text-success-500 text-xs">
                              {t("schedules.overrideActive")}
                            </Badge>
                          )}
                          {o.originalUserName && (
                            <span style={{ fontSize: "0.8125rem", color: "#94A3B8" }}>
                              {t("schedules.overrideReplaces", { name: o.originalUserName })}
                            </span>
                          )}
                        </div>
                        <p style={{ fontSize: "0.8125rem", color: "#94A3B8", marginTop: "0.25rem" }}>
                          {formatForDisplay(o.startUtc, scheduleTimezone)} — {formatForDisplay(o.endUtc, scheduleTimezone)}
                          {" "}({zoneAbbreviation(o.startUtc, scheduleTimezone)})
                        </p>
                        {o.reason && (
                          <p style={{ fontSize: "0.8125rem", color: "#64748B", marginTop: "0.25rem" }}>{o.reason}</p>
                        )}
                      </div>
                      <Button
                        variant="outline"
                        size="sm"
                        className="bg-input-background hover:bg-error-500/10 hover:text-error-500"
                        aria-label={t("schedules.ariaCancelOverride", { name: o.overrideUserName ?? "" })}
                        onClick={() => setCancellingOverride(o)}
                      >
                        {t("schedules.cancelOverride")}
                      </Button>
                    </div>
                  ))}
                </div>
              )}
            </Card>
          </TabsContent>
        </Tabs>

        <DeleteConfirmDialog
          open={!!cancellingOverride}
          onOpenChange={(open) => !open && setCancellingOverride(null)}
          title={t("schedules.cancelOverrideTitle")}
          message={t("schedules.cancelOverrideMsg", { name: cancellingOverride?.overrideUserName ?? "" })}
          warning={t("schedules.cancelOverrideWarn")}
          confirmLabel={t("schedules.cancelOverride")}
          isLoading={deleteOverrideMutation.isPending}
          onConfirm={() => {
            if (!cancellingOverride) return;
            deleteOverrideMutation.mutate(cancellingOverride.id, {
              onSettled: () => setCancellingOverride(null),
            });
          }}
        />

        <Dialog open={isHandoverConfirmOpen} onOpenChange={setIsHandoverConfirmOpen}>
          <DialogContent className="bg-card border-border sm:max-w-[560px]">
            <DialogHeader>
              <DialogTitle style={{ fontSize: "1.25rem", fontWeight: 600 }}>
                {tf("schedules.handoverConfirmTitle")}
              </DialogTitle>
            </DialogHeader>
            <div className="py-2">
              <div className="flex gap-3">
                <AlertCircle className="w-5 h-5 text-warning-500 flex-shrink-0 mt-0.5" />
                <p style={{ fontSize: "0.875rem" }}>{tf("schedules.handoverConfirmBody")}</p>
              </div>
              <ul className="mt-3 space-y-1 pl-8">
                {pendingHandoverMoves().map((m) => (
                  <li key={m.name} style={{ fontSize: "0.8125rem", color: "#94A3B8" }}>
                    {tf("schedules.handoverRecalcRow", {
                      name: m.name,
                      from: m.from.toLocaleString(dateLocale),
                      to: m.to.toLocaleString(dateLocale),
                    })}
                  </li>
                ))}
              </ul>
            </div>
            <DialogFooter>
              <Button
                variant="outline"
                onClick={() => setIsHandoverConfirmOpen(false)}
                className="bg-input-background"
              >
                {t("common.cancel")}
              </Button>
              <Button
                onClick={() => {
                  setIsHandoverConfirmOpen(false);
                  void performSave();
                }}
                disabled={isSaving}
                className="bg-brand-500 hover:bg-brand-600 text-white"
              >
                {tf("schedules.handoverConfirmSave")}
              </Button>
            </DialogFooter>
          </DialogContent>
        </Dialog>

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

/** Lists materialized occurrences in the viewer's chosen zone, suffixed with the zone abbreviation so
 * cross-zone teams see both their local time and the schedule's own zone. */
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