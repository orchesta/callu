import { useEffect, useState, useMemo } from "react";
import { Link, useParams, useNavigate } from "react-router";
import { Button } from "@/shared/components/ui/button";
import { Badge } from "@/shared/components/ui/badge";
import { Card } from "@/shared/components/ui/card";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
  DialogDescription,
} from "@/shared/components/ui/dialog";
import { Tabs, TabsList, TabsTrigger, TabsContent } from "@/shared/components/ui/tabs";
import {
  ChevronLeft,
  ChevronRight,
  Home,
  Trash2,
  Radio,
  Eye,
  Code,
  CheckCircle,
  AlertCircle,
  Clock,
  Zap,
  Copy,
  Check,
  FlaskConical,
  Link2,
} from "lucide-react";
import {
  useCapturesByService,
  useCapturesByIntegration,
  useServiceCaptureCount,
  useIntegrationCaptureCount,
  useMarkCaptureReviewed,
  useDeleteCapture,
  useDeleteAllCaptures,
  useDeleteAllIntegrationCaptures,
} from "../hooks/use-captures";
import type { WebhookCaptureDto } from "../types/webhook-capture.types";
import { useService, serviceQueries, useCreateService } from "../hooks/use-services";
import { CAPTURE_PAGE_SIZE } from "../api/captures.api";
import { getLocale, t } from "@/shared/locales/i18n";
import { useLocaleTick } from "@/shared/hooks/use-locale-tick";
import { useQuery } from "@tanstack/react-query";
import { useTestWebhookTemplate } from "@/features/settings/hooks/use-webhook-templates";
import { TemplateTestResultView } from "@/features/settings/components/template-test-result";
import { useIntegration, useBindIntegrationService } from "@/features/applications/hooks/use-integrations";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";

function formatRelativeTime(
  dateStr: string,
  translate: (key: string, params?: Record<string, string | number>) => string,
) {
  const diff = Date.now() - new Date(dateStr).getTime();
  const minutes = Math.floor(diff / 60000);
  const hours = Math.floor(minutes / 60);
  const days = Math.floor(hours / 24);
  if (minutes < 1) return translate("webhookCaptures.relativeJustNow");
  if (minutes < 60) return translate("webhookCaptures.relativeMinutes", { count: minutes });
  if (hours < 24) return translate("webhookCaptures.relativeHours", { count: hours });
  return translate("webhookCaptures.relativeDays", { count: days });
}

function formatFullTimestamp(dateStr: string) {
  const tag = getLocale() === "tr" ? "tr-TR" : "en-US";
  return new Date(dateStr).toLocaleString(tag, {
    month: "short", day: "numeric", year: "numeric",
    hour: "2-digit", minute: "2-digit", second: "2-digit",
  });
}

function formatBodySize(bytes: number) {
  if (bytes < 1024) return `${bytes} B`;
  return `${(bytes / 1024).toFixed(1)} KB`;
}

function parseHeaders(headers: string): Record<string, string> {
  try { return JSON.parse(headers); } catch { return {}; }
}

function parseBody(body: string): unknown {
  try { return JSON.parse(body); } catch { return body; }
}

import React from "react";

