import { useState, useId } from "react";
import { Link, useParams } from "react-router";
import { Button } from "@/shared/components/ui/button";
import { Badge } from "@/shared/components/ui/badge";
import { Card } from "@/shared/components/ui/card";
import { Avatar, AvatarFallback } from "@/shared/components/ui/avatar";
import { Textarea } from "@/shared/components/ui/textarea";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/shared/components/ui/dialog";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";
import {
  AlertTriangle,
  CheckCircle,
  Clock,
  TrendingUp,
  Server,
  MessageSquare,
  ExternalLink,
  ChevronRight,
  Home,
  Phone,
  Info,
  Zap,
  Video,
  FileText,
  BookOpen,
  AlertCircle,
  UserCog,
  Pencil,
  Trash2,
  Search,
  Shield,
  ScrollText,
} from "lucide-react";
import {
  useIncident,
  useIncidentTimeline,
  useIncidentNotes,
  useAcknowledgeIncident,
  useInvestigateIncident,
  useMitigateIncident,
  useResolveIncident,
  useCloseIncident,
  useReopenIncident,
  useEscalateIncident,
  useAddNote,
  useIncidentConference,
  useWebhookDeliveries,
  useReassignIncident,
  useUpdateNote,
  useDeleteNote,
} from "../hooks/use-incidents";
import { usePostmortemsByIncident } from "@/features/postmortems/hooks/use-postmortems";
import { useRunbooksByService } from "@/features/runbooks/hooks/use-runbooks";
import { useCreateConferenceRoom } from "@/features/conference/hooks/use-conference";
import type { IncidentNoteDto, IncidentTimelineEvent } from "../types/incident.types";
import { IncidentEscalationCard } from "./escalation-ladder";
import { LifecycleStrip } from "./lifecycle-strip";
import {
  canAcknowledgeIncidents,
  canManageIncidents,
  canResolveIncidents,
  hasPermission,
  PERMISSIONS,
} from "@/shared/auth";
import { useAuth } from "@/shared/auth/auth.context";
import { DeleteConfirmDialog } from "@/shared/components/delete-confirm-dialog";
import { useUsers } from "@/features/users/hooks/use-users";
import { getSeverityBadge, getStatusBadge } from "@/shared/utils/incident-styles";
import { formatDateTime, getTimeAgo } from "@/shared/utils/time";
import { t } from "@/shared/locales/i18n";
import { initialsFromName } from "@/features/users/utils/user-display";

const timelineIcons: Record<string, { icon: React.ComponentType<{ className?: string }>; color: string }> = {
  created: { icon: AlertTriangle, color: "text-error-500" },
  triggered: { icon: AlertTriangle, color: "text-error-500" },
  notification: { icon: Phone, color: "text-brand-500" },
  acknowledged: { icon: CheckCircle, color: "text-warning-500" },
  callacknowledged: { icon: CheckCircle, color: "text-warning-500" },
  note: { icon: MessageSquare, color: "text-blue-400" },
  resolved: { icon: CheckCircle, color: "text-success-500" },
  closed: { icon: CheckCircle, color: "text-success-500" },
  escalated: { icon: TrendingUp, color: "text-warning-500" },
  callescalated: { icon: TrendingUp, color: "text-warning-500" },
  escalationstep: { icon: TrendingUp, color: "text-orange-400" },
  assigned: { icon: Info, color: "text-brand-500" },
  reassigned: { icon: Info, color: "text-brand-500" },
  callinitiated: { icon: Phone, color: "text-brand-500" },
  callconnected: { icon: Phone, color: "text-success-500" },
  callfailed: { icon: Phone, color: "text-error-500" },
  conferencecreated: { icon: Video, color: "text-brand-500" },
};

function getTimelineIcon(type: string) {
  return timelineIcons[type.toLowerCase()] ?? { icon: Clock, color: "text-muted-foreground" };
}

type TimelineItem =
  | { kind: "event"; data: IncidentTimelineEvent }
  | { kind: "note"; data: IncidentNoteDto };

// The backend writes a NoteAdded marker event alongside every note; the note itself is
// fetched separately and rendered with its full content, so the marker would be a second
// entry for the same thing. Other NoteAdded events (e.g. "Paging suppressed") have no note
// row behind them and must stay.
function isNoteMarker(event: IncidentTimelineEvent): boolean {
  return (
    event.eventType.toLowerCase() === "noteadded" &&
    event.title.trim().toLowerCase() === "note added"
  );
}

