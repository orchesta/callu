/**
 * Status Page Management
 * Admin panel for configuring public status page
 */

import { useState, useEffect, useMemo, useReducer, useId } from 'react';
import { DeleteConfirmDialog } from "@/shared/components/delete-confirm-dialog";
import { toast } from '@/shared/utils/toast';
import { getLocale, onLocaleChange, t } from '@/shared/locales/i18n';
import { Button } from '@/shared/components/ui/button';
import { Card } from '@/shared/components/ui/card';
import { Input } from '@/shared/components/ui/input';
import { Label } from '@/shared/components/ui/label';
import { Switch } from '@/shared/components/ui/switch';
import { Badge } from '@/shared/components/ui/badge';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/shared/components/ui/select';
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from '@/shared/components/ui/dialog';
import { Textarea } from '@/shared/components/ui/textarea';
import {
  ChevronDown,
  ChevronUp,
  ExternalLink,
  Eye,
  Copy,
  Check,
  Settings,
  Globe,
  Link as LinkIcon,
  Save,
  Plus,
  GripVertical,
  Loader2,
  AlertTriangle,
  HeartPulse,
  Zap,
  Trash2,
  Mail,
} from 'lucide-react';
import { Alert, AlertDescription } from '@/shared/components/ui/alert';
import {
  useStatusPages,
  useStatusPage,
  useCreateStatusPage,
  useDeleteStatusPage,
  useUpdateStatusPage,
  useAddComponent,
  useUpdateComponent,
  useRemoveComponent,
  useCreateStatusIncident,
  useAddIncidentUpdate,
  useNotifyStatusPageSubscribers,
  useStatusPageStats,
  useStatusPageUptime,
  useStatusPageSubscribers,
  useRemoveSubscriber,
} from '../hooks/use-status-pages';
import { UptimeGraph } from './uptime-graph';
import { orderComponents } from '../utils/component-order';
import { HealthCheckDialog, type HealthCheckPayload } from './health-check-dialog';
import { useServices } from '@/features/services/hooks/use-services';
import type { StatusPageComponentDto } from '../types/status-page.types';
import { dateLocale } from "@/shared/utils/datetime";

function incidentStatusLabelManage(status: string): string {
  const key = status.toLowerCase();
  const map: Record<string, string> = {
    investigating: 'statusPage.incidentStatus.investigating',
    identified: 'statusPage.incidentStatus.identified',
    monitoring: 'statusPage.incidentStatus.monitoring',
    resolved: 'statusPage.incidentStatus.resolved',
  };
  const trKey = map[key];
  return trKey ? t(trKey) : status;
}

