import { useState } from "react";
import { Link, useNavigate } from "react-router";
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
} from "@/shared/components/ui/dialog";
import {
  Video,
  Clock,
  Play,
  RotateCcw,
  ChevronLeft,
  ChevronRight,
  ExternalLink,
  PhoneOff,
} from "lucide-react";
import { LoadingState } from "@/shared/components/loading-state";
import { ErrorState } from "@/shared/components/error-state";
import { EmptyState } from "@/shared/components/empty-state";
import { PageHeader } from "@/shared/components/page-header";
import { useConferences } from "../hooks/use-conferences";
import { useEndConference } from "@/features/conference/hooks/use-conference";
import { DeleteConfirmDialog } from "@/shared/components/delete-confirm-dialog";
import { useAuth } from "@/shared/auth/auth.context";
import { canManageIncidents } from "@/shared/auth";
import { conferenceApi } from "../api/conference.api";
import { formatDateTime, getTimeAgo } from "@/shared/utils/time";
import type { ConferenceRoomDto, ConferenceRoomFilter } from "../types/conference.types";
import { toast, handleApiError } from "@/shared/utils/toast";
import { t } from "@/shared/locales/i18n";

function getStatusBadgeClass(status: string) {
  const s = status.toLowerCase();
  if (s === "active") return "bg-brand-500/10 text-brand-500 border-brand-500/20";
  if (s === "ended") return "bg-success-500/10 text-success-500 border-success-500/20";
  if (s === "expired") return "bg-warning-500/10 text-warning-500 border-warning-500/20";
  return "bg-muted/10 text-muted-foreground border-muted/20";
}