function buildTimeline(
  events: IncidentTimelineEvent[],
  notes: IncidentNoteDto[],
): TimelineItem[] {
  const items: TimelineItem[] = [
    ...events.filter((e) => !isNoteMarker(e)).map((e) => ({ kind: "event" as const, data: e })),
    ...notes.map((n) => ({ kind: "note" as const, data: n })),
  ];
  return items.sort((a, b) => {
    const tA = a.data.createdAt;
    const tB = b.data.createdAt;
    return new Date(tB).getTime() - new Date(tA).getTime();
  });
}

export function IncidentDetail() {
  const { id = "" } = useParams();
  const formId = useId();
  const { user } = useAuth();
  const { data: incident, isLoading, error } = useIncident(id);
  const { data: timelineData } = useIncidentTimeline(id);
  const { data: notes = [] } = useIncidentNotes(id);
  const { data: webhookDeliveries = [] } = useWebhookDeliveries(id);
  const reassignIncident = useReassignIncident();
  const { data: assignableUsers = [] } = useUsers();
  const [isReassignOpen, setIsReassignOpen] = useState(false);
  const [reassignTo, setReassignTo] = useState("");
  const updateNote = useUpdateNote();
  const deleteNote = useDeleteNote();
  const [editingNoteId, setEditingNoteId] = useState<string | null>(null);
  const [editingNoteText, setEditingNoteText] = useState("");
  const [deletingNote, setDeletingNote] = useState<IncidentNoteDto | null>(null);
  const { data: conference, refetch: refetchConference } = useIncidentConference(id);
  const { data: incidentPostmortems } = usePostmortemsByIncident(id);
  const { data: relatedRunbooks = [] } = useRunbooksByService(incident?.serviceId ?? "");

  const acknowledgeIncident = useAcknowledgeIncident();
  const investigateIncident = useInvestigateIncident();
  const mitigateIncident = useMitigateIncident();
  const resolveIncident = useResolveIncident();
  const closeIncident = useCloseIncident();
  const reopenIncident = useReopenIncident();
  const escalateIncident = useEscalateIncident();
  const createConference = useCreateConferenceRoom();
  const addNote = useAddNote();
  const [newNote, setNewNote] = useState("");

  const timelineEvents = timelineData?.events ?? [];
  const timelineItems = buildTimeline(timelineEvents, notes);

  const linkedPostmortem = incidentPostmortems?.[0];

  const handleAcknowledge = () => acknowledgeIncident.mutate(id);
  const handleInvestigate = () => investigateIncident.mutate(id);
  const handleMitigate = () => mitigateIncident.mutate(id);
  const handleResolve = () => resolveIncident.mutate(id);
  const handleClose = () => closeIncident.mutate(id);
  const handleReopen = () => reopenIncident.mutate(id);
  const handleEscalate = () => escalateIncident.mutate({ id });
  // Creates the conference room AND sends the SMS invite (join link) to team members with a
  // phone number — the only path that does so. Refetch so the join card appears afterwards.
  const handleStartConference = () =>
    createConference.mutate(id, { onSuccess: () => { void refetchConference(); } });

  const handleAddNote = () => {
    if (!newNote.trim()) return;
    addNote.mutate(
      { incidentId: id, content: newNote },
      { onSuccess: () => setNewNote("") },
    );
  };

  if (isLoading) {
    return (
      <div className="p-6 flex items-center justify-center min-h-[400px]">
        <div className="w-6 h-6 border-2 border-brand-500/30 border-t-brand-500 rounded-full animate-spin" />
      </div>
    );
  }

  if (error || !incident) {
    return (
      <div className="p-6 flex items-center justify-center min-h-[400px]">
        <div className="text-center">
          <AlertCircle className="w-8 h-8 text-error-500 mx-auto mb-3" />
          <p className="text-lg font-semibold mb-2">{t("incidents.loadFailed")}</p>
          <p className="text-sm text-muted-foreground">
            {error instanceof Error ? error.message : t("common.errorOccurred")}
          </p>
          <Link to="/incidents">
            <Button variant="outline" className="mt-4 bg-input-background">
              {t("common.goBack")}
            </Button>
          </Link>
        </div>
      </div>
    );
  }

  const shortId = id.replace(/-/g, "").slice(-8).toUpperCase();

  // Mirrors the three policies the API applies to these endpoints: a Member may acknowledge,
  // resolve and close, but reopen/escalate/notes/conference need CanManageIncidents. Viewers get
  // none of them. Gating on a single flag would surface buttons the backend answers with 403.
  const canManage = canManageIncidents(user?.role);
  const canAcknowledge = canAcknowledgeIncidents(user?.role);
  const canResolve = canResolveIncidents(user?.role);
  const canViewAuditLog = hasPermission(user?.role, PERMISSIONS.ViewAuditLog);

  const isTerminal = incident.status === "Resolved" || incident.status === "Closed";
  const status = incident.status;
  // Acknowledge only accepts Open (tests still use the legacy "Triggered" label for an open page).
  const showAck =
    canAcknowledge && (status === "Open" || status === "Triggered");
  // PUT /incidents requires CanManageIncidents — same gate as reopen/escalate.
  const showInvestigate =
    canManage &&
    (status === "Open" ||
      status === "Triggered" ||
      status === "Acknowledged" ||
      status === "Mitigated");
  const showMitigate =
    canManage && (status === "Acknowledged" || status === "Investigating");
  const showResolve = canResolve && !isTerminal;
  const showClose = canResolve && incident.status === "Resolved";
  const showReopen = canManage && isTerminal;
  const showEscalate = canManage && !isTerminal;
  const showReassign = canManage && !isTerminal;
  // Offer starting a war-room only when the incident is open and no room is active yet.
  const showStartConference = showEscalate && !conference?.userParticipantToken;
  const hasActions =
    showAck ||
    showInvestigate ||
    showMitigate ||
    showResolve ||
    showClose ||
    showReopen ||
    showEscalate ||
    showStartConference;

  return (
    <div className={`p-6 space-y-6 lg:pb-6 ${hasActions ? "pb-28" : ""}`}>
      <nav className="flex items-center gap-2 text-sm">
        <Link to="/dashboard" className="text-muted-foreground hover:text-foreground transition-colors">
          <Home className="w-4 h-4" />
        </Link>
        <ChevronRight className="w-4 h-4 text-muted-foreground" />
        <Link to="/incidents" className="text-muted-foreground hover:text-foreground transition-colors">
          {t("nav.incidents")}
        </Link>
        <ChevronRight className="w-4 h-4 text-muted-foreground" />
        <span className="text-foreground font-medium">INC-{shortId}</span>
      </nav>

      <div className="flex flex-col lg:flex-row lg:items-start lg:justify-between gap-4">
        <div className="space-y-3">
          <div className="flex flex-wrap items-center gap-3">
            <h1 className="text-3xl font-semibold">{incident.title}</h1>
            <Badge className={getSeverityBadge(incident.severity)}>{incident.severity}</Badge>
            <Badge className={getStatusBadge(incident.status)}>{incident.status}</Badge>
          </div>
          <p className="text-sm text-muted-foreground">
            Incident ID: INC-{shortId} · Started {getTimeAgo(incident.startedAt)}
          </p>
          {canViewAuditLog && (
            <Link
              to={`/audit-logs/incident/${id}`}
              className="inline-flex items-center gap-1.5 text-sm text-brand-400 underline-offset-2 hover:underline"
            >
              <ScrollText className="w-4 h-4" />
              {t("incidents.auditTrail")}
            </Link>
          )}
        </div>

        <div className="hidden lg:flex flex-wrap gap-2">
          {showAck && (
            <Button
              className="bg-warning-500 hover:bg-warning-600 text-white"
              onClick={handleAcknowledge}
              disabled={acknowledgeIncident.isPending}
            >
              <CheckCircle className="w-4 h-4 mr-2" />
              {t("incidents.acknowledge")}
            </Button>
          )}
          {showInvestigate && (
            <Button
              variant="outline"
              className="bg-input-background"
              onClick={handleInvestigate}
              disabled={investigateIncident.isPending}
            >
              <Search className="w-4 h-4 mr-2" />
              {t("incidents.investigate")}
            </Button>
          )}
          {showMitigate && (
            <Button
              variant="outline"
              className="bg-input-background"
              onClick={handleMitigate}
              disabled={mitigateIncident.isPending}
            >
              <Shield className="w-4 h-4 mr-2" />
              {t("incidents.mitigate")}
            </Button>
          )}
          {showResolve && (
            <Button
              className="bg-success-500 hover:bg-success-600 text-white"
              onClick={handleResolve}
              disabled={resolveIncident.isPending}
            >
              <CheckCircle className="w-4 h-4 mr-2" />
              {t("incidents.resolve")}
            </Button>
          )}
          {showClose && (
            <Button
              variant="outline"
              className="bg-input-background"
              onClick={handleClose}
              disabled={closeIncident.isPending}
            >
              <CheckCircle className="w-4 h-4 mr-2" />
              {t("incidents.close")}
            </Button>
          )}
          {showReopen && (
            <Button
              variant="outline"
              className="bg-input-background"
              onClick={handleReopen}
              disabled={reopenIncident.isPending}
            >
              <TrendingUp className="w-4 h-4 mr-2 rotate-180" />
              {t("incidents.reopen")}
            </Button>
          )}
          {showEscalate && (
            <Button
              variant="outline"
              className="bg-input-background"
              onClick={handleEscalate}
              disabled={escalateIncident.isPending}
            >
              <TrendingUp className="w-4 h-4 mr-2" />
              {t("incidents.escalate")}
            </Button>
          )}
          {showReassign && (
            <Button
              variant="outline"
              className="bg-input-background"
              onClick={() => { setReassignTo(""); setIsReassignOpen(true); }}
            >
              <UserCog className="w-4 h-4 mr-2" />
              {t("incidents.reassign")}
            </Button>
          )}
          {showStartConference && (
            <Button
              variant="outline"
              className="bg-input-background"
              onClick={handleStartConference}
              disabled={createConference.isPending}
            >
              <Video className="w-4 h-4 mr-2" />
              {t("incidents.startConference")}
            </Button>
          )}
        </div>
      </div>

      {hasActions && (
      <div
        className="lg:hidden fixed bottom-0 left-0 right-0 z-40 border-t border-border bg-card/95 backdrop-blur-xl px-4 py-3 flex flex-wrap gap-2 justify-center"
        style={{ paddingBottom: "max(0.75rem, env(safe-area-inset-bottom))" }}
        role="region"
        aria-label={t("incidentDetail.mobileActions")}
      >
        {showAck && (
          <Button
            className="flex-1 min-w-[140px] max-w-[200px] bg-warning-500 hover:bg-warning-600 text-white"
            onClick={handleAcknowledge}
            disabled={acknowledgeIncident.isPending}
          >
            <CheckCircle className="w-4 h-4 mr-2 shrink-0" />
            {t("incidents.acknowledge")}
          </Button>
        )}
        {showInvestigate && (
          <Button
            variant="outline"
            className="flex-1 min-w-[140px] max-w-[200px] bg-input-background"
            onClick={handleInvestigate}
            disabled={investigateIncident.isPending}
          >
            <Search className="w-4 h-4 mr-2 shrink-0" />
            {t("incidents.investigate")}
          </Button>
        )}
        {showMitigate && (
          <Button
            variant="outline"
            className="flex-1 min-w-[140px] max-w-[200px] bg-input-background"
            onClick={handleMitigate}
            disabled={mitigateIncident.isPending}
          >
            <Shield className="w-4 h-4 mr-2 shrink-0" />
            {t("incidents.mitigate")}
          </Button>
        )}
        {showResolve && (
          <Button
            className="flex-1 min-w-[140px] max-w-[200px] bg-success-500 hover:bg-success-600 text-white"
            onClick={handleResolve}
            disabled={resolveIncident.isPending}
          >
            <CheckCircle className="w-4 h-4 mr-2 shrink-0" />
            {t("incidents.resolve")}
          </Button>
        )}
        {showClose && (
          <Button
            variant="outline"
            className="flex-1 min-w-[120px] max-w-[180px] bg-input-background"
            onClick={handleClose}
            disabled={closeIncident.isPending}
          >
            <CheckCircle className="w-4 h-4 mr-2 shrink-0" />
            {t("incidents.close")}
          </Button>
        )}
        {showReopen && (
          <Button
            variant="outline"
            className="flex-1 min-w-[120px] max-w-[180px] bg-input-background"
            onClick={handleReopen}
            disabled={reopenIncident.isPending}
          >
            <TrendingUp className="w-4 h-4 mr-2 rotate-180 shrink-0" />
            {t("incidents.reopen")}
          </Button>
        )}
        {showEscalate && (
          <Button
            variant="outline"
            className="flex-1 min-w-[120px] max-w-[180px] bg-input-background"
            onClick={handleEscalate}
            disabled={escalateIncident.isPending}
          >
            <TrendingUp className="w-4 h-4 mr-2 shrink-0" />
            {t("incidents.escalate")}
          </Button>
        )}
        {showStartConference && (
          <Button
            variant="outline"
            className="flex-1 min-w-[140px] max-w-[200px] bg-input-background"
            onClick={handleStartConference}
            disabled={createConference.isPending}
          >
            <Video className="w-4 h-4 mr-2 shrink-0" />
            {t("incidents.startConference")}
          </Button>
        )}
      </div>
      )}

      {conference && conference.userParticipantToken && (
        <Card className="p-4 bg-brand-500/10 border-brand-500/30 flex flex-col sm:flex-row items-center justify-between gap-4 animate-in fade-in slide-in-from-top-4 duration-500">
          <div className="flex items-center gap-3">
            <div className="w-10 h-10 rounded-full bg-brand-500/20 flex items-center justify-center flex-shrink-0">
              <Video className="w-5 h-5 text-brand-500 animate-pulse" />
            </div>
            <div>
              <p className="font-semibold text-brand-500">{t("incidents.activeConference")}</p>
              <p className="text-sm text-brand-400">
                {t("incidents.conferenceParticipants", { count: conference.participantCount })}
              </p>
            </div>
          </div>
          <Link to={`/conference/${conference.userParticipantToken}`}>
            <Button className="bg-brand-500 hover:bg-brand-600 text-white w-full sm:w-auto shadow-lg shadow-brand-500/20">
              {t("incidents.joinConference")}
            </Button>
          </Link>
        </Card>
      )}

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        <div className="lg:col-span-2 space-y-6">
          <LifecycleStrip
            status={incident.status}
            startedAt={incident.startedAt}
            acknowledgedAt={incident.acknowledgedAt}
            resolvedAt={incident.resolvedAt}
          />

          <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
            <h3 className="text-lg font-semibold mb-4">{t("incidentDetail.details")}</h3>
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div>
                <p className="text-xs text-muted-foreground font-semibold tracking-widest uppercase mb-2">
                  {t("incidentDetail.service")}
                </p>
                <div className="flex items-center gap-2">
                  <Server className="w-4 h-4 text-brand-500" />
                  {incident.serviceId ? (
                    <Link
                      to={`/services/${incident.serviceId}`}
                      className="text-sm font-medium text-brand-500 hover:text-brand-400 transition-colors"
                    >
                      {incident.serviceName ?? "Service"}
                    </Link>
                  ) : (
                    <span className="text-sm font-medium text-muted-foreground">—</span>
                  )}
                </div>
              </div>
              <div>
                <p className="text-xs text-muted-foreground font-semibold tracking-widest uppercase mb-2">
                  {t("incidentDetail.startedAt")}
                </p>
                <p className="text-sm font-medium">{formatDateTime(incident.startedAt)}</p>
              </div>
              <div>
                <p className="text-xs text-muted-foreground font-semibold tracking-widest uppercase mb-2">
                  {t("incidentDetail.acknowledgedAt")}
                </p>
                <p className="text-sm font-medium">{formatDateTime(incident.acknowledgedAt)}</p>
              </div>
              <div>
                <p className="text-xs text-muted-foreground font-semibold tracking-widest uppercase mb-2">
                  {t("incidentDetail.resolvedAt")}
                </p>
                <p className="text-sm font-medium">{formatDateTime(incident.resolvedAt)}</p>
              </div>
              {incident.acknowledgedBy && (
                <div>
                  <p className="text-xs text-muted-foreground font-semibold tracking-widest uppercase mb-2">
                    {t("incidentDetail.acknowledgedBy")}
                  </p>
                  <div className="flex items-center gap-2">
                    <Avatar className="w-6 h-6">
                      <AvatarFallback className="text-[10px] bg-brand-500/10 text-brand-500">
                        {incident.acknowledgedBy.split(" ").map((n) => n[0]).join("")}
                      </AvatarFallback>
                    </Avatar>
                    <span className="text-sm font-medium">{incident.acknowledgedBy}</span>
                  </div>
                </div>
              )}
              {incident.resolvedBy && (
                <div>
                  <p className="text-xs text-muted-foreground font-semibold tracking-widest uppercase mb-2">
                    {t("incidentDetail.resolvedBy")}
                  </p>
                  <div className="flex items-center gap-2">
                    <Avatar className="w-6 h-6">
                      <AvatarFallback className="text-[10px] bg-success-500/10 text-success-500">
                        {incident.resolvedBy.split(" ").map((n) => n[0]).join("")}
                      </AvatarFallback>
                    </Avatar>
                    <span className="text-sm font-medium">{incident.resolvedBy}</span>
                  </div>
                </div>
              )}
            </div>
          </Card>

          {incident.description && (
            <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
              <h3 className="text-lg font-semibold mb-4">{t("incidentDetail.description")}</h3>
              <div className="p-4 rounded-lg bg-muted/10 border border-border">
                <p className="text-sm whitespace-pre-wrap leading-relaxed text-foreground">
                  {incident.description}
                </p>
              </div>
            </Card>
          )}

          <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
            <h3 className="text-lg font-semibold mb-6">{t("incidentDetail.timeline")}</h3>
            <div className="relative space-y-6">
              <div className="absolute left-5 top-0 bottom-0 w-0.5 bg-border" />

              {timelineItems.length === 0 && (
                <p className="text-sm text-muted-foreground pl-14">{t("incidentDetail.noTimeline")}</p>
              )}

              {timelineItems.map((item) => {
                if (item.kind === "event") {
                  const { icon: Icon, color } = getTimelineIcon(item.data.eventType);
                  return (
                    <div key={item.data.id} className="relative flex gap-4">
                      <div className="relative z-10 w-10 h-10 rounded-full bg-surface flex items-center justify-center border-2 border-border flex-shrink-0">
                        <Icon className={`w-5 h-5 ${color}`} />
                      </div>
                      <div className="flex-1 pb-6">
                        <div className="flex items-start justify-between gap-2 mb-1">
                          <p className="text-sm font-semibold">{item.data.title}</p>
                          <span className="text-xs text-muted-foreground flex-shrink-0">
                            {getTimeAgo(item.data.createdAt)}
                          </span>
                        </div>
                        {item.data.description && (
                          <p className="text-xs text-muted-foreground">{item.data.description}</p>
                        )}
                        {item.data.actorName && (
                          <p className="text-xs text-muted-foreground mt-1">by {item.data.actorName}</p>
                        )}
                      </div>
                    </div>
                  );
                }

                const { icon: NoteIcon, color: noteColor } = getTimelineIcon("note");
                const note = item.data;
                return (
                  <div key={note.id} className="relative flex gap-4">
                    <div className="relative z-10 w-10 h-10 rounded-full bg-surface flex items-center justify-center border-2 border-border flex-shrink-0">
                      <NoteIcon className={`w-5 h-5 ${noteColor}`} />
                    </div>
                    <div className="flex-1 pb-6">
                      <div className="flex items-start justify-between gap-2 mb-1">
                        {editingNoteId === note.id ? (
                          <div className="flex-1 space-y-2">
                            <Textarea
                              value={editingNoteText}
                              onChange={(e) => setEditingNoteText(e.target.value)}
                              rows={3}
                              className="bg-input-background resize-none"
                              aria-label={t("incidents.editNoteAria")}
                            />
                            <div className="flex gap-2">
                              <Button
                                size="sm"
                                disabled={!editingNoteText.trim() || updateNote.isPending}
                                onClick={() =>
                                  updateNote.mutate(
                                    {
                                      noteId: note.id,
                                      incidentId: id,
                                      content: editingNoteText.trim(),
                                      isPinned: note.isPinned ?? false,
                                    },
                                    { onSuccess: () => setEditingNoteId(null) },
                                  )
                                }
                                className="bg-brand-500 hover:bg-brand-600 text-white"
                              >
                                {t("common.save")}
                              </Button>
                              <Button
                                size="sm"
                                variant="outline"
                                onClick={() => setEditingNoteId(null)}
                                className="bg-input-background"
                              >
                                {t("common.cancel")}
                              </Button>
                            </div>
                          </div>
                        ) : (
                          <p className="text-sm font-semibold">{note.content}</p>
                        )}
                        <div className="flex items-center gap-1 flex-shrink-0">
                          <span className="text-xs text-muted-foreground">
                            {getTimeAgo(note.createdAt)}
                          </span>
                          {canManage && editingNoteId !== note.id && (
                            <>
                              <Button
                                size="sm"
                                variant="ghost"
                                aria-label={t("incidents.editNote")}
                                onClick={() => { setEditingNoteId(note.id); setEditingNoteText(note.content); }}
                              >
                                <Pencil className="w-3 h-3" />
                              </Button>
                              <Button
                                size="sm"
                                variant="ghost"
                                className="text-error-400"
                                aria-label={t("incidents.deleteNote")}
                                onClick={() => setDeletingNote(note)}
                              >
                                <Trash2 className="w-3 h-3" />
                              </Button>
                            </>
                          )}
                        </div>
                      </div>
                      {note.createdByName && (
                        <div className="flex items-center gap-2 mt-2">
                          <Avatar className="w-6 h-6">
                            <AvatarFallback className="text-[10px] bg-brand-500/10 text-brand-500">
                              {initialsFromName(note.createdByName)}
                            </AvatarFallback>
                          </Avatar>
                          <span className="text-xs text-muted-foreground">{note.createdByName}</span>
                        </div>
                      )}
                    </div>
                  </div>
                );
              })}
            </div>

            {canManage && (
              <div className="mt-6 pt-6 border-t border-border space-y-3">
                <label className="text-sm font-semibold">{t("incidentDetail.addNote")}</label>
                <Textarea
                  placeholder={t("incidentDetail.notePlaceholder")}
                  value={newNote}
                  onChange={(e) => setNewNote(e.target.value)}
                  rows={3}
                  className="bg-input-background backdrop-blur-sm resize-none text-sm"
                />
                <Button
                  onClick={handleAddNote}
                  disabled={!newNote.trim() || addNote.isPending}
                  className="bg-brand-500 hover:bg-brand-600"
                >
                  <MessageSquare className="w-4 h-4 mr-2" />
                  {t("incidentDetail.addNote")}
                </Button>
              </div>
            )}
          </Card>
        </div>

        <div className="space-y-6">
          <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
            <h3 className="text-lg font-semibold mb-4">{t("incidentDetail.serviceHealth")}</h3>
            <div className="space-y-3">
              <div className="flex items-center justify-between">
                <span className="text-sm text-muted-foreground">{t("incidentDetail.status")}</span>
                <Badge className={getStatusBadge(incident.status)}>{incident.status}</Badge>
              </div>
              <div className="flex items-center justify-between">
                <span className="text-sm text-muted-foreground">{t("incidentDetail.severity")}</span>
                <Badge className={getSeverityBadge(incident.severity)}>{incident.severity}</Badge>
              </div>
              {incident.teamName && (
                <div className="flex items-center justify-between">
                  <span className="text-sm text-muted-foreground">{t("incidentDetail.team")}</span>
                  <span className="text-sm font-medium">{incident.teamName}</span>
                </div>
              )}
            </div>
            {incident.serviceId && (
              <Link to={`/services/${incident.serviceId}`}>
                <Button variant="outline" className="w-full mt-4 bg-input-background">
                  {t("incidentDetail.viewServiceDetails")}
                  <ExternalLink className="w-4 h-4 ml-2" />
                </Button>
              </Link>
            )}
          </Card>

          <IncidentEscalationCard incidentId={incident.id} />

          <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
            <h3 className="text-lg font-semibold mb-4 flex items-center gap-2">
              <FileText className="w-5 h-5 text-brand-500" />
              {t("incidentDetail.postmortem")}
            </h3>
            {linkedPostmortem ? (
              <div className="space-y-3">
                <div className="p-3 rounded-lg bg-brand-500/5 border border-brand-500/20">
                  <p className="text-sm font-medium truncate">{linkedPostmortem.title}</p>
                  <div className="flex items-center gap-2 mt-1">
                    <Badge className={linkedPostmortem.status === "Published"
                      ? "bg-success-500/10 text-success-500 border-success-500/20 text-xs"
                      : "bg-warning-500/10 text-warning-500 border-warning-500/20 text-xs"
                    }>
                      {linkedPostmortem.status}
                    </Badge>
                    <span className="text-xs text-muted-foreground">{getTimeAgo(linkedPostmortem.createdAt)}</span>
                  </div>
                </div>
                <Link to={`/postmortems/${linkedPostmortem.id}`}>
                  <Button variant="outline" className="w-full bg-input-background">
                    {t("incidentDetail.viewPostmortem")}
                    <ExternalLink className="w-4 h-4 ml-2" />
                  </Button>
                </Link>
              </div>
            ) : (
              <div className="text-center py-3">
                <p className="text-sm text-muted-foreground mb-3">{t("incidentDetail.noPostmortem")}</p>
                <Link to={`/postmortems/new?incidentId=${id}`}>
                  <Button variant="outline" className="bg-input-background">
                    <FileText className="w-4 h-4 mr-2" />
                    {t("incidentDetail.createPostmortem")}
                  </Button>
                </Link>
              </div>
            )}
          </Card>

          <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
            <h3 className="text-lg font-semibold mb-4 flex items-center gap-2">
              <BookOpen className="w-5 h-5 text-brand-500" />
              {t("incidentDetail.runbooks")}
            </h3>
            {relatedRunbooks.length > 0 ? (
              <div className="space-y-2">
                {relatedRunbooks.slice(0, 5).map((rb) => (
                  <Link
                    key={rb.id}
                    to={`/runbooks/${rb.id}`}
                    className="flex items-center gap-3 p-3 rounded-lg hover:bg-muted/10 transition-colors group"
                  >
                    <BookOpen className="w-4 h-4 text-muted-foreground group-hover:text-brand-500 flex-shrink-0" />
                    <div className="flex-1 min-w-0">
                      <p className="text-sm font-medium truncate group-hover:text-brand-500 transition-colors">{rb.title}</p>
                      {rb.tags?.length > 0 && (
                        <div className="flex gap-1 mt-1">
                          {rb.tags.slice(0, 3).map((tag) => (
                            <span key={tag} className="text-[10px] px-1.5 py-0.5 rounded bg-muted/20 text-muted-foreground">
                              {tag}
                            </span>
                          ))}
                        </div>
                      )}
                    </div>
                    <ChevronRight className="w-4 h-4 text-muted-foreground group-hover:text-brand-500 flex-shrink-0" />
                  </Link>
                ))}
              </div>
            ) : (
              <div className="text-center py-3">
                <p className="text-sm text-muted-foreground mb-3">{t("incidentDetail.noRunbooks")}</p>
                <Link to="/runbooks">
                  <Button variant="outline" className="bg-input-background">
                    <BookOpen className="w-4 h-4 mr-2" />
                    {t("incidentDetail.browseRunbooks")}
                  </Button>
                </Link>
              </div>
            )}
          </Card>

          {webhookDeliveries.length > 0 && (
            <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
              <h3 className="text-lg font-semibold mb-4 flex items-center gap-2">
                <Zap className="w-5 h-5 text-brand-500" />
                Outbound Deliveries
              </h3>
              <div className="space-y-2">
                {webhookDeliveries.slice(0, 5).map((d) => {
                  const statusClass =
                    d.status === "Succeeded" ? "bg-success-500/10 text-success-400 border-success-500/20" :
                    d.status === "Failed" ? "bg-error-500/10 text-error-400 border-error-500/20" :
                    d.status === "Retrying" ? "bg-yellow-500/10 text-yellow-400 border-yellow-500/20" :
                    "bg-muted/10 text-muted-foreground border-border";
                  return (
                    <div key={d.id} className="p-3 rounded-lg bg-muted/5 border border-border space-y-1">
                      <div className="flex items-center justify-between gap-2">
                        <span className={`text-[10px] px-1.5 py-0.5 rounded border ${statusClass}`}>
                          {d.status}
                        </span>
                        <span className="text-xs text-muted-foreground">
                          {d.ackType ?? "?"} · attempt {d.attemptCount}
                          {d.httpStatus != null && ` · HTTP ${d.httpStatus}`}
                        </span>
                        <span className="text-[10px] text-muted-foreground ml-auto">
                          {getTimeAgo(d.attemptedAt)}
                        </span>
                      </div>
                      <p className="text-xs text-muted-foreground truncate" title={d.url}>
                        {d.url}
                      </p>
                      {d.error && (
                        <p className="text-xs text-error-400 truncate" title={d.error}>
                          {d.error}
                        </p>
                      )}
                      {d.nextRetryAt && d.status === "Retrying" && (
                        <p className="text-[10px] text-yellow-400">
                          Next retry: {getTimeAgo(d.nextRetryAt)}
                        </p>
                      )}
                    </div>
                  );
                })}
              </div>
            </Card>
          )}
        </div>
      </div>

      <Dialog open={isReassignOpen} onOpenChange={setIsReassignOpen}>
        <DialogContent className="bg-card border-border sm:max-w-[480px]">
          <DialogHeader>
            <DialogTitle style={{ fontSize: "1.25rem", fontWeight: 600 }}>
              {t("incidents.reassignTitle")}
            </DialogTitle>
            <DialogDescription style={{ fontSize: "0.875rem", color: "#94A3B8" }}>
              {t("incidents.reassignDesc")}
            </DialogDescription>
          </DialogHeader>

          <div className="py-2">
            <label
              htmlFor={`${formId}-reassign`}
              style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}
            >
              {t("incidents.reassignTo")}
            </label>
            <Select value={reassignTo} onValueChange={setReassignTo}>
              <SelectTrigger id={`${formId}-reassign`} className="bg-input-background">
                <SelectValue placeholder={t("incidents.reassignPlaceholder")} />
              </SelectTrigger>
              <SelectContent>
                {assignableUsers.map((u) => (
                  <SelectItem key={u.id} value={u.id}>
                    {u.displayName || `${u.firstName ?? ""} ${u.lastName ?? ""}`.trim() || u.email}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          <DialogFooter>
            <Button
              variant="outline"
              onClick={() => setIsReassignOpen(false)}
              disabled={reassignIncident.isPending}
              className="bg-input-background"
            >
              {t("common.cancel")}
            </Button>
            <Button
              disabled={!reassignTo || reassignIncident.isPending}
              onClick={() => {
                if (!reassignTo || !id) return;
                reassignIncident.mutate(
                  { id, targetUserId: reassignTo },
                  { onSuccess: () => setIsReassignOpen(false) },
                );
              }}
              className="bg-brand-500 hover:bg-brand-600 text-white"
            >
              {reassignIncident.isPending ? t("common.saving") : t("incidents.reassign")}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <DeleteConfirmDialog
        open={!!deletingNote}
        onOpenChange={(open) => !open && setDeletingNote(null)}
        title={t("incidents.deleteNoteTitle")}
        message={t("incidents.deleteNoteMsg")}
        warning={t("incidents.deleteNoteWarn")}
        isLoading={deleteNote.isPending}
        onConfirm={() => {
          if (!deletingNote) return;
          deleteNote.mutate(
            { noteId: deletingNote.id, incidentId: id },
            { onSettled: () => setDeletingNote(null) },
          );
        }}
      />
    </div>
  );
}