export function StatusPageManagement() {
  const formId = useId();
  const [, bumpManageLocale] = useReducer((n: number) => n + 1, 0);
  useEffect(() => onLocaleChange(() => bumpManageLocale()), []);

  const { data: statusPages, isLoading: isPagesLoading } = useStatusPages();
  const createPageMutation = useCreateStatusPage();
  const deletePageMutation = useDeleteStatusPage();
  const currentPageId = statusPages?.[0]?.id;
  const { data: pageDetail } = useStatusPage(currentPageId ?? '');
  const { data: stats } = useStatusPageStats(currentPageId);
  const { data: uptimeData, isLoading: isUptimeLoading } = useStatusPageUptime(currentPageId);
  const updatePageMutation = useUpdateStatusPage();
  const addComponentMutation = useAddComponent();
  const updateComponentMutation = useUpdateComponent();
  const removeComponentMutation = useRemoveComponent();
  const createIncidentMutation = useCreateStatusIncident();
  const { data: subscriberList = [], isLoading: isSubscribersLoading } = useStatusPageSubscribers(currentPageId);
  const [isDeletingPage, setIsDeletingPage] = useState(false);
  const removeSubscriberMutation = useRemoveSubscriber();

  const { data: allServices = [] } = useServices();

  const [localName, setLocalName] = useState('');
  const [localSlug, setLocalSlug] = useState('');
  const [localIsPublic, setLocalIsPublic] = useState(true);
  const [localSupportEmail, setLocalSupportEmail] = useState('');
  const [localAllowSubscriptions, setLocalAllowSubscriptions] = useState(true);
  const [copied, setCopied] = useState(false);
  const [success, setSuccess] = useState('');
  const [initialised, setInitialised] = useState(false);

  const [isAddComponentModalOpen, setIsAddComponentModalOpen] = useState(false);
  const [addComponentServiceId, setAddComponentServiceId] = useState('');
  const [addComponentName, setAddComponentName] = useState('');
  const [addComponentDesc, setAddComponentDesc] = useState('');

  const [expandedIncidentId, setExpandedIncidentId] = useState<string | null>(null);
  const [updateMessage, setUpdateMessage] = useState('');
  const [updateStatus, setUpdateStatus] = useState('investigating');
  const addIncidentUpdateMutation = useAddIncidentUpdate();
  const notifySubscribersMutation = useNotifyStatusPageSubscribers();
  const [isIncidentModalOpen, setIsIncidentModalOpen] = useState(false);
  const [isMaintenanceModalOpen, setIsMaintenanceModalOpen] = useState(false);
  const [incidentTitle, setIncidentTitle] = useState('');

  /** Which component the health-check dialog is editing; the dialog owns the draft itself. */
  const [hcDialogComponent, setHcDialogComponent] = useState<StatusPageComponentDto | null>(null);
  const [incidentMessage, setIncidentMessage] = useState('');

  useEffect(() => {
    if (pageDetail && !initialised) {
      setLocalName(pageDetail.name ?? '');
      setLocalSlug(pageDetail.slug ?? '');
      setLocalIsPublic(pageDetail.isPublic ?? true);
      setLocalSupportEmail(pageDetail.supportEmail ?? '');
      setLocalAllowSubscriptions(pageDetail.allowSubscriptions ?? true);
      setInitialised(true);
    }
  }, [pageDetail, initialised]);

  const activeIncidents = useMemo(
    () => pageDetail?.incidents?.filter(i => i.status !== 'resolved') ?? [],
    [pageDetail?.incidents],
  );

  const [draggedComponentId, setDraggedComponentId] = useState<string | null>(null);
  const [dragOrder, setDragOrder] = useState<string[] | null>(null);

  const components: StatusPageComponentDto[] = useMemo(
    () => orderComponents(pageDetail?.components ?? [], dragOrder),
    [pageDetail, dragOrder],
  );

  if (isPagesLoading) {
    return (
      <div className="p-6 flex items-center justify-center min-h-[300px]">
        <div className="flex flex-col items-center gap-3 text-muted-foreground">
          <Loader2 className="w-8 h-8 animate-spin text-brand-500" />
          <p className="text-sm">{t('statusPage.loadingStatusPage')}</p>
        </div>
      </div>
    );
  }

  if (statusPages && statusPages.length === 0) {
    return (
      <div className="p-6 space-y-6">
        <div>
          <h1 style={{ fontSize: '1.875rem', fontWeight: 600 }}>{t('statusPage.title')}</h1>
          <p style={{ fontSize: '0.875rem', color: '#94A3B8', marginTop: '0.25rem' }}>
            {t('statusPage.description')}
          </p>
        </div>
        <Card className="p-12 flex flex-col items-center text-center gap-4 bg-card/80">
          <Globe className="w-16 h-16 text-brand-500/40" />
          <h2 style={{ fontSize: '1.5rem', fontWeight: 600 }}>{t('statusPage.emptyStatusTitle')}</h2>
          <p style={{ fontSize: '0.875rem', color: '#94A3B8', maxWidth: '400px' }}>
            {t('statusPage.emptyStatusDesc')}
          </p>
          <Button
            className="bg-brand-500 hover:bg-brand-600 text-white mt-2"
            disabled={createPageMutation.isPending}
            onClick={() =>
              createPageMutation.mutate(
                {
                  name: t('statusPage.defaultNewPageName'),
                  slug: `status-${Date.now().toString(36)}`,
                  description: t('statusPage.defaultNewPageDescription'),
                },
                { onError: () => toast.error(t("statusPage.toastCreatePageFailed")) },
              )
            }
          >
            {createPageMutation.isPending ? (
              <><Loader2 className="w-4 h-4 mr-2 animate-spin" />{t('statusPage.creatingStatusPage')}</>
            ) : (
              <><Plus className="w-4 h-4 mr-2" />{t('statusPage.createStatusPageBtn')}</>
            )}
          </Button>
        </Card>
      </div>
    );
  }

  const handleComponentDragStart = (id: string) => {
    setDraggedComponentId(id);
    setDragOrder(components.map((c) => c.id));
  };

  const handleComponentDragOver = (e: React.DragEvent, targetId: string) => {
    e.preventDefault();
    if (!draggedComponentId || draggedComponentId === targetId || !dragOrder) return;
    const from = dragOrder.indexOf(draggedComponentId);
    const to = dragOrder.indexOf(targetId);
    if (from < 0 || to < 0) return;
    const next = [...dragOrder];
    next.splice(from, 1);
    next.splice(to, 0, draggedComponentId);
    setDragOrder(next);
  };

  const handleComponentDragEnd = () => {
    setDraggedComponentId(null);
    if (!dragOrder) return;
    const byId = new Map((pageDetail?.components ?? []).map((c) => [c.id, c]));
    dragOrder.forEach((id, idx) => {
      const comp = byId.get(id);
      if (comp && comp.displayOrder !== idx + 1) {
        updateComponentMutation.mutate({ componentId: id, displayOrder: idx + 1 });
      }
    });
  };
  const isSaving = updatePageMutation.isPending;

  const publicUrl = pageDetail
    ? `${window.location.origin}/status/${pageDetail.slug}`
    : `${window.location.origin}/status`;

  const handleCopyLink = async () => {
    await navigator.clipboard.writeText(publicUrl);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  const handlePreviewStatus = () => {
    const sep = publicUrl.includes('?') ? '&' : '?';
    const withLang = `${publicUrl}${sep}lang=${getLocale()}`;
    window.open(withLang, '_blank', 'noopener,noreferrer');
  };

  const handleUpdateComponentStatus = (componentId: string, status: string) => {
    updateComponentMutation.mutate({ componentId, status });
  };

  const handleSaveHealthCheck = (payload: HealthCheckPayload) => {
    updateComponentMutation.mutate(payload, {
      onSuccess: () => {
        toast.success(t("statusPage.toastHealthCheckSaved"));
        setHcDialogComponent(null);
      },
      onError: () => toast.error(t("statusPage.toastHealthCheckSaveFailed")),
    });
  };

  const handleCreateIncident = () => {
    if (!currentPageId || !incidentTitle.trim()) return;
    createIncidentMutation.mutate(
      { pageId: currentPageId, title: incidentTitle.trim(), status: 'investigating', impact: 'major' },
      {
        onSuccess: () => {
          toast.success(t('statusPage.incidentCreated'));
          setIsIncidentModalOpen(false);
          setIncidentTitle('');
          setIncidentMessage('');
        },
        onError: () => toast.error(t('statusPage.incidentFailed')),
      },
    );
  };

  const handleScheduleMaintenance = () => {
    if (!currentPageId || !incidentTitle.trim()) return;
    createIncidentMutation.mutate(
      { pageId: currentPageId, title: incidentTitle.trim(), status: 'scheduled', impact: 'maintenance' },
      {
        onSuccess: () => {
          toast.success(t('statusPage.maintenanceScheduled'));
          setIsMaintenanceModalOpen(false);
          setIncidentTitle('');
          setIncidentMessage('');
        },
        onError: () => toast.error(t('statusPage.maintenanceFailed')),
      },
    );
  };

  const handleMarkAllOperational = () => {
    if (!components.length) return;
    const nonOperational = components.filter(c => c.status !== 'operational');
    if (!nonOperational.length) {
      toast.info(t('statusPage.allOperational'));
      return;
    }
    nonOperational.forEach(c =>
      updateComponentMutation.mutate(
        { componentId: c.id, status: 'operational' },
        { onError: () => toast.error(t('statusPage.failedUpdate', { name: c.name })) },
      ),
    );
    toast.success(t('statusPage.markingOperational', { count: String(nonOperational.length) }));
  };

  const handleSave = async () => {
    if (!currentPageId) return;
    setSuccess('');
    try {
      await updatePageMutation.mutateAsync({
        id: currentPageId,
        name: localName,
        slug: localSlug || undefined,
        isPublic: localIsPublic,
        description: pageDetail?.description,
        supportEmail: localSupportEmail || undefined,
        allowSubscriptions: localAllowSubscriptions,
      });
      setSuccess(t('statusPage.saveSuccess'));
      setTimeout(() => setSuccess(''), 3000);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : t('statusPage.saveFailed'));
    }
  };

  const handleAddIncidentUpdate = (incidentId: string) => {
    if (!updateMessage.trim()) return;
    addIncidentUpdateMutation.mutate(
      { incidentId, message: updateMessage.trim(), status: updateStatus },
      {
        onSuccess: () => {
          toast.success(t("statusPage.toastUpdateAdded"));
          setUpdateMessage('');
          setExpandedIncidentId(null);
        },
        onError: () => toast.error(t("statusPage.toastUpdateAddFailed")),
      },
    );
  };

  const visibleComponentsCount = components.length;

  return (
    <div className="p-6 space-y-6">
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
        <div>
          <h1 style={{ fontSize: '1.875rem', fontWeight: 600 }}>{t('statusPage.title')}</h1>
          <p style={{ fontSize: '0.875rem', color: '#94A3B8', marginTop: '0.25rem' }}>
            {t('statusPage.description')}
          </p>
        </div>
        <div className="flex gap-2">
          <Button
            variant="outline"
            className="bg-input-background"
            onClick={handlePreviewStatus}
          >
            <Eye className="w-4 h-4 mr-2" />
            {t('common.preview')}
          </Button>
          <Button
            variant="ghost"
            className="text-error-400"
            aria-label={t('statusPage.deletePageAria')}
            onClick={() => setIsDeletingPage(true)}
          >
            <Trash2 className="w-4 h-4" />
          </Button>
          <Button
            onClick={handleSave}
            disabled={isSaving}
            className="bg-brand-500 hover:bg-brand-600 text-white"
          >
            {isSaving ? (
              <>
                <Loader2 className="w-4 h-4 mr-2 animate-spin" />
                {t('common.saving')}
              </>
            ) : (
              <>
                <Save className="w-4 h-4 mr-2" />
                {t('common.saveChanges')}
              </>
            )}
          </Button>
        </div>
      </div>

      {success && (
        <Alert className="border-success-500 bg-success-500/10">
          <Check className="h-4 w-4 text-success-500" />
          <AlertDescription className="text-success-500">{success}</AlertDescription>
        </Alert>
      )}

      <div className="grid grid-cols-1 xl:grid-cols-3 gap-6">
        <div className="xl:col-span-2 space-y-6">
          <Card className="p-6 bg-card/80 backdrop-blur-sm">
            <div className="flex items-center gap-3 mb-6">
              <Settings className="w-5 h-5 text-brand-500" />
              <h2 style={{ fontSize: '1.125rem', fontWeight: 600 }}>{t('statusPage.generalSettings')}</h2>
            </div>

            <div className="space-y-4">
              <div className="flex items-center justify-between p-4 rounded-lg bg-surface-light/20 border border-border">
                <div>
                  <p style={{ fontSize: '0.9375rem', fontWeight: 600 }}>{t('statusPage.enablePublic')}</p>
                  <p style={{ fontSize: '0.8125rem', color: '#94A3B8', marginTop: '0.25rem' }}>
                    {t('statusPage.enablePublicDesc')}
                  </p>
                </div>
                <Switch
                  checked={localIsPublic}
                  aria-label={t("statusPage.ariaPublicToggle")}
                  onCheckedChange={(checked) => setLocalIsPublic(checked)}
                />
              </div>

              <div className="space-y-2">
                <Label htmlFor="companyName">{t('statusPage.companyName')}</Label>
                <Input
                  id="companyName"
                  value={localName}
                  onChange={(e) => setLocalName(e.target.value)}
                  className="bg-input-background"
                />
              </div>

              <div className="space-y-2">
                <Label htmlFor="slug">{t('statusPage.slugLabel')}</Label>
                <Input
                  id="slug"
                  value={localSlug}
                  onChange={(e) => setLocalSlug(e.target.value)}
                  className="bg-input-background"
                />
                <p style={{ fontSize: '0.75rem', color: '#94A3B8' }}>
                  {t('statusPage.slugDesc')}
                </p>
              </div>

              <div className="space-y-2">
                <Label htmlFor="supportEmail">{t('statusPage.supportEmail')}</Label>
                <Input
                  id="supportEmail"
                  type="email"
                  placeholder={t("statusPage.supportEmailFieldPlaceholder")}
                  value={localSupportEmail}
                  onChange={(e) => setLocalSupportEmail(e.target.value)}
                  className="bg-input-background"
                />
                <p style={{ fontSize: '0.75rem', color: '#94A3B8' }}>
                  {t('statusPage.supportEmailDesc')}
                </p>
              </div>

              <div className="flex items-center justify-between p-4 rounded-lg bg-surface-light/20 border border-border">
                <div>
                  <p style={{ fontSize: '0.9375rem', fontWeight: 600 }}>{t('statusPage.emailSubscriptions')}</p>
                  <p style={{ fontSize: '0.8125rem', color: '#94A3B8', marginTop: '0.25rem' }}>
                    {t('statusPage.emailSubscriptionsDesc')}
                  </p>
                </div>
                <Switch
                  checked={localAllowSubscriptions}
                  aria-label={t("statusPage.ariaSubscriptionsToggle")}
                  onCheckedChange={(checked) => setLocalAllowSubscriptions(checked)}
                />
              </div>
            </div>
          </Card>

          <Card className="p-6 bg-card/80 backdrop-blur-sm">
            <div className="flex items-center justify-between mb-6">
              <div className="flex items-center gap-3">
                <Globe className="w-5 h-5 text-brand-500" />
                <h2 style={{ fontSize: '1.125rem', fontWeight: 600 }}>{t('statusPage.serviceVisibility')}</h2>
                <Badge variant="outline">
                  {visibleComponentsCount === 1
                    ? t('statusPage.componentsCountOne', { count: String(visibleComponentsCount) })
                    : t('statusPage.componentsCountMany', { count: String(visibleComponentsCount) })}
                </Badge>
              </div>
              <Button
                size="sm"
                onClick={() => {
                  setAddComponentServiceId('');
                  setAddComponentName('');
                  setAddComponentDesc('');
                  setIsAddComponentModalOpen(true);
                }}
                disabled={!currentPageId}
                className="bg-brand-500 hover:bg-brand-600 text-white"
              >
                <Plus className="w-4 h-4 mr-2" />
                {t('statusPage.addService')}
              </Button>
            </div>

            <div className="space-y-3">
              {components.map((component) => (
                <div
                  key={component.id}
                  draggable
                  onDragStart={() => handleComponentDragStart(component.id)}
                  onDragOver={(e) => handleComponentDragOver(e, component.id)}
                  onDragEnd={handleComponentDragEnd}
                  className={`flex items-center gap-4 p-4 rounded-lg bg-surface-light/20 border border-border hover:border-brand-500/30 transition-all ${draggedComponentId === component.id ? 'opacity-50' : ''}`}
                >
                  <GripVertical className="w-4 h-4 text-muted-foreground cursor-grab active:cursor-grabbing" />

                  <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-2 mb-1">
                      <p style={{ fontSize: '0.9375rem', fontWeight: 600 }}>{component.name}</p>
                      {component.healthCheckEnabled && (
                        <Badge variant="outline" className="text-xs gap-1">
                          <Zap className="w-3 h-3" />
                          {component.lastHealthCheckResponseMs != null
                            ? `${component.lastHealthCheckResponseMs}ms`
                            : t('statusPage.healthCheckAbbr')}
                        </Badge>
                      )}
                    </div>
                    <div className="flex items-center gap-2">
                      <p style={{ fontSize: '0.8125rem', color: '#94A3B8' }}>{component.description}</p>
                      {component.healthCheckEnabled && component.lastHealthCheckAt && (
                        <span style={{ fontSize: '0.7rem', color: '#64748B' }}>
                          · {t('statusPage.lastCheckAt')}{' '}
                          {new Date(component.lastHealthCheckAt).toLocaleTimeString(dateLocale())}
                        </span>
                      )}
                      {component.healthCheckConsecutiveFailures > 0 && (
                        <Badge className="bg-error-500/10 text-error-500 text-xs">
                          {component.healthCheckConsecutiveFailures === 1
                            ? t('statusPage.failCountOne', {
                                count: String(component.healthCheckConsecutiveFailures),
                              })
                            : t('statusPage.failCountMany', {
                                count: String(component.healthCheckConsecutiveFailures),
                              })}
                        </Badge>
                      )}
                    </div>
                  </div>

                  <button
                    onClick={() => setHcDialogComponent(component)}
                    className="p-2 rounded-lg hover:bg-surface-light transition-colors"
                    aria-label={t('statusPage.ariaHealthCheckFor', { name: component.name })}
                  >
                    <HeartPulse className="w-4 h-4 text-muted-foreground" />
                  </button>

                  <button
                    onClick={() => removeComponentMutation.mutate(component.id, {
                      onSuccess: () =>
                        toast.success(
                          t("statusPage.toastComponentRemoved", { name: component.name }),
                        ),
                      onError: () =>
                        toast.error(t("statusPage.toastComponentRemoveFailed", { name: component.name })),
                    })}
                    className="p-2 rounded-lg hover:bg-error-500/10 transition-colors"
                    aria-label={t('statusPage.ariaRemoveComponent', { name: component.name })}
                    disabled={removeComponentMutation.isPending}
                  >
                    <Trash2 className="w-4 h-4 text-muted-foreground hover:text-error-500" />
                  </button>

                  <Select
                    value={component.status}
                    onValueChange={(value) => handleUpdateComponentStatus(component.id, value)}
                  >
                    <SelectTrigger
                      className="w-[140px] bg-input-background"
                      aria-label={`${t('common.status')} — ${component.name}`}
                    >
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="operational">{t('statusPage.operational')}</SelectItem>
                      <SelectItem value="degraded">{t('statusPage.degraded')}</SelectItem>
                      <SelectItem value="partial_outage">{t('statusPage.partialOutage')}</SelectItem>
                      <SelectItem value="major_outage">{t('statusPage.majorOutage')}</SelectItem>
                      <SelectItem value="maintenance">{t('statusPage.maintenance')}</SelectItem>
                    </SelectContent>
                  </Select>
                </div>
              ))}
            </div>

            {components.length === 0 && (
              <div className="text-center py-8">
                <Globe className="w-10 h-10 text-muted-foreground mx-auto mb-3 opacity-50" />
                <p style={{ fontSize: '0.875rem', fontWeight: 600, marginBottom: '0.25rem' }}>
                  {t('statusPage.noComponents')}
                </p>
                <p style={{ fontSize: '0.8125rem', color: '#94A3B8' }}>
                  {t('statusPage.noComponentsDesc')}
                </p>
              </div>
            )}

            <p style={{ fontSize: '0.75rem', color: '#94A3B8', marginTop: '1rem' }}>
              {t('statusPage.dragTip')}
            </p>
          </Card>

          {activeIncidents.length > 0 && (
            <Card className="p-6 bg-card/80 backdrop-blur-sm border-error-500/20">
              <div className="flex items-center gap-3 mb-4">
                <AlertTriangle className="w-5 h-5 text-error-500" />
                <h2 style={{ fontSize: '1.125rem', fontWeight: 600 }}>{t('statusPage.activeIncidentsManage')}</h2>
                <Badge className="bg-error-500/10 text-error-500 text-xs">
                  {t('statusPage.activeCountBadge', { count: String(activeIncidents.length) })}
                </Badge>
              </div>
              <div className="space-y-3">
                {activeIncidents.map(incident => (
                  <div key={incident.id} className="rounded-lg border border-error-500/20 bg-error-500/5 overflow-hidden">
                    <div
                      className="flex items-center justify-between p-4 cursor-pointer hover:bg-error-500/10 transition-colors"
                      tabIndex={0}
                      role="button"
                      aria-expanded={expandedIncidentId === incident.id}
                      onClick={() => setExpandedIncidentId(expandedIncidentId === incident.id ? null : incident.id)}
                      onKeyDown={(e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); setExpandedIncidentId(expandedIncidentId === incident.id ? null : incident.id); } }}
                    >
                      <div className="flex-1">
                        <p style={{ fontSize: '0.9375rem', fontWeight: 600 }}>{incident.title}</p>
                        <div className="flex items-center gap-2 mt-1">
                          <Badge className="text-xs bg-warning-500/10 text-warning-500">
                            {incidentStatusLabelManage(incident.status)}
                          </Badge>
                          {incident.impact && (
                            <Badge variant="outline" className="text-xs">{incident.impact}</Badge>
                          )}
                          <span style={{ fontSize: '0.75rem', color: '#64748B' }}>
                            {new Date(incident.createdAt).toLocaleDateString(dateLocale())}
                          </span>
                        </div>
                      </div>
                      <div className="flex items-center gap-2">
                        <span style={{ fontSize: '0.75rem', color: '#94A3B8' }}>
                          {(incident.updates?.length ?? 0) === 1
                            ? t('statusPage.updateCountOne', {
                                count: String(incident.updates?.length ?? 0),
                              })
                            : t('statusPage.updateCountMany', {
                                count: String(incident.updates?.length ?? 0),
                              })}
                        </span>
                        {expandedIncidentId === incident.id ? (
                          <ChevronUp className="w-4 h-4 text-muted-foreground" />
                        ) : (
                          <ChevronDown className="w-4 h-4 text-muted-foreground" />
                        )}
                      </div>
                    </div>
                    {expandedIncidentId === incident.id && (
                      <div className="border-t border-error-500/20 p-4 space-y-3">
                        {incident.updates && incident.updates.length > 0 && (
                          <div className="space-y-2 mb-3">
                            {incident.updates.map((upd, idx) => (
                              <div key={idx} className="flex gap-3 text-sm">
                                <div className="w-2 h-2 rounded-full bg-brand-500 mt-1.5 flex-shrink-0" />
                                <div>
                                  <p style={{ fontSize: '0.8125rem', color: '#CBD5E1' }}>{upd.message}</p>
                                  <p style={{ fontSize: '0.75rem', color: '#64748B' }}>
                                    {incidentStatusLabelManage(upd.status)} ·{' '}
                                    {new Date(upd.createdAt).toLocaleString(dateLocale())}
                                  </p>
                                </div>
                              </div>
                            ))}
                          </div>
                        )}
                        <div className="flex gap-2">
                          <select
                            value={updateStatus}
                            onChange={e => setUpdateStatus(e.target.value)}
                            className="px-3 py-2 rounded-lg bg-input-background border border-border text-sm"
                          >
                            <option value="investigating">
                              {t('statusPage.incidentStatus.investigating')}
                            </option>
                            <option value="identified">{t('statusPage.incidentStatus.identified')}</option>
                            <option value="monitoring">{t('statusPage.incidentStatus.monitoring')}</option>
                            <option value="resolved">{t('statusPage.incidentStatus.resolved')}</option>
                          </select>
                          <Input
                            value={updateMessage}
                            onChange={e => setUpdateMessage(e.target.value)}
                            placeholder={t("statusPage.latestUpdatePlaceholder")}
                            className="bg-input-background flex-1"
                            onKeyDown={e => e.key === 'Enter' && handleAddIncidentUpdate(incident.id)}
                          />
                          <Button
                            size="sm"
                            onClick={() => handleAddIncidentUpdate(incident.id)}
                            disabled={!updateMessage.trim() || addIncidentUpdateMutation.isPending}
                            className="bg-brand-500 hover:bg-brand-600 text-white"
                          >
                            {addIncidentUpdateMutation.isPending ? (
                              <Loader2 className="w-4 h-4 animate-spin" />
                            ) : (
                              <Plus className="w-4 h-4" />
                            )}
                          </Button>
                        </div>
                        <div className="flex items-center justify-between gap-3 pt-3 border-t border-error-500/10">
                          <p style={{ fontSize: '0.75rem', color: '#64748B' }}>
                            {t('statusPage.notifySubscribersHint')}
                          </p>
                          <Button
                            size="sm"
                            variant="outline"
                            onClick={() => notifySubscribersMutation.mutate(incident.id)}
                            disabled={notifySubscribersMutation.isPending}
                            className="bg-input-background shrink-0"
                          >
                            {notifySubscribersMutation.isPending ? (
                              <Loader2 className="w-4 h-4 animate-spin mr-2" />
                            ) : (
                              <Mail className="w-4 h-4 mr-2" />
                            )}
                            {t('statusPage.notifySubscribers')}
                          </Button>
                        </div>
                      </div>
                    )}
                  </div>
                ))}
              </div>
            </Card>
          )}
        </div>

        <div className="space-y-6">
          <Card className="p-6 bg-card/80 backdrop-blur-sm">
            <div className="flex items-center gap-3 mb-4">
              <LinkIcon className="w-5 h-5 text-brand-500" />
              <h2 style={{ fontSize: '1.125rem', fontWeight: 600 }}>{t('statusPage.publicLink')}</h2>
            </div>

            <div className="space-y-3">
              <div className="p-3 rounded-lg bg-surface-light/20 border border-border">
                <p style={{ fontSize: '0.75rem', color: '#94A3B8', marginBottom: '0.5rem' }}>
                  {t('statusPage.liveAt')}
                </p>
                <div className="flex items-center gap-2">
                  <code
                    className="flex-1 text-xs bg-background/50 px-2 py-1 rounded border border-border overflow-x-auto"
                    style={{ fontSize: '0.75rem', fontFamily: 'monospace' }}
                  >
                    {publicUrl}
                  </code>
                  <button
                    onClick={handleCopyLink}
                    className="p-2 rounded hover:bg-surface-light transition-colors"
                    aria-label={t("statusPage.copyLinkAria")}
                  >
                    {copied ? (
                      <Check className="w-4 h-4 text-success-500" />
                    ) : (
                      <Copy className="w-4 h-4 text-muted-foreground" />
                    )}
                  </button>
                </div>
              </div>

              <button
                onClick={handlePreviewStatus}
                className="flex items-center justify-center gap-2 w-full px-4 py-2 rounded-lg bg-brand-500/10 border border-brand-500/20 hover:bg-brand-500/20 transition-all cursor-pointer"
                style={{ fontSize: '0.875rem', color: '#3E7BFA' }}
              >
                <ExternalLink className="w-4 h-4" />
                {t('statusPage.openPublicPage')}
              </button>
            </div>
          </Card>

          <Card className="p-6 bg-card/80 backdrop-blur-sm">
            <h2 style={{ fontSize: '1.125rem', fontWeight: 600, marginBottom: '1rem' }}>
              {t('statusPage.quickActions')}
            </h2>

            <div className="space-y-2">
              <Button
                variant="outline"
                className="w-full justify-start bg-input-background"
                onClick={() => {
                  setIncidentTitle('');
                  setIncidentMessage('');
                  setIsIncidentModalOpen(true);
                }}
                disabled={!currentPageId}
              >
                <Plus className="w-4 h-4 mr-2" />
                {t('statusPage.createIncident')}
              </Button>

              <Button
                variant="outline"
                className="w-full justify-start bg-input-background"
                onClick={() => {
                  setIncidentTitle('');
                  setIncidentMessage('');
                  setIsMaintenanceModalOpen(true);
                }}
                disabled={!currentPageId}
              >
                <Settings className="w-4 h-4 mr-2" />
                {t('statusPage.scheduleMaintenance')}
              </Button>

              <Button
                variant="outline"
                className="w-full justify-start bg-input-background text-error-500 hover:text-error-600"
                onClick={handleMarkAllOperational}
                disabled={!components.length || updateComponentMutation.isPending}
              >
                <Check className="w-4 h-4 mr-2" />
                {t('statusPage.markAllOperational')}
              </Button>
            </div>
          </Card>

          <Card className="p-6 bg-gradient-to-br from-brand-500/5 to-purple-500/5 border-brand-500/20">
            <h2 style={{ fontSize: '1.125rem', fontWeight: 600, marginBottom: '1rem' }}>
              {t('statusPage.statistics')}
            </h2>

            <div className="space-y-3">
              <div className="flex items-center justify-between">
                <span style={{ fontSize: '0.875rem', color: '#94A3B8' }}>{t('statusPage.pageViews')}</span>
                <span style={{ fontSize: '0.9375rem', fontWeight: 600 }}>
                  {stats?.pageViews?.toLocaleString() ?? '—'}
                </span>
              </div>
              <div className="flex items-center justify-between">
                <span style={{ fontSize: '0.875rem', color: '#94A3B8' }}>{t('statusPage.subscribers')}</span>
                <span style={{ fontSize: '0.9375rem', fontWeight: 600 }}>
                  {stats?.subscriberCount?.toLocaleString() ?? '—'}
                </span>
              </div>
              <div className="flex items-center justify-between">
                <span style={{ fontSize: '0.875rem', color: '#94A3B8' }}>{t('statusPage.components')}</span>
                <span style={{ fontSize: '0.9375rem', fontWeight: 600 }}>
                  {stats?.componentCount ?? components.length}
                </span>
              </div>
              <div className="flex items-center justify-between">
                <span style={{ fontSize: '0.875rem', color: '#94A3B8' }}>{t('statusPage.activeIncidents')}</span>
                <span style={{ fontSize: '0.9375rem', fontWeight: 600, color: stats?.activeIncidentCount ? '#EF4444' : '#22C55E' }}>
                  {stats?.activeIncidentCount ?? 0}
                </span>
              </div>
            </div>
          </Card>
        </div>
      </div>

      {localAllowSubscriptions && (
        <Card className="p-6 bg-card/80 backdrop-blur-sm">
          <div className="flex items-center justify-between mb-5">
            <div className="flex items-center gap-3">
              <Mail className="w-5 h-5 text-brand-500" />
              <h2 style={{ fontSize: '1.125rem', fontWeight: 600 }}>{t('statusPage.emailSubscribersTitle')}</h2>
              <span
                style={{
                  fontSize: '0.75rem',
                  background: 'rgba(62,123,250,0.1)',
                  color: '#3E7BFA',
                  padding: '2px 8px',
                  borderRadius: '999px',
                  border: '1px solid rgba(62,123,250,0.2)',
                }}
              >
                {t('statusPage.subscribersTotalBadge', { count: String(subscriberList.length) })}
              </span>
            </div>
          </div>

          {isSubscribersLoading ? (
            <div style={{ textAlign: 'center', padding: '24px 0' }}>
              <Loader2 className="w-5 h-5 animate-spin text-brand-500 mx-auto" />
            </div>
          ) : subscriberList.length === 0 ? (
            <div style={{ textAlign: 'center', padding: '32px 0' }}>
              <Mail className="w-10 h-10 text-muted-foreground mx-auto mb-3 opacity-40" />
              <p style={{ fontSize: '0.875rem', color: '#64748B' }}>{t('statusPage.noSubscribersYet')}</p>
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full">
                <thead>
                  <tr style={{ borderBottom: '1px solid rgba(148,163,184,0.1)' }}>
                    <th style={{ fontSize: '0.75rem', fontWeight: 600, textAlign: 'left', padding: '0.5rem 0', color: '#64748B' }}>
                      {t('statusPage.subscriberColEmail')}
                    </th>
                    <th style={{ fontSize: '0.75rem', fontWeight: 600, textAlign: 'left', padding: '0.5rem 0', color: '#64748B' }}>
                      {t('statusPage.subscriberColStatus')}
                    </th>
                    <th style={{ fontSize: '0.75rem', fontWeight: 600, textAlign: 'left', padding: '0.5rem 0', color: '#64748B' }}>
                      {t('statusPage.subscriberColSubscribed')}
                    </th>
                    <th style={{ padding: '0.5rem 0' }}></th>
                  </tr>
                </thead>
                <tbody>
                  {subscriberList.map((sub) => (
                    <tr key={sub.id} style={{ borderBottom: '1px solid rgba(148,163,184,0.06)' }}>
                      <td style={{ padding: '0.75rem 0', fontSize: '0.875rem' }}>{sub.email}</td>
                      <td style={{ padding: '0.75rem 0' }}>
                        <span
                          style={{
                            fontSize: '0.75rem',
                            padding: '2px 8px',
                            borderRadius: '999px',
                            background: sub.isConfirmed ? 'rgba(34,197,94,0.1)' : 'rgba(245,158,11,0.1)',
                            color: sub.isConfirmed ? '#22C55E' : '#F59E0B',
                            border: `1px solid ${sub.isConfirmed ? 'rgba(34,197,94,0.2)' : 'rgba(245,158,11,0.2)'}`,
                          }}
                        >
                          {sub.isConfirmed ? t('statusPage.subscriberConfirmed') : t('statusPage.subscriberPending')}
                        </span>
                      </td>
                      <td style={{ padding: '0.75rem 0', fontSize: '0.8125rem', color: '#94A3B8' }}>
                        {new Date(sub.subscribedAt).toLocaleDateString()}
                      </td>
                      <td style={{ padding: '0.75rem 0', textAlign: 'right' }}>
                        <button
                          onClick={() =>
                            currentPageId &&
                            removeSubscriberMutation.mutate({ pageId: currentPageId, email: sub.email })
                          }
                          disabled={removeSubscriberMutation.isPending}
                          className="p-1.5 rounded hover:bg-error-500/10 transition-colors"
                          title={t("statusPage.removeSubscriberTitle")}
                        >
                          <Trash2 className="w-4 h-4 text-muted-foreground hover:text-error-500" />
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </Card>
      )}

      <Card className="p-6 bg-card/80 backdrop-blur-sm">
        <div className="flex items-center justify-between mb-6">
          <div className="flex items-center gap-3">
            <svg className="w-5 h-5 text-brand-500" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2}>
              <path d="M22 12h-4l-3 9L9 3l-3 9H2" />
            </svg>
            <h2 style={{ fontSize: '1.125rem', fontWeight: 600 }}>{t('statusPage.uptimeHistoryManage')}</h2>
            <span
              style={{
                fontSize: '0.75rem',
                color: '#94A3B8',
                background: 'rgba(148,163,184,0.1)',
                padding: '2px 8px',
                borderRadius: '999px',
                border: '1px solid rgba(148,163,184,0.2)',
              }}
            >
              {t('statusPage.publicLast30Days')}
            </span>
          </div>
          {uptimeData && uptimeData.length > 0 && (
            <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
              <div style={{ width: '8px', height: '8px', borderRadius: '50%', backgroundColor: '#22C55E' }} />
              <span style={{ fontSize: '0.8125rem', color: '#94A3B8' }}>
                {t('statusPage.uptimeAvgLabel')}{' '}
                <strong style={{ color: '#22C55E' }}>
                  {(
                    uptimeData.reduce((sum, c) => sum + c.averageUptimePercent, 0) / uptimeData.length
                  ).toFixed(2)}%
                </strong>
              </span>
            </div>
          )}
        </div>
        <UptimeGraph components={uptimeData ?? []} isLoading={isUptimeLoading} />
      </Card>

      <Dialog open={isIncidentModalOpen} onOpenChange={setIsIncidentModalOpen}>
        <DialogContent className="bg-card border-border sm:max-w-[480px]">
          <DialogHeader>
            <DialogTitle style={{ fontSize: '1.25rem', fontWeight: 600 }}>
              {t('statusPage.createIncident')}
            </DialogTitle>
          </DialogHeader>

          <div className="space-y-4 py-2">
            <div>
              <Label htmlFor="incident-title">{t('statusPage.incidentTitle')}</Label>
              <Input
                id="incident-title"
                value={incidentTitle}
                onChange={(e) => setIncidentTitle(e.target.value)}
                placeholder={t("statusPage.incidentPlaceholder")}
                className="bg-input-background mt-1"
                autoFocus
              />
            </div>
            <div>
              <Label htmlFor="incident-message">{t('statusPage.initialMessage')}</Label>
              <Textarea
                id="incident-message"
                value={incidentMessage}
                onChange={(e) => setIncidentMessage(e.target.value)}
                placeholder={t("statusPage.describeIncident")}
                rows={3}
                className="bg-input-background mt-1 resize-none"
              />
            </div>
            <div className="p-3 rounded-lg bg-warning-500/10 border border-warning-500/20">
              <p style={{ fontSize: '0.8125rem', color: '#F59E0B' }}>{t('statusPage.createIncidentStatusHint')}</p>
            </div>
          </div>

          <DialogFooter>
            <Button
              variant="outline"
              onClick={() => setIsIncidentModalOpen(false)}
              className="bg-input-background"
            >
              {t('common.cancel')}
            </Button>
            <Button
              onClick={handleCreateIncident}
              disabled={!incidentTitle.trim() || createIncidentMutation.isPending}
              className="bg-error-600 hover:bg-error-700 text-white"
            >
              {createIncidentMutation.isPending ? (
                <>
                  <Loader2 className="w-4 h-4 mr-2 animate-spin" />
                  {t('common.creating')}
                </>
              ) : (
                t('statusPage.createIncidentBtn')
              )}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog open={isMaintenanceModalOpen} onOpenChange={setIsMaintenanceModalOpen}>
        <DialogContent className="bg-card border-border sm:max-w-[480px]">
          <DialogHeader>
            <DialogTitle style={{ fontSize: '1.25rem', fontWeight: 600 }}>
              {t('statusPage.scheduleMaintenance')}
            </DialogTitle>
          </DialogHeader>

          <div className="space-y-4 py-2">
            <div>
              <Label htmlFor="maintenance-title">{t('statusPage.maintenanceTitle')}</Label>
              <Input
                id="maintenance-title"
                value={incidentTitle}
                onChange={(e) => setIncidentTitle(e.target.value)}
                placeholder={t("statusPage.maintenancePlaceholder")}
                className="bg-input-background mt-1"
                autoFocus
              />
            </div>
            <div>
              <Label htmlFor="maintenance-message">{t('statusPage.maintenanceDetails')}</Label>
              <Textarea
                id="maintenance-message"
                value={incidentMessage}
                onChange={(e) => setIncidentMessage(e.target.value)}
                placeholder={t("statusPage.describeMaintenance")}
                rows={3}
                className="bg-input-background mt-1 resize-none"
              />
            </div>
            <div className="p-3 rounded-lg bg-brand-500/10 border border-brand-500/20">
              <p style={{ fontSize: '0.8125rem', color: '#94A3B8' }}>{t('statusPage.maintenanceScheduleHint')}</p>
            </div>
          </div>

          <DialogFooter>
            <Button
              variant="outline"
              onClick={() => setIsMaintenanceModalOpen(false)}
              className="bg-input-background"
            >
              {t('common.cancel')}
            </Button>
            <Button
              onClick={handleScheduleMaintenance}
              disabled={!incidentTitle.trim() || createIncidentMutation.isPending}
              className="bg-brand-500 hover:bg-brand-600 text-white"
            >
              {createIncidentMutation.isPending ? (
                <>
                  <Loader2 className="w-4 h-4 mr-2 animate-spin" />
                  {t('statusPage.scheduling')}
                </>
              ) : (
                t('statusPage.scheduleMaintenanceBtn')
              )}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <HealthCheckDialog
        component={hcDialogComponent}
        onClose={() => setHcDialogComponent(null)}
        onSave={handleSaveHealthCheck}
        isSaving={updateComponentMutation.isPending}
      />

      <Dialog open={isAddComponentModalOpen} onOpenChange={setIsAddComponentModalOpen}>
        <DialogContent className="bg-card border-border sm:max-w-[480px]">
          <DialogHeader>
            <DialogTitle style={{ fontSize: '1.25rem', fontWeight: 600 }}>
              {t('statusPage.addServiceModalTitle')}
            </DialogTitle>
          </DialogHeader>

          <div className="space-y-4 py-2">
            {(() => {
              const existingServiceIds = new Set(components.map(c => c.serviceId).filter(Boolean));
              const availableServices = allServices.filter(s => !existingServiceIds.has(s.id));
              return availableServices.length > 0 ? (
                <div>
                  <Label htmlFor={`${formId}-add-component-service`}>{t('statusPage.selectServiceLabel')}</Label>
                  <Select value={addComponentServiceId} onValueChange={(val) => {
                    setAddComponentServiceId(val);
                    const svc = allServices.find(s => s.id === val);
                    if (svc) {
                      setAddComponentName(svc.name);
                      setAddComponentDesc(svc.description ?? '');
                    }
                  }}>
                    <SelectTrigger id={`${formId}-add-component-service`} className="bg-input-background mt-1">
                      <SelectValue placeholder={t("statusPage.chooseServicePlaceholder")} />
                    </SelectTrigger>
                    <SelectContent>
                      {availableServices.filter(s => s.id).map((service) => (
                        <SelectItem key={service.id} value={service.id}>
                          {service.name}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>
              ) : (
                <div className="p-3 rounded-lg bg-warning-500/10 border border-warning-500/20">
                  <p style={{ fontSize: '0.8125rem', color: '#F59E0B' }}>
                    {t('statusPage.allServicesOnPageWarning')}
                  </p>
                </div>
              );
            })()}

            <div>
              <Label>{t('statusPage.displayNameLabel')}</Label>
              <Input
                value={addComponentName}
                onChange={(e) => setAddComponentName(e.target.value)}
                placeholder={t("statusPage.serviceNameExamplePlaceholder")}
                className="bg-input-background mt-1"
              />
            </div>

            <div>
              <Label>{t('statusPage.descriptionOptionalLabel')}</Label>
              <Input
                value={addComponentDesc}
                onChange={(e) => setAddComponentDesc(e.target.value)}
                placeholder={t("statusPage.serviceBriefDescriptionPlaceholder")}
                className="bg-input-background mt-1"
              />
            </div>
          </div>

          <DialogFooter>
            <Button
              variant="outline"
              onClick={() => setIsAddComponentModalOpen(false)}
              className="bg-input-background"
            >
              {t('common.cancel')}
            </Button>
            <Button
              onClick={() => {
                if (!currentPageId || !addComponentName.trim()) return;
                addComponentMutation.mutate(
                  {
                    pageId: currentPageId,
                    name: addComponentName.trim(),
                    description: addComponentDesc || undefined,
                    serviceId: addComponentServiceId || undefined,
                  },
                  {
                    onSuccess: () => {
                      toast.success(
                        t("statusPage.toastComponentAdded", { name: addComponentName }),
                      );
                      setIsAddComponentModalOpen(false);
                    },
                    onError: () => toast.error(t("statusPage.toastAddComponentFailed")),
                  },
                );
              }}
              disabled={!addComponentName.trim() || addComponentMutation.isPending}
              className="bg-brand-500 hover:bg-brand-600 text-white"
            >
              {addComponentMutation.isPending ? (
                <>
                  <Loader2 className="w-4 h-4 mr-2 animate-spin" />
                  {t('statusPage.addingService')}
                </>
              ) : (
                <>
                  <Plus className="w-4 h-4 mr-2" />
                  {t('statusPage.addToStatusPage')}
                </>
              )}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
      <DeleteConfirmDialog
        open={isDeletingPage}
        onOpenChange={setIsDeletingPage}
        title={t('statusPage.deletePageTitle')}
        message={t('statusPage.deletePageMsg', { name: localName || pageDetail?.name || '' })}
        warning={t('statusPage.deletePageWarn')}
        isLoading={deletePageMutation.isPending}
        onConfirm={() => {
          if (!currentPageId) return;
          deletePageMutation.mutate(currentPageId, { onSettled: () => setIsDeletingPage(false) });
        }}
      />

    </div>
  );
}