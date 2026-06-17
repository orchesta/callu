import { useState, useMemo } from "react";
import { Link, useParams } from "react-router";
import { Button } from "@/shared/components/ui/button";
import { Badge } from "@/shared/components/ui/badge";
import { Card } from "@/shared/components/ui/card";
import { Avatar, AvatarFallback } from "@/shared/components/ui/avatar";
import { Textarea } from "@/shared/components/ui/textarea";
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
} from "lucide-react";
import {
  useIncident,
  useIncidentTimeline,
  useIncidentNotes,
  useAcknowledgeIncident,
  useResolveIncident,
  useCloseIncident,
  useReopenIncident,
  useEscalateIncident,
  useAddNote,
  useIncidentConference,
  useWebhookDeliveries,
} from "../hooks/use-incidents";
import { usePostmortems } from "@/features/postmortems/hooks/use-postmortems";
import { useRunbooks } from "@/features/runbooks/hooks/use-runbooks";
import type { IncidentNoteDto, IncidentTimelineEvent } from "../types/incident.types";
import { getSeverityBadge, getStatusBadge } from "@/shared/utils/incident-styles";
import { formatDateTime, getTimeAgo } from "@/shared/utils/time";
import { t } from "@/shared/locales/i18n";

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

function buildTimeline(
  events: IncidentTimelineEvent[],
  notes: IncidentNoteDto[],
): TimelineItem[] {
  const items: TimelineItem[] = [
    ...events.map((e) => ({ kind: "event" as const, data: e })),
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
  const { data: incident, isLoading } = useIncident(id);
  const { data: timelineData } = useIncidentTimeline(id);
  const { data: notes = [] } = useIncidentNotes(id);
  const { data: webhookDeliveries = [] } = useWebhookDeliveries(id);
  const { data: conference } = useIncidentConference(id);
  const { data: allPostmortems } = usePostmortems();
  const { data: allRunbooks } = useRunbooks();

  const acknowledgeIncident = useAcknowledgeIncident();
  const resolveIncident = useResolveIncident();
  const closeIncident = useCloseIncident();
  const reopenIncident = useReopenIncident();
  const escalateIncident = useEscalateIncident();
  const addNote = useAddNote();
  const [newNote, setNewNote] = useState("");

  const timelineEvents = timelineData?.events ?? [];
  const timelineItems = buildTimeline(timelineEvents, notes);

  const linkedPostmortem = useMemo(
    () => allPostmortems?.find((p) => p.incidentId === id),
    [allPostmortems, id]
  );

  const relatedRunbooks = useMemo(
    () => incident?.serviceId
      ? (allRunbooks?.filter((r) => r.serviceId === incident.serviceId) ?? [])
      : [],
    [allRunbooks, incident?.serviceId]
  );

  const handleAcknowledge = () => acknowledgeIncident.mutate(id);
  const handleResolve = () => resolveIncident.mutate(id);
  const handleClose = () => closeIncident.mutate(id);
  const handleReopen = () => reopenIncident.mutate(id);
  const handleEscalate = () => escalateIncident.mutate({ id });

  const handleAddNote = () => {
    if (!newNote.trim()) return;
    addNote.mutate(
      { incidentId: id, content: newNote },
      { onSuccess: () => setNewNote("") },
    );
  };

  if (isLoading || !incident) {
    return (
      <div className="p-6 flex items-center justify-center min-h-[400px]">
        <div className="w-6 h-6 border-2 border-brand-500/30 border-t-brand-500 rounded-full animate-spin" />
      </div>
    );
  }

  const shortId = id.replace(/-/g, "").slice(-8).toUpperCase();

  const showAck =
    incident.status !== "Acknowledged" &&
    incident.status !== "Resolved" &&
    incident.status !== "Closed";
  const showResolve = incident.status !== "Resolved" && incident.status !== "Closed";
  const showClose = incident.status === "Resolved";
  const showReopen = incident.status === "Resolved" || incident.status === "Closed";
  const showEscalate = incident.status !== "Resolved" && incident.status !== "Closed";

  return (
    <div className="p-6 space-y-6 pb-28 lg:pb-6">
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
        </div>
      </div>

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
      </div>

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
                        <p className="text-sm font-semibold">{note.content}</p>
                        <span className="text-xs text-muted-foreground flex-shrink-0">
                          {getTimeAgo(note.createdAt)}
                        </span>
                      </div>
                      {note.createdBy && (
                        <div className="flex items-center gap-2 mt-2">
                          <Avatar className="w-6 h-6">
                            <AvatarFallback className="text-[10px] bg-brand-500/10 text-brand-500">
                              {note.createdBy.split(" ").map((n: string) => n[0]).join("")}
                            </AvatarFallback>
                          </Avatar>
                          <span className="text-xs text-muted-foreground">{note.createdBy}</span>
                        </div>
                      )}
                    </div>
                  </div>
                );
              })}
            </div>

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

          {incident.serviceId && (
            <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
              <h3 className="text-lg font-semibold mb-4">{t("incidentDetail.escalation")}</h3>
              <div className="flex items-start gap-3">
                <div className="w-8 h-8 rounded-lg bg-brand-500/10 flex items-center justify-center flex-shrink-0">
                  <Zap className="w-4 h-4 text-brand-500" />
                </div>
                <div className="flex-1 min-w-0">
                  <p className="text-sm font-medium">{t("incidentDetail.escalationLinked")}</p>
                  <p className="text-xs text-muted-foreground mt-1">{t("incidentDetail.escalationViewHint")}</p>
                </div>
              </div>
              <Link to="/escalations">
                <Button variant="outline" className="w-full mt-4 bg-input-background">
                  {t("incidentDetail.viewEscalationPolicies")}
                  <ExternalLink className="w-4 h-4 ml-2" />
                </Button>
              </Link>
            </Card>
          )}

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
    </div>
  );
}