const CaptureStats = React.memo(function CaptureStats({ captures, total }: { captures: WebhookCaptureDto[]; total: number }) {
  const { newCount, reviewed, resolved } = useMemo(() => {
    let n = 0, rev = 0, res = 0;
    for (const c of captures) {
      if (c.status === 'Reviewed') rev++;
      else if (c.status === 'Ignored' || c.status === 'UsedForTemplate') res++;
      else n++;
    }
    return { newCount: n, reviewed: rev, resolved: res };
  }, [captures]);

  return (
    <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
      <div className="p-4 rounded-lg bg-card/80 backdrop-blur-sm border border-border">
        <p style={{ fontSize: '0.75rem', color: '#94A3B8', fontWeight: 600, letterSpacing: '0.05em' }}>{t("webhookCaptures.statTotal").toUpperCase()}</p>
        <p style={{ fontSize: '1.5rem', fontWeight: 700, marginTop: '0.5rem' }}>{total}</p>
      </div>
      <div className="p-4 rounded-lg bg-card/80 backdrop-blur-sm border border-success-500/20">
        <p style={{ fontSize: '0.75rem', color: '#22C55E', fontWeight: 600, letterSpacing: '0.05em' }}>{t("webhookCaptures.statNew").toUpperCase()}</p>
        <p style={{ fontSize: '1.5rem', fontWeight: 700, color: '#22C55E', marginTop: '0.5rem' }}>{newCount}</p>
      </div>
      <div className="p-4 rounded-lg bg-card/80 backdrop-blur-sm border border-muted/20">
        <p style={{ fontSize: '0.75rem', color: '#94A3B8', fontWeight: 600, letterSpacing: '0.05em' }}>{t("webhookCaptures.statResolved").toUpperCase()}</p>
        <p style={{ fontSize: '1.5rem', fontWeight: 700, marginTop: '0.5rem' }}>{resolved}</p>
      </div>
      <div className="p-4 rounded-lg bg-card/80 backdrop-blur-sm border border-muted/20">
        <p style={{ fontSize: '0.75rem', color: '#94A3B8', fontWeight: 600, letterSpacing: '0.05em' }}>{t("webhookCaptures.statReviewed").toUpperCase()}</p>
        <p style={{ fontSize: '1.5rem', fontWeight: 700, marginTop: '0.5rem' }}>{reviewed}</p>
      </div>
    </div>
  );
});

interface WebhookCapturesProps {
  /** 'application' reads the route :id as an integration id; default is the service page. */
  scope?: "service" | "application";
}

