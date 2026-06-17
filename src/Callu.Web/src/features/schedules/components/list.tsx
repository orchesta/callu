import { useState } from "react";
import { t } from "@/shared/locales/i18n";
import { localDateTimeToUtcInstant } from "@/shared/lib/timezone";
import { Link, useNavigate } from "react-router";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
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
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";
import {
  Plus,
  Calendar,
  Clock,
  Users,
  Edit,
  Trash2,
  AlertCircle,
  RotateCw,
  Shield,
} from "lucide-react";
import { LoadingState } from "@/shared/components/loading-state";
import { ErrorState } from "@/shared/components/error-state";
import { EmptyState } from "@/shared/components/empty-state";
import { StatCard } from "@/shared/components/stat-card";
import { PageHeader } from "@/shared/components/page-header";
import { SearchInput } from "@/shared/components/search-input";
import { DeleteConfirmDialog } from "@/shared/components/delete-confirm-dialog";
import {
  useSchedules,
  useDeleteSchedule,
  useCreateOverride,
} from "../hooks/use-schedules";
import type { ScheduleDto } from "../types/schedule.types";
import { useUsers } from "@/features/users/hooks/use-users";

const AVATAR_COLORS = ["#3E7BFA", "#22C55E", "#FB923C", "#A855F7", "#EC4899", "#EF4444", "#14B8A6", "#F59E0B"];

function getInitials(displayName?: string, email?: string): string {
  if (displayName) {
    return displayName.split(" ").map(w => w[0]).join("").toUpperCase().slice(0, 2);
  }
  return (email ?? "?")[0].toUpperCase();
}