export function ConferenceList() {
  const [filters, setFilters] = useState<ConferenceRoomFilter>({
    page: 1,
    pageSize: 25,
  });

  const { data, isLoading, error, refetch } = useConferences(filters);
  const [playingVideo, setPlayingVideo] = useState<string | null>(null);
  const [joiningId, setJoiningId] = useState<string | null>(null);
  const [endingRoom, setEndingRoom] = useState<ConferenceRoomDto | null>(null);
  const endConference = useEndConference();
  const { user } = useAuth();
  const canManage = canManageIncidents(user?.role);
  const [searchQuery, setSearchQuery] = useState("");
  const navigate = useNavigate();

  // Client-side filter over the loaded page only — the API has no free-text search.
  const visibleRooms = (data?.items ?? []).filter(
    (room) => !searchQuery || room.incidentTitle?.toLowerCase().includes(searchQuery.toLowerCase()),
  );

  // Resolve the current user's participant token for the room's incident (creating one if
  // needed) and jump straight into the live conference — same path the incident page uses.
  const handleJoin = async (room: ConferenceRoomDto) => {
    setJoiningId(room.id);
    try {
      const res = await conferenceApi.getJoinInfo(room.incidentId);
      const token = res.data?.userParticipantToken;
      if (token) {
        navigate(`/conference/${token}`);
      } else {
        toast.error(t("conferences.joinFailed"), t("conferences.joinEnded"));
        refetch();
      }
    } catch (err) {
      handleApiError(err, t("conferences.joinFailed"));
    } finally {
      setJoiningId(null);
    }
  };

  const handlePageChange = (newPage: number) => {
    setFilters((prev) => ({ ...prev, page: newPage }));
  };

  const handleStatusChange = (status: string) => {
    setFilters((prev) => ({
      ...prev,
      status: status === "all" ? undefined : status,
      page: 1,
    }));
  };

  const totalPages = data ? Math.ceil(data.totalCount / (filters.pageSize || 25)) : 0;

  return (
    <div className="p-6 space-y-6">
      <PageHeader
        title={t("conferences.title")}
        subtitle={t("conferences.subtitle")}
        action={
          <Button type="button" onClick={() => refetch()} variant="outline">
            <RotateCcw className="w-4 h-4 mr-2" />
            {t("conferences.refresh")}
          </Button>
        }
      />

      <Card className="p-4 border-border bg-card">
        <div className="flex flex-col sm:flex-row gap-4">
          <div className="flex-1">
            <Input
              type="text"
              placeholder={t("conferences.searchPlaceholder")}
              className="w-full bg-input-background"
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
            />
          </div>
          <div className="w-full sm:w-48">
            <Select value={filters.status ?? "all"} onValueChange={handleStatusChange}>
              <SelectTrigger aria-label={t("conferences.statusPlaceholder")}>
                <SelectValue placeholder={t("conferences.statusPlaceholder")} />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="all">{t("conferences.allStatuses")}</SelectItem>
                <SelectItem value="Active">{t("conferences.statusActive")}</SelectItem>
                <SelectItem value="Ended">{t("conferences.statusEnded")}</SelectItem>
                <SelectItem value="Expired">{t("conferences.statusExpired")}</SelectItem>
              </SelectContent>
            </Select>
          </div>
        </div>
      </Card>

      {isLoading ? (
        <LoadingState message={t("conferences.loadingRecords")} />
      ) : error ? (
        <ErrorState
          title={t("conferences.loadFailed")}
          message={error instanceof Error ? error.message : t("common.unexpectedError")}
          onRetry={() => refetch()}
        />
      ) : !data || data.items.length === 0 ? (
        <EmptyState
          icon={Video}
          title={t("conferences.emptyTitle")}
          description={t("conferences.emptyDesc")}
          action={
            <Button type="button" onClick={() => setFilters({ page: 1, pageSize: 25 })} variant="outline">
              {t("conferences.clearFilters")}
            </Button>
          }
        />
      ) : (
        <Card className="overflow-hidden border-border bg-card">
          <div className="overflow-x-auto">
            <table className="w-full text-sm text-left">
              <thead className="bg-muted/50 text-muted-foreground uppercase py-3 outline outline-1 outline-border">
                <tr>
                  <th className="px-4 py-3 font-medium">{t("conferences.colStatus")}</th>
                  <th className="px-4 py-3 font-medium">{t("conferences.colIncident")}</th>
                  <th className="px-4 py-3 font-medium">{t("conferences.colStartedAt")}</th>
                  <th className="px-4 py-3 font-medium">{t("conferences.colActivity")}</th>
                  <th className="px-4 py-3 font-medium text-right">{t("conferences.colRecording")}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {visibleRooms.length === 0 && (
                  <tr>
                    <td colSpan={5} className="px-4 py-6 text-center text-sm text-muted-foreground">
                      {t("conferences.noMatchesOnPage")}
                    </td>
                  </tr>
                )}
                {visibleRooms.map((room) => (
                  <tr key={room.id} className="hover:bg-muted/30 transition-colors">
                    <td className="px-4 py-3">
                      <Badge variant="outline" className={getStatusBadgeClass(room.status)}>
                        {room.status}
                      </Badge>
                    </td>
                    <td className="px-4 py-3">
                      <Link
                        to={`/incidents/${room.incidentId}`}
                        className="font-medium text-foreground hover:text-brand-500 hover:underline flex items-center gap-1"
                      >
                        {room.incidentTitle}
                        <ExternalLink className="w-3 h-3 opacity-50" />
                      </Link>
                    </td>
                    <td className="px-4 py-3">
                      <div className="flex flex-col">
                        <span>{formatDateTime(room.createdAt)}</span>
                        <span className="text-xs text-muted-foreground">{getTimeAgo(room.createdAt)}</span>
                      </div>
                    </td>
                    <td className="px-4 py-3">
                      <div className="flex items-center gap-2">
                        <Badge variant="secondary" className="bg-secondary/50 text-secondary-foreground font-mono">
                          {room.participantCount}{" "}
                          {room.participantCount === 1 ? t("conferences.user") : t("conferences.users")}
                        </Badge>
                        {room.endedAt && (
                          <div
                            className="flex items-center text-muted-foreground text-xs"
                            title={t("conferences.endedAtTitle", { time: formatDateTime(room.endedAt) })}
                          >
                            <Clock className="w-3 h-3 mr-1" />
                            {getTimeAgo(room.endedAt)}
                          </div>
                        )}
                      </div>
                    </td>
                    <td className="px-4 py-3 text-right">
                      <div className="flex items-center justify-end gap-2">
                        {room.status === "Active" && (
                          <Button
                            type="button"
                            size="sm"
                            className="bg-brand-500 text-white hover:bg-brand-600"
                            onClick={() => handleJoin(room)}
                            disabled={joiningId === room.id}
                          >
                            <Video className="w-4 h-4 mr-2" />
                            {joiningId === room.id ? t("conferences.joining") : t("conferences.join")}
                          </Button>
                        )}
                        {room.status === "Active" && canManage && (
                          <Button
                            type="button"
                            size="sm"
                            variant="outline"
                            className="bg-input-background hover:bg-error-500/10 hover:text-error-500"
                            aria-label={t("conferences.endAria").replace("{title}", room.incidentTitle ?? "")}
                            onClick={() => setEndingRoom(room)}
                          >
                            <PhoneOff className="w-4 h-4 mr-2" />
                            {t("conferences.end")}
                          </Button>
                        )}
                        {room.recordingUrl ? (
                          <Button
                            type="button"
                            variant="outline"
                            size="sm"
                            className="text-brand-500 border-brand-500/30 hover:bg-brand-500/10"
                            onClick={() => setPlayingVideo(room.recordingUrl!)}
                          >
                            <Play className="w-4 h-4 mr-2" />
                            {t("conferences.watch")}
                          </Button>
                        ) : room.recordingEnabled && room.status === "Active" ? (
                          <span className="text-xs text-brand-500 animate-pulse flex items-center justify-end gap-1">
                            <div className="w-2 h-2 rounded-full bg-brand-500" />
                            {t("conferences.recording")}
                          </span>
                        ) : !room.recordingUrl && room.status !== "Active" ? (
                          <span className="text-xs text-muted-foreground">{t("conferences.noRecording")}</span>
                        ) : null}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {totalPages > 1 && (
            <div className="p-4 border-t border-border flex items-center justify-between">
              <span className="text-sm text-muted-foreground">
                {t("conferences.paginationRange", {
                  from: String(((filters.page || 1) - 1) * (filters.pageSize || 25) + 1),
                  to: String(Math.min((filters.page || 1) * (filters.pageSize || 25), data.totalCount)),
                  total: String(data.totalCount),
                })}
              </span>
              <div className="flex gap-2">
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={() => handlePageChange((filters.page || 1) - 1)}
                  disabled={(filters.page || 1) === 1}
                >
                  <ChevronLeft className="w-4 h-4" />
                </Button>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={() => handlePageChange((filters.page || 1) + 1)}
                  disabled={(filters.page || 1) === totalPages}
                >
                  <ChevronRight className="w-4 h-4" />
                </Button>
              </div>
            </div>
          )}
        </Card>
      )}

      <Dialog open={!!playingVideo} onOpenChange={(open) => !open && setPlayingVideo(null)}>
        <DialogContent className="sm:max-w-4xl bg-card border-border p-0 overflow-hidden">
          <DialogHeader className="p-4 bg-muted/30 border-b border-border">
            <DialogTitle className="flex items-center gap-2">
              <Video className="w-5 h-5 text-brand-500" />
              {t("conferences.dialogRecordingTitle")}
            </DialogTitle>
          </DialogHeader>
          <div className="aspect-video bg-black w-full relative group flex items-center justify-center">
            {playingVideo ? (
              <video controls autoPlay className="w-full h-full object-contain" src={playingVideo} />
            ) : null}
          </div>
        </DialogContent>
      </Dialog>

      <DeleteConfirmDialog
        open={!!endingRoom}
        onOpenChange={(open) => !open && setEndingRoom(null)}
        title={t("conferences.endTitle")}
        message={t("conferences.endMsg").replace("{title}", endingRoom?.incidentTitle ?? "")}
        warning={t("conferences.endWarn")}
        confirmLabel={t("conferences.end")}
        isLoading={endConference.isPending}
        onConfirm={() => {
          if (!endingRoom) return;
          endConference.mutate(endingRoom.id, {
            onSettled: () => { setEndingRoom(null); refetch(); },
          });
        }}
      />
    </div>
  );
}