export function WebhookCaptures({ scope = "service" }: WebhookCapturesProps) {
  const { id = "" } = useParams();
  const isApplication = scope === "application";
  const serviceId = isApplication ? "" : id;
  const integrationId = isApplication ? id : "";
  const navigate = useNavigate();
  const { data: service } = useService(serviceId);
  const { data: integration } = useIntegration(isApplication ? integrationId : undefined);

  const i18nTick = useLocaleTick();

  const [page, setPage] = useState(1);
  const { data: serviceCaptures = [], isLoading: isServiceLoading } = useCapturesByService(serviceId, page);
  const { data: integrationCaptures = [], isLoading: isIntegrationLoading } =
    useCapturesByIntegration(integrationId, page);
  const { data: serviceCount } = useServiceCaptureCount(serviceId);
  const { data: integrationCount } = useIntegrationCaptureCount(integrationId);
  const captures = isApplication ? integrationCaptures : serviceCaptures;
  const isLoading = isApplication ? isIntegrationLoading : isServiceLoading;
  const totalCount = (isApplication ? integrationCount?.count : serviceCount?.count) ?? captures.length;
  const totalPages = Math.max(1, Math.ceil(totalCount / CAPTURE_PAGE_SIZE));

  // An emptied page (deletes, cap trim) steps back instead of showing a dead screen.
  useEffect(() => {
    if (!isLoading && captures.length === 0 && page > 1) setPage((p) => Math.max(1, p - 1));
  }, [isLoading, captures.length, page]);

  const markReviewedMutation = useMarkCaptureReviewed();
  const deleteMutation = useDeleteCapture();
  const deleteAllMutation = useDeleteAllCaptures();
  const clearIntegrationCapturesMutation = useDeleteAllIntegrationCaptures();
  const previewParseMutation = useTestWebhookTemplate();

  const { data: services = [] } = useQuery({ ...serviceQueries.all(), enabled: isApplication });
  const createServiceMutation = useCreateService();
  const bindServiceMutation = useBindIntegrationService();

  const boundTemplateId = isApplication ? integration?.webhookTemplateId : service?.webhookTemplateId;

  const [selectedCapture, setSelectedCapture] = useState<WebhookCaptureDto | null>(null);
  const [isDetailModalOpen, setIsDetailModalOpen] = useState(false);
  const [isDeleteAllModalOpen, setIsDeleteAllModalOpen] = useState(false);
  const [activeTab, setActiveTab] = useState("body");
  const [copiedId, setCopiedId] = useState<string | null>(null);
  const [isBindModalOpen, setIsBindModalOpen] = useState(false);
  const [bindMode, setBindMode] = useState<"select" | "create">("select");
  const [bindServiceId, setBindServiceId] = useState<string | null>(null);
  const [newServiceName, setNewServiceName] = useState("");

  const getMethodBadge = (method: string) => {
    switch (method) {
      case "GET":
        return "bg-blue-400/10 text-blue-400 border-blue-400/20";
      case "POST":
        return "bg-success-500/10 text-success-500 border-success-500/20";
      case "PUT":
        return "bg-warning-500/10 text-warning-500 border-warning-500/20";
      case "PATCH":
        return "bg-purple-400/10 text-purple-400 border-purple-400/20";
      case "DELETE":
        return "bg-error-500/10 text-error-500 border-error-500/20";
      default:
        return "bg-muted/10 text-muted-foreground border-muted/20";
    }
  };

  const handleViewCapture = (capture: WebhookCaptureDto) => {
    previewParseMutation.reset();
    setSelectedCapture(capture);
    setIsDetailModalOpen(true);
  };

  const handleMarkReviewed = async (captureId: string) => {
    try {
      await markReviewedMutation.mutateAsync(captureId);
    } catch { /* empty */ }
  };

  const handleDeleteCapture = async (captureId: string) => {
    try {
      await deleteMutation.mutateAsync(captureId);
      if (selectedCapture?.id === captureId) {
        setIsDetailModalOpen(false);
      }
    } catch { /* empty */ }
  };

  const handleClearAll = async () => {
    try {
      if (isApplication) {
        await clearIntegrationCapturesMutation.mutateAsync(integrationId);
      } else {
        await deleteAllMutation.mutateAsync(serviceId);
      }
      setPage(1);
      setIsDeleteAllModalOpen(false);
    } catch { /* empty */ }
  };

  const handleUseForTemplate = (captureId: string) => {
    navigate(
      isApplication
        ? `/applications/${integrationId}/template?captureId=${captureId}`
        : `/services/${serviceId}/template?captureId=${captureId}`,
    );
  };

  const handlePreviewParse = () => {
    if (!boundTemplateId || !selectedCapture) return;
    previewParseMutation.mutate({ id: boundTemplateId, samplePayload: selectedCapture.body });
  };

  const closeBindModal = () => {
    setIsBindModalOpen(false);
    setBindMode("select");
    setBindServiceId(null);
    setNewServiceName("");
  };

  const handleBindExisting = async () => {
    if (!bindServiceId) return;
    try {
      await bindServiceMutation.mutateAsync({ id: integrationId, serviceId: bindServiceId });
      closeBindModal();
    } catch { /* empty */ }
  };

  const handleCreateAndBind = async () => {
    const name = newServiceName.trim();
    if (!name) return;
    try {
      const created = await createServiceMutation.mutateAsync({ name, type: "Api" });
      await bindServiceMutation.mutateAsync({ id: integrationId, serviceId: created.id });
      closeBindModal();
    } catch { /* empty */ }
  };

  const bindSelectedService = services.find((svc) => svc.id === bindServiceId);
  const showNoTeamWarning =
    isApplication &&
    !integration?.teamId &&
    (bindMode === "create" ? true : !!bindSelectedService && !bindSelectedService.teamName);

  const copyToClipboard = (text: string, id: string) => {
    navigator.clipboard.writeText(text);
    setCopiedId(id);
    setTimeout(() => setCopiedId(null), 2000);
  };

  if (isLoading) {
    return (
      <div className="p-6 flex items-center justify-center min-h-[400px]">
        <div className="w-6 h-6 border-2 border-brand-500/30 border-t-brand-500 rounded-full animate-spin" />
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
          {isApplication ? (
            <>
              <Link to="/applications" className="text-muted-foreground hover:text-foreground transition-colors">
                {t("applications.captures.breadcrumbRoot")}
              </Link>
              {integration?.name && (
                <>
                  <ChevronRight className="w-4 h-4 text-muted-foreground" />
                  <span className="text-muted-foreground">{integration.name}</span>
                </>
              )}
              <ChevronRight className="w-4 h-4 text-muted-foreground" />
              <span className="text-foreground font-medium">{t("applications.captures.title")}</span>
            </>
          ) : (
            <>
              <Link to="/services" className="text-muted-foreground hover:text-foreground transition-colors">
                {t("services.title")}
              </Link>
              <ChevronRight className="w-4 h-4 text-muted-foreground" />
              <Link to={`/services/${serviceId}`} className="text-muted-foreground hover:text-foreground transition-colors">
                {service?.name ?? t("services.varGroupService")}
              </Link>
              <ChevronRight className="w-4 h-4 text-muted-foreground" />
              <span className="text-foreground font-medium">{t("webhookCaptures.pageTitle")}</span>
            </>
          )}
        </nav>

        <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
          <div>
            <h1 style={{ fontSize: '1.875rem', fontWeight: 600 }}>
              {isApplication ? t("applications.captures.title") : t("webhookCaptures.pageTitle")}
            </h1>
            <p style={{ fontSize: '0.875rem', color: '#94A3B8', marginTop: '0.25rem' }}>
              {isApplication ? t("applications.captures.subtitle") : t("webhookCaptures.pageSubtitle")}
            </p>
          </div>
          {isApplication ? (
            <div className="flex items-center gap-2">
              <Button
                onClick={() => setIsBindModalOpen(true)}
                className="bg-brand-500 hover:bg-brand-600 text-white"
              >
                <Link2 className="w-4 h-4 mr-2" />
                {t("applications.captures.bindToService")}
              </Button>
              <Button
                onClick={() => setIsDeleteAllModalOpen(true)}
                disabled={totalCount === 0}
                variant="outline"
                className="bg-input-background hover:bg-error-500/10 hover:text-error-500"
              >
                <Trash2 className="w-4 h-4 mr-2" />
                {t("webhookCaptures.clearAll")}
              </Button>
            </div>
          ) : (
            <Button
              onClick={() => setIsDeleteAllModalOpen(true)}
              disabled={totalCount === 0}
              variant="outline"
              className="bg-input-background hover:bg-error-500/10 hover:text-error-500"
            >
              <Trash2 className="w-4 h-4 mr-2" />
              {t("webhookCaptures.clearAll")}
            </Button>
          )}
        </div>

        <CaptureStats key={i18nTick} captures={captures} total={totalCount} />

        {captures.length > 0 ? (
          <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
            <div className="space-y-2">
              {captures.map((capture) => (
                <div
                  key={capture.id}
                  className="p-4 rounded-lg bg-surface-light/20 hover:bg-surface-light/30 transition-all border border-border hover:border-border-light cursor-pointer"
                  tabIndex={0}
                  role="button"
                  onClick={() => handleViewCapture(capture)}
                  onKeyDown={(e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); handleViewCapture(capture); } }}
                >
                  <div className="flex items-center justify-between gap-4 mb-3">
                    <div className="flex items-center gap-3">
                      <Badge className={`${getMethodBadge(capture.method)} border text-xs font-mono`}>
                        {capture.method}
                      </Badge>
                      <span style={{ fontSize: '0.8125rem', color: '#94A3B8' }} className="font-mono">
                        {formatBodySize(capture.bodySize)}
                      </span>
                      {capture.status === 'Reviewed' && (
                        <Badge className="bg-muted/20 text-muted-foreground border-muted/30 border text-xs">
                          {t("webhookCaptures.badgeReviewed")}
                        </Badge>
                      )}
                    </div>
                    <div className="flex items-center gap-2 text-sm text-muted-foreground">
                      <Clock className="w-4 h-4" />
                      <span>{formatRelativeTime(capture.capturedAt, t)}</span>
                    </div>
                  </div>

                  <div className="flex items-start justify-between gap-4">
                    <div className="flex-1 min-w-0">
                      <p style={{ fontSize: '0.8125rem', color: '#94A3B8', marginBottom: '0.25rem' }}>
                        {t("webhookCaptures.fieldSourceIp")}
                      </p>
                      <p style={{ fontSize: '0.875rem' }} className="font-mono truncate">
                        {capture.sourceIp}
                      </p>
                    </div>
                    <div className="flex-1 min-w-0">
                      <p style={{ fontSize: '0.8125rem', color: '#94A3B8', marginBottom: '0.25rem' }}>
                        {t("webhookCaptures.fieldContentType")}
                      </p>
                      <p style={{ fontSize: '0.875rem' }} className="font-mono truncate">
                        {capture.contentType ?? t("webhookCaptures.fieldNotApplicable")}
                      </p>
                    </div>
                    <div className="flex-shrink-0">
                      <Button
                        size="sm"
                        variant="outline"
                        onClick={(e) => {
                          e.stopPropagation();
                          handleViewCapture(capture);
                        }}
                        className="bg-input-background"
                      >
                        <Eye className="w-4 h-4 mr-2" />
                        {t("webhookCaptures.view")}
                      </Button>
                    </div>
                  </div>

                  <div className="mt-3 p-3 rounded-md bg-muted/10 border border-border">
                    <p style={{ fontSize: '0.75rem', color: '#94A3B8', marginBottom: '0.5rem' }}>
                      {t("webhookCaptures.payloadPreview")}
                    </p>
                    <pre className="text-xs font-mono overflow-hidden text-ellipsis whitespace-nowrap">
                      {capture.body.substring(0, 100)}{capture.body.length > 100 ? '...' : ''}
                    </pre>
                  </div>
                </div>
              ))}
            </div>
            {totalPages > 1 && (
              <div className="mt-4 flex items-center justify-between border-t border-border pt-4">
                <Button
                  variant="outline"
                  size="sm"
                  className="bg-input-background"
                  disabled={page <= 1}
                  onClick={() => setPage((p) => Math.max(1, p - 1))}
                >
                  <ChevronLeft className="w-4 h-4 mr-1" />
                  {t("common.previous")}
                </Button>
                <span className="text-sm text-muted-foreground">
                  {t("webhookCaptures.pageIndicator", { page, pages: totalPages })}
                </span>
                <Button
                  variant="outline"
                  size="sm"
                  className="bg-input-background"
                  disabled={page >= totalPages}
                  onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
                >
                  {t("common.next")}
                  <ChevronRight className="w-4 h-4 ml-1" />
                </Button>
              </div>
            )}
          </Card>
        ) : (
          <Card className="p-12 bg-card/80 backdrop-blur-sm border-border text-center">
            <div className="flex justify-center mb-4">
              <div className="w-16 h-16 rounded-full bg-muted/20 flex items-center justify-center">
                <Radio className="w-8 h-8 text-muted-foreground animate-pulse" />
              </div>
            </div>
            <h3 style={{ fontSize: '1.125rem', fontWeight: 600, marginBottom: '0.5rem' }}>
              {t("webhookCaptures.emptyTitle")}
            </h3>
            <p style={{ fontSize: '0.875rem', color: '#94A3B8', marginBottom: '1.5rem', maxWidth: '32rem', margin: '0 auto' }}>
              {isApplication ? t("applications.captures.emptyDesc") : t("webhookCaptures.emptyDesc")}
            </p>
            <Link to={isApplication ? "/applications" : `/services/${serviceId}`}>
              <Button className="bg-brand-500 hover:bg-brand-600">
                <Radio className="w-4 h-4 mr-2" />
                {t("webhookCaptures.enableListening")}
              </Button>
            </Link>
          </Card>
        )}
      </div>

      <Dialog
        open={isDetailModalOpen}
        onOpenChange={(open) => {
          setIsDetailModalOpen(open);
          if (!open) previewParseMutation.reset();
        }}
      >
        <DialogContent className="bg-card border-border sm:max-w-[800px] max-h-[90vh] overflow-y-auto">
          {selectedCapture && (
            <>
              <DialogHeader>
                <DialogTitle style={{ fontSize: '1.5rem', fontWeight: 600 }}>
                  {t("webhookCaptures.modalTitle")}
                </DialogTitle>
                <DialogDescription style={{ fontSize: '0.875rem', color: '#94A3B8' }}>
                  {t("webhookCaptures.modalDesc")}
                </DialogDescription>
              </DialogHeader>

              <div className="space-y-5">
                <div className="grid grid-cols-2 gap-4">
                  <div>
                    <p style={{ fontSize: '0.75rem', color: '#94A3B8', marginBottom: '0.5rem' }}>
                      {t("webhookCaptures.fieldTimestamp")}
                    </p>
                    <p style={{ fontSize: '0.875rem', fontWeight: 600 }}>
                      {formatFullTimestamp(selectedCapture.capturedAt)}
                    </p>
                  </div>
                  <div>
                    <p style={{ fontSize: '0.75rem', color: '#94A3B8', marginBottom: '0.5rem' }}>
                      {t("webhookCaptures.fieldStatus")}
                    </p>
                    <div className="flex items-center gap-2">
                      <Badge className={`${getMethodBadge(selectedCapture.method)} border text-xs`}>
                        {selectedCapture.method}
                      </Badge>
                    </div>
                  </div>
                  <div>
                    <p style={{ fontSize: '0.75rem', color: '#94A3B8', marginBottom: '0.5rem' }}>
                      {t("webhookCaptures.fieldSourceIp")}
                    </p>
                    <p style={{ fontSize: '0.875rem' }} className="font-mono">
                      {selectedCapture.sourceIp}
                    </p>
                  </div>
                  <div>
                    <p style={{ fontSize: '0.75rem', color: '#94A3B8', marginBottom: '0.5rem' }}>
                      {t("webhookCaptures.fieldPayloadSize")}
                    </p>
                    <p style={{ fontSize: '0.875rem' }} className="font-mono">
                      {formatBodySize(selectedCapture.bodySize)}
                    </p>
                  </div>
                </div>

                <Tabs value={activeTab} onValueChange={setActiveTab}>
                  <TabsList className="bg-card/80 backdrop-blur-sm border border-border">
                    <TabsTrigger value="body">
                      <Code className="w-4 h-4 mr-2" />
                      {t("webhookCaptures.tabBody")}
                    </TabsTrigger>
                    <TabsTrigger value="headers">
                      {t("webhookCaptures.tabHeaders")}
                    </TabsTrigger>
                  </TabsList>

                  <TabsContent value="body" className="space-y-3">
                    <div className="flex items-center justify-between">
                      <p style={{ fontSize: '0.875rem', fontWeight: 600 }}>
                        {t("webhookCaptures.requestBody")}
                      </p>
                      <Button
                        size="sm"
                        variant="outline"
                        onClick={() => {
                          copyToClipboard(selectedCapture.body, 'body');
                        }}
                        className="bg-input-background"
                      >
                        {copiedId === 'body' ? (
                          <>
                            <Check className="w-4 h-4 mr-2 text-success-500" />
                            {t("webhookCaptures.copied")}
                          </>
                        ) : (
                          <>
                            <Copy className="w-4 h-4 mr-2" />
                            {t("webhookCaptures.copy")}
                          </>
                        )}
                      </Button>
                    </div>
                    <pre className="p-4 rounded-lg bg-muted/10 border border-border text-xs font-mono overflow-x-auto max-h-96 overflow-y-auto">
                      {(() => { const parsed = parseBody(selectedCapture.body); return typeof parsed === 'string' ? parsed : JSON.stringify(parsed, null, 2); })()}
                    </pre>
                  </TabsContent>

                  <TabsContent value="headers" className="space-y-3">
                    <p style={{ fontSize: '0.875rem', fontWeight: 600 }}>
                      {t("webhookCaptures.httpHeaders")}
                    </p>
                    <div className="space-y-2">
                      {Object.entries(parseHeaders(selectedCapture.headers)).map(([key, value]) => (
                        <div
                          key={key}
                          className="flex items-start gap-3 p-3 rounded-lg bg-surface-light/20 border border-border"
                        >
                          <div className="flex-1 min-w-0">
                            <p style={{ fontSize: '0.75rem', color: '#94A3B8', marginBottom: '0.25rem' }}>
                              {key}
                            </p>
                            <p style={{ fontSize: '0.875rem' }} className="font-mono break-all">
                              {value}
                            </p>
                          </div>
                        </div>
                      ))}
                    </div>
                  </TabsContent>
                </Tabs>

                <div className="pt-4 border-t border-border space-y-3">
                  {boundTemplateId ? (
                    <>
                      <div className="flex items-center justify-between gap-4">
                        <p style={{ fontSize: '0.875rem', fontWeight: 600 }}>
                          {t("applications.captures.previewParse")}
                        </p>
                        <Button
                          size="sm"
                          variant="outline"
                          disabled={previewParseMutation.isPending}
                          onClick={handlePreviewParse}
                          className="bg-input-background"
                        >
                          <FlaskConical className="w-4 h-4 mr-2" />
                          {previewParseMutation.isPending
                            ? t("webhookTemplates.running")
                            : t("applications.captures.previewParse")}
                        </Button>
                      </div>
                      {previewParseMutation.data && (
                        <TemplateTestResultView result={previewParseMutation.data} />
                      )}
                    </>
                  ) : (
                    <p style={{ fontSize: '0.8125rem', color: '#94A3B8' }}>
                      {t("applications.captures.noTemplateHint")}
                    </p>
                  )}
                </div>

                <div className="flex flex-wrap gap-2 pt-4 border-t border-border">
                  {selectedCapture.status !== 'Reviewed' && (
                    <Button
                      variant="outline"
                      onClick={() => {
                        handleMarkReviewed(selectedCapture.id);
                        setSelectedCapture({ ...selectedCapture, status: 'Reviewed' });
                      }}
                      className="bg-input-background"
                    >
                      <CheckCircle className="w-4 h-4 mr-2" />
                      {t("webhookCaptures.markReviewed")}
                    </Button>
                  )}
                  <Button
                    onClick={() => handleUseForTemplate(selectedCapture.id)}
                    className="bg-brand-500 hover:bg-brand-600 text-white"
                  >
                    <Zap className="w-4 h-4 mr-2" />
                    {t("webhookCaptures.useForTemplate")}
                  </Button>
                  <Button
                    variant="outline"
                    onClick={() => handleDeleteCapture(selectedCapture.id)}
                    className="bg-input-background hover:bg-error-500/10 hover:text-error-500 ml-auto"
                  >
                    <Trash2 className="w-4 h-4 mr-2" />
                    {t("common.delete")}
                  </Button>
                </div>
              </div>
            </>
          )}
        </DialogContent>
      </Dialog>

      <Dialog open={isDeleteAllModalOpen} onOpenChange={setIsDeleteAllModalOpen}>
        <DialogContent className="bg-card border-border sm:max-w-[500px]">
          <DialogHeader>
            <DialogTitle style={{ fontSize: '1.5rem', fontWeight: 600 }}>
              {t("webhookCaptures.clearAllTitle")}
            </DialogTitle>
            <DialogDescription style={{ fontSize: '0.875rem', color: '#94A3B8' }}>
              {t("webhookCaptures.clearAllDesc")}
            </DialogDescription>
          </DialogHeader>
          <div className="py-4">
            <div className="flex gap-3 mb-4">
              <div className="w-10 h-10 rounded-full bg-error-500/10 flex items-center justify-center flex-shrink-0">
                <AlertCircle className="w-5 h-5 text-error-500" />
              </div>
              <div>
                <p style={{ fontSize: '0.875rem', marginBottom: '0.5rem' }}>
                  {t("webhookCaptures.clearAllLead", { count: totalCount })}
                </p>
                <p style={{ fontSize: '0.8125rem', color: '#94A3B8' }}>
                  {t("communications.deleteDialogShort")}
                </p>
              </div>
            </div>
          </div>
          <DialogFooter>
            <Button
              variant="outline"
              onClick={() => setIsDeleteAllModalOpen(false)}
              className="bg-input-background"
            >
              {t("common.cancel")}
            </Button>
            <Button
              onClick={handleClearAll}
              className="bg-error-500 hover:bg-error-600 text-white"
            >
              <Trash2 className="w-4 h-4 mr-2" />
              {t("webhookCaptures.clearAllConfirmBtn")}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {isApplication && (
        <Dialog open={isBindModalOpen} onOpenChange={(open) => { if (!open) closeBindModal(); }}>
          <DialogContent className="bg-card border-border sm:max-w-[520px]">
            <DialogHeader>
              <DialogTitle style={{ fontSize: '1.5rem', fontWeight: 600 }}>
                {t("applications.captures.bindToService")}
              </DialogTitle>
              <DialogDescription style={{ fontSize: '0.875rem', color: '#94A3B8' }}>
                {t("applications.captures.bindDialogDesc")}
              </DialogDescription>
            </DialogHeader>

            <div className="space-y-4">
              <div className="flex gap-2">
                <Button
                  size="sm"
                  variant={bindMode === "select" ? "default" : "outline"}
                  onClick={() => setBindMode("select")}
                  className={bindMode === "select" ? "bg-brand-500 hover:bg-brand-600 text-white" : "bg-input-background"}
                >
                  {t("applications.captures.selectExisting")}
                </Button>
                <Button
                  size="sm"
                  variant={bindMode === "create" ? "default" : "outline"}
                  onClick={() => setBindMode("create")}
                  className={bindMode === "create" ? "bg-brand-500 hover:bg-brand-600 text-white" : "bg-input-background"}
                >
                  {t("applications.captures.createNew")}
                </Button>
              </div>

              {bindMode === "select" ? (
                services.length === 0 ? (
                  <p style={{ fontSize: '0.875rem', color: '#94A3B8' }}>
                    {t("applications.captures.noServices")}
                  </p>
                ) : (
                  <div
                    role="radiogroup"
                    aria-label={t("applications.captures.selectExisting")}
                    className="max-h-64 overflow-y-auto space-y-2"
                  >
                    {services.map((svc) => (
                      <button
                        key={svc.id}
                        type="button"
                        role="radio"
                        aria-checked={bindServiceId === svc.id}
                        onClick={() => setBindServiceId(svc.id)}
                        className={`w-full flex items-center justify-between gap-3 p-3 rounded-lg border text-left transition-colors ${
                          bindServiceId === svc.id
                            ? "border-brand-500 bg-brand-500/10"
                            : "border-border bg-surface-light/20 hover:bg-surface-light/30"
                        }`}
                      >
                        <span style={{ fontSize: '0.875rem', fontWeight: 600 }}>{svc.name}</span>
                        <span style={{ fontSize: '0.75rem', color: '#94A3B8' }}>
                          {svc.teamName ?? t("applications.captures.noTeam")}
                        </span>
                      </button>
                    ))}
                  </div>
                )
              ) : (
                <div className="space-y-2">
                  <Label htmlFor="bind-service-name">{t("applications.captures.serviceNameLabel")}</Label>
                  <Input
                    id="bind-service-name"
                    value={newServiceName}
                    onChange={(e) => setNewServiceName(e.target.value)}
                    className="bg-input-background"
                  />
                </div>
              )}

              {showNoTeamWarning && (
                <div className="flex items-start gap-2 p-3 rounded-lg bg-warning-500/10 border border-warning-500/20">
                  <AlertCircle className="w-4 h-4 text-warning-500 flex-shrink-0 mt-0.5" />
                  <p style={{ fontSize: '0.8125rem' }} className="text-warning-500">
                    {t("applications.captures.noTeamWarning")}
                  </p>
                </div>
              )}
            </div>

            <DialogFooter>
              <Button variant="outline" onClick={closeBindModal} className="bg-input-background">
                {t("common.cancel")}
              </Button>
              {bindMode === "select" ? (
                <Button
                  disabled={!bindServiceId || bindServiceMutation.isPending}
                  onClick={handleBindExisting}
                  className="bg-brand-500 hover:bg-brand-600 text-white"
                >
                  <Link2 className="w-4 h-4 mr-2" />
                  {t("applications.captures.bindConfirm")}
                </Button>
              ) : (
                <Button
                  disabled={!newServiceName.trim() || createServiceMutation.isPending || bindServiceMutation.isPending}
                  onClick={handleCreateAndBind}
                  className="bg-brand-500 hover:bg-brand-600 text-white"
                >
                  <Link2 className="w-4 h-4 mr-2" />
                  {t("applications.captures.createAndBind")}
                </Button>
              )}
            </DialogFooter>
          </DialogContent>
        </Dialog>
      )}
    </>
  );
}