export function SchedulesList() {
  const navigate = useNavigate();
  const [searchQuery, setSearchQuery] = useState("");
  const [isOverrideModalOpen, setIsOverrideModalOpen] = useState(false);
  const [selectedSchedule, setSelectedSchedule] = useState<ScheduleDto | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<ScheduleDto | null>(null);
  const [overrideData, setOverrideData] = useState({
    userId: "",
    startDate: "",
    startTime: "",
    duration: "4",
    reason: "",
  });

  const { data: schedules = [], isLoading, error } = useSchedules();
  const deleteSchedule = useDeleteSchedule();
  const createOverride = useCreateOverride();
  const { data: apiUsers = [] } = useUsers();

  const userList = apiUsers.map((u, i) => ({
    id: u.id,
    name: u.displayName || `${u.firstName ?? ""} ${u.lastName ?? ""}`.trim() || u.email,
    initials: u.initials || getInitials(u.displayName, u.email),
    phone: u.phoneNumber || "",
    color: AVATAR_COLORS[i % AVATAR_COLORS.length],
  }));

  const filteredSchedules = schedules.filter((schedule) =>
    schedule.name.toLowerCase().includes(searchQuery.toLowerCase())
  );

  const handleCreateOverride = (schedule: ScheduleDto) => {
    setSelectedSchedule(schedule);
    setIsOverrideModalOpen(true);
    setOverrideData({ userId: "", startDate: "", startTime: "", duration: "4", reason: "" });
  };

  const handleSubmitOverride = async () => {
    if (!selectedSchedule || !overrideData.userId || !overrideData.startDate || !overrideData.startTime) return;
    const localIso = `${overrideData.startDate}T${overrideData.startTime}:00`;
    const startDate = localDateTimeToUtcInstant(localIso, selectedSchedule.timezone);
    const endDate = new Date(startDate.getTime() + parseInt(overrideData.duration) * 60 * 60 * 1000);
    createOverride.mutate(
      {
        scheduleId: selectedSchedule.id,
        overrideUserId: overrideData.userId,
        startUtc: startDate.toISOString(),
        endUtc: endDate.toISOString(),
        reason: overrideData.reason || undefined,
      },
      { onSuccess: () => setIsOverrideModalOpen(false) }
    );
  };

  const handleDelete = () => {
    if (!deleteTarget) return;
    deleteSchedule.mutate(deleteTarget.id, {
      onSuccess: () => setDeleteTarget(null),
    });
  };

  const getCurrentDateTime = () => {
    const now = new Date();
    return {
      date: now.toISOString().split("T")[0],
      time: now.toTimeString().slice(0, 5),
    };
  };

  if (isLoading) {
    return <LoadingState message={t("schedules.loading")} />;
  }

  if (error) {
    return (
      <ErrorState
        title={t("schedules.loadFailed")}
        message={error instanceof Error ? error.message : t("common.errorOccurred")}
      />
    );
  }

  return (
    <>
      <div className="p-6 space-y-6">
        <PageHeader
          title={t("schedules.title")}
          subtitle={t("schedules.subtitle")}
          action={
            <Button onClick={() => navigate("/schedules/new")} className="bg-brand-500 hover:bg-brand-600 text-white shadow-lg shadow-brand-500/20">
              <Plus className="w-4 h-4 mr-2" />
              {t("schedules.createSchedule")}
            </Button>
          }
        />

        <SearchInput
          placeholder={t("schedules.searchSchedules")}
          value={searchQuery}
          onChange={setSearchQuery}
        />

        <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
          <StatCard label={t("schedules.totalSchedules")} value={filteredSchedules.length} />
          <StatCard label={t("schedules.activeOnCall")} value={filteredSchedules.filter((s) => s.currentOnCallUser).length} color="#22C55E" borderColor="border-success-500/20" />
          <StatCard label={t("schedules.totalRotations")} value={filteredSchedules.reduce((sum, s) => sum + s.rotationCount, 0)} color="#FB923C" borderColor="border-warning-500/20" />
          <StatCard label={t("schedules.avgRotations")} value={filteredSchedules.length > 0 ? (filteredSchedules.reduce((sum, s) => sum + s.rotationCount, 0) / filteredSchedules.length).toFixed(1) : "0"} color="#3E7BFA" borderColor="border-brand-500/20" />
        </div>

        {filteredSchedules.length > 0 ? (
          <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
            {filteredSchedules.map((schedule) => (
              <Card
                key={schedule.id}
                className="p-6 bg-card/80 backdrop-blur-sm border-border hover:border-border-light transition-all hover:shadow-lg"
              >
                <div className="flex items-start justify-between gap-3 mb-5">
                  <div className="flex items-start gap-3 flex-1 min-w-0">
                    <div className="w-10 h-10 rounded-lg bg-brand-500/10 flex items-center justify-center flex-shrink-0">
                      <Calendar className="w-5 h-5 text-brand-500" />
                    </div>
                    <div className="flex-1 min-w-0">
                      <Link
                        to={`/schedules/${schedule.id}`}
                        className="font-semibold hover:text-brand-500 transition-colors block truncate"
                        style={{ fontSize: "1.0625rem" }}
                      >
                        {schedule.name}
                      </Link>
                      <div className="flex items-center gap-2 mt-1">
                        {schedule.teamName && (
                          <Badge className="bg-brand-500/10 text-brand-500 border-brand-500/20 border text-xs">
                            <Users className="w-3 h-3 mr-1" />
                            {schedule.teamName}
                          </Badge>
                        )}
                        <Badge className="bg-muted/20 text-muted-foreground border-border border text-xs">
                          {schedule.timezone}
                        </Badge>
                      </div>
                    </div>
                  </div>
                </div>

                <div className="mb-5 p-4 rounded-lg bg-gradient-to-br from-success-500/10 to-transparent border-2 border-success-500/20">
                  <div className="flex items-center gap-2 mb-3">
                    <div className="w-2 h-2 bg-success-500 rounded-full animate-pulse" />
                    <p
                      style={{
                        fontSize: "0.75rem",
                        color: "#22C55E",
                        fontWeight: 600,
                        letterSpacing: "0.05em",
                      }}
                    >
                      {t("schedules.currentlyOnCallLabel")}
                    </p>
                  </div>
                  <div className="flex items-center gap-3">
                    <div className="w-12 h-12 rounded-full flex items-center justify-center text-white font-bold flex-shrink-0 bg-brand-500">
                      {schedule.currentOnCallUser
                        ? schedule.currentOnCallUser.split(" ").map(w => w[0]).join("").slice(0, 2)
                        : "—"}
                    </div>
                    <div className="flex-1 min-w-0">
                      <p style={{ fontSize: "1.0625rem", fontWeight: 600 }}>
                        {schedule.currentOnCallUser || t("schedules.noOneOnCall")}
                      </p>
                    </div>
                    <Button
                      size="sm"
                      variant="outline"
                      onClick={() => handleCreateOverride(schedule)}
                      className="bg-input-background flex-shrink-0"
                    >
                      <Shield className="w-4 h-4 mr-2" />
                      {t("schedules.override")}
                    </Button>
                  </div>
                </div>

                <div className="grid grid-cols-2 gap-3 mb-4">
                  <div className="p-3 rounded-lg bg-surface-light/20">
                    <div className="flex items-center gap-2 mb-1">
                      <RotateCw className="w-4 h-4 text-muted-foreground" />
                      <p style={{ fontSize: "0.75rem", color: "#94A3B8" }}>
                        {t("schedules.rotations")}
                      </p>
                    </div>
                    <p style={{ fontSize: "0.9375rem", fontWeight: 600 }}>
                      {schedule.rotationCount}
                    </p>
                  </div>
                  <div className="p-3 rounded-lg bg-surface-light/20">
                    <div className="flex items-center gap-2 mb-1">
                      <Clock className="w-4 h-4 text-muted-foreground" />
                      <p style={{ fontSize: "0.75rem", color: "#94A3B8" }}>
                        {t("schedules.created")}
                      </p>
                    </div>
                    <p style={{ fontSize: "0.9375rem", fontWeight: 600 }}>
                      {new Date(schedule.createdAt).toLocaleDateString()}
                    </p>
                  </div>
                </div>

                {schedule.description && (
                  <div className="mb-4">
                    <p
                      style={{ fontSize: "0.875rem", color: "#94A3B8" }}
                      className="line-clamp-2"
                    >
                      {schedule.description}
                    </p>
                  </div>
                )}

                <div className="flex gap-2 pt-4 border-t border-border">
                  <Link to={`/schedules/${schedule.id}`} className="flex-1">
                    <Button variant="outline" className="w-full bg-input-background">
                      <Edit className="w-4 h-4 mr-2" />
                      {t("schedules.editSchedule")}
                    </Button>
                  </Link>
                  <Button
                    variant="outline"
                    size="sm"
                    className="bg-input-background hover:bg-error-500/10 hover:text-error-500"
                    onClick={() => setDeleteTarget(schedule)}
                  >
                    <Trash2 className="w-4 h-4" />
                  </Button>
                </div>
              </Card>
            ))}
          </div>
        ) : (
          <EmptyState
            icon={Calendar}
            title={t("schedules.noSchedulesFound")}
            description={searchQuery ? t("schedules.adjustSearch") : t("schedules.createFirstSchedule")}
            action={
              <Button onClick={() => navigate("/schedules/new")} className="bg-brand-500 hover:bg-brand-600">
                <Plus className="w-4 h-4 mr-2" />
                {t("schedules.createSchedule")}
              </Button>
            }
          />
        )}
      </div>

      <Dialog open={isOverrideModalOpen} onOpenChange={setIsOverrideModalOpen}>
        <DialogContent className="bg-card border-border sm:max-w-[600px]">
          <DialogHeader>
            <DialogTitle style={{ fontSize: "1.5rem", fontWeight: 600 }}>
              {t("schedules.createOverrideTitle")}
            </DialogTitle>
            <DialogDescription style={{ fontSize: "0.875rem", color: "#94A3B8" }}>
              {t("schedules.overrideDesc").replace("{name}", selectedSchedule?.name ?? "")}
            </DialogDescription>
          </DialogHeader>

          <div className="space-y-5 py-4">
            {selectedSchedule?.currentOnCallUser && (
              <div className="p-4 rounded-lg bg-muted/10 border border-border">
                <p style={{ fontSize: "0.75rem", color: "#94A3B8", marginBottom: "0.5rem" }}>
                  {t("schedules.currentlyOnCallModal")}
                </p>
                <div className="flex items-center gap-3">
                  <div className="w-10 h-10 rounded-full flex items-center justify-center text-white font-bold bg-brand-500">
                    {selectedSchedule.currentOnCallUser.split(" ").map(w => w[0]).join("").slice(0, 2)}
                  </div>
                  <p style={{ fontSize: "0.9375rem", fontWeight: 600 }}>
                    {selectedSchedule.currentOnCallUser}
                  </p>
                </div>
              </div>
            )}

            <div>
              <label
                style={{
                  fontSize: "0.875rem",
                  fontWeight: 600,
                  marginBottom: "0.5rem",
                  display: "block",
                }}
              >
                {t("schedules.overrideWith")} <span className="text-error-500">*</span>
              </label>
              <Select
                value={overrideData.userId}
                onValueChange={(value) => setOverrideData({ ...overrideData, userId: value })}
              >
                <SelectTrigger className="bg-input-background">
                  <SelectValue placeholder={t("schedules.selectUser")} />
                </SelectTrigger>
                <SelectContent>
                  {userList.filter((member) => member.id).map((member) => (
                    <SelectItem key={member.id} value={member.id}>
                      <div className="flex items-center gap-2">
                        <div
                          className="w-6 h-6 rounded-full flex items-center justify-center text-white text-xs font-bold"
                          style={{ backgroundColor: member.color }}
                        >
                          {member.initials}
                        </div>
                        <span>{member.name}</span>
                      </div>
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>

            <div className="grid grid-cols-2 gap-4">
              <div>
                <label
                  style={{
                    fontSize: "0.875rem",
                    fontWeight: 600,
                    marginBottom: "0.5rem",
                    display: "block",
                  }}
                >
                  {t("schedules.startDate")} <span className="text-error-500">*</span>
                </label>
                <Input
                  type="date"
                  value={overrideData.startDate}
                  onChange={(e) => setOverrideData({ ...overrideData, startDate: e.target.value })}
                  className="bg-input-background"
                  min={getCurrentDateTime().date}
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
                  {t("schedules.startTime")} <span className="text-error-500">*</span>
                </label>
                <Input
                  type="time"
                  value={overrideData.startTime}
                  onChange={(e) => setOverrideData({ ...overrideData, startTime: e.target.value })}
                  className="bg-input-background"
                />
              </div>
            </div>

            {selectedSchedule?.timezone && overrideData.startDate && overrideData.startTime && (() => {
              const local = `${overrideData.startDate}T${overrideData.startTime}`;
              let utcIso: string | null = null;
              try {
                const utc = localDateTimeToUtcInstant(local, selectedSchedule.timezone);
                utcIso = utc ? utc.toISOString().slice(0, 16).replace('T', ' ') + ' UTC' : null;
              } catch { /* empty */ }
              const tzAbbr = (() => {
                try {
                  const fmt = new Intl.DateTimeFormat('en-US', {
                    timeZone: selectedSchedule.timezone,
                    timeZoneName: 'short',
                  });
                  const parts = fmt.formatToParts(new Date(local));
                  return parts.find((p) => p.type === 'timeZoneName')?.value ?? '';
                } catch { return ''; }
              })();
              return (
                <p className="text-xs text-muted-foreground">
                  <span className="font-mono">{overrideData.startTime}</span>{' '}
                  {tzAbbr && <span className="font-mono">{tzAbbr}</span>}{' '}
                  ({selectedSchedule.timezone})
                  {utcIso && <> <span className="text-foreground/70">→ {utcIso}</span></>}
                </p>
              );
            })()}
            {selectedSchedule?.timezone && (!overrideData.startDate || !overrideData.startTime) && (
              <p className="text-xs text-muted-foreground">
                Times are interpreted in the schedule's timezone:{" "}
                <span className="font-mono">{selectedSchedule.timezone}</span>
              </p>
            )}

            <div>
              <label
                style={{
                  fontSize: "0.875rem",
                  fontWeight: 600,
                  marginBottom: "0.5rem",
                  display: "block",
                }}
              >
                {t("schedules.durationLabel")} <span className="text-error-500">*</span>
              </label>
              <Select
                value={overrideData.duration}
                onValueChange={(value) => setOverrideData({ ...overrideData, duration: value })}
              >
                <SelectTrigger className="bg-input-background">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="2">{t("schedules.hours2")}</SelectItem>
                  <SelectItem value="4">{t("schedules.hours4")}</SelectItem>
                  <SelectItem value="8">{t("schedules.hours8")}</SelectItem>
                  <SelectItem value="12">{t("schedules.hours12")}</SelectItem>
                  <SelectItem value="24">{t("schedules.hours24")}</SelectItem>
                  <SelectItem value="48">{t("schedules.hours48")}</SelectItem>
                  <SelectItem value="168">{t("schedules.week1")}</SelectItem>
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
                {t("schedules.reasonOptional")}
              </label>
              <Input
                placeholder={t("schedules.reasonPlaceholder")}
                value={overrideData.reason}
                onChange={(e) => setOverrideData({ ...overrideData, reason: e.target.value })}
                className="bg-input-background"
              />
            </div>

            <div className="p-4 rounded-lg bg-brand-500/5 border border-brand-500/20 flex gap-3">
              <AlertCircle className="w-5 h-5 text-brand-500 flex-shrink-0 mt-0.5" />
              <div>
                <p style={{ fontSize: "0.8125rem", color: "#94A3B8" }}>
                  {t("schedules.overrideNote")}
                </p>
              </div>
            </div>
          </div>

          <DialogFooter>
            <Button
              variant="outline"
              onClick={() => setIsOverrideModalOpen(false)}
              disabled={createOverride.isPending}
              className="bg-input-background"
            >
              {t("common.cancel")}
            </Button>
            <Button
              onClick={handleSubmitOverride}
              disabled={
                !overrideData.userId ||
                !overrideData.startDate ||
                !overrideData.startTime ||
                createOverride.isPending
              }
              className="bg-brand-500 hover:bg-brand-600 text-white"
            >
              {createOverride.isPending ? (
                <>
                  <div className="w-4 h-4 border-2 border-white/30 border-t-white rounded-full animate-spin mr-2" />
                  {t("schedules.creatingOverride")}
                </>
              ) : (
                <>
                  <Shield className="w-4 h-4 mr-2" />
                  {t("schedules.createOverrideTitle")}
                </>
              )}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <DeleteConfirmDialog
        open={!!deleteTarget}
        onOpenChange={(open) => !open && setDeleteTarget(null)}
        title={t("schedules.deleteScheduleTitle")}
        message={t("schedules.deleteScheduleMsg").replace("{name}", deleteTarget?.name ?? "")}
        warning={t("schedules.deleteScheduleWarn")}
        onConfirm={handleDelete}
        isLoading={deleteSchedule.isPending}
        confirmLabel={t("schedules.deleteSchedule")}
        cancelLabel={t("common.cancel")}
      />
    </>
  );
}