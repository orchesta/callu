/**
 * Public Status Page
 * Anonymous access - statuspage.io style
 * Shows real-time service health for public viewing
 */

import { useState, useEffect, useReducer } from 'react';
import { useParams, useSearchParams } from 'react-router';
import { CheckCircle, XCircle, AlertCircle, Clock, Activity, RefreshCw, Bell } from 'lucide-react';
import { Card } from '@/shared/components/ui/card';
import { Badge } from '@/shared/components/ui/badge';
import { useStatusPageBySlug, useSubscribeToStatusPage, useStatusPageUptime } from '../hooks/use-status-pages';
import { UptimeGraph } from './uptime-graph';
import { toast } from '@/shared/utils/toast';
import {
  t,
  getLocale,
  setLocale,
  onLocaleChange,
  SUPPORTED_LOCALES,
  type LocaleCode,
} from '@/shared/locales/i18n';
import type { StatusPageComponentDto, StatusPageIncidentDto } from '../types/status-page.types';

type ComponentStatus = 'operational' | 'degraded' | 'partial_outage' | 'major_outage' | 'maintenance';

export function PublicStatusPage() {
  const { slug = '' } = useParams<{ slug: string }>();
  const [searchParams, setSearchParams] = useSearchParams();
  const [, localeTick] = useReducer((n: number) => n + 1, 0);
  const { data: page, isLoading, refetch } = useStatusPageBySlug(slug);
  const [lastUpdated, setLastUpdated] = useState(new Date());
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [subscribeEmail, setSubscribeEmail] = useState('');
  const subscribeMutation = useSubscribeToStatusPage();
  const { data: uptimeData, isLoading: isUptimeLoading } = useStatusPageUptime(page?.id);

  useEffect(() => {
    return onLocaleChange(() => {
      localeTick();
    });
  }, []);

  useEffect(() => {
    const lang = searchParams.get('lang');
    if (lang === 'en' || lang === 'tr') {
      void setLocale(lang);
    }
  }, [searchParams]);

  const handleLocaleChange = async (code: LocaleCode) => {
    await setLocale(code);
    setSearchParams(
      (prev) => {
        const next = new URLSearchParams(prev);
        next.set('lang', code);
        return next;
      },
      { replace: true },
    );
  };

  useEffect(() => {
    const interval = setInterval(() => {
      refetch().then(() => setLastUpdated(new Date()));
    }, 30000);
    return () => clearInterval(interval);
  }, [refetch]);

  const handleRefresh = async () => {
    setIsRefreshing(true);
    await refetch();
    setLastUpdated(new Date());
    setIsRefreshing(false);
  };

  const handleSubscribe = async () => {
    if (!subscribeEmail.trim() || !page?.id) return;
    subscribeMutation.mutate(
      { pageId: page.id, email: subscribeEmail.trim() },
      {
        onSuccess: () => {
          toast.success(t('statusPage.publicSubscribeSuccess'));
          setSubscribeEmail('');
        },
        onError: () => toast.error(t('statusPage.publicSubscribeFailed')),
      },
    );
  };

  if (isLoading) {
    return (
      <div className="min-h-screen flex flex-col items-center justify-center gap-3 bg-background">
        <div className="w-6 h-6 border-2 border-brand-500/30 border-t-brand-500 rounded-full animate-spin" />
        <p className="text-sm text-muted-foreground">{t('statusPage.publicLoadingPage')}</p>
      </div>
    );
  }

  const components = page?.components ?? [];
  const incidents = page?.incidents?.filter((i) => i.status !== 'resolved') ?? [];
  const overallStatus = getOverallStatus(components);

  return (
    <div className="min-h-screen bg-background">
      <header className="border-b border-border bg-card/50 backdrop-blur-md sticky top-0 z-50">
        <div className="max-w-5xl mx-auto px-6 py-4">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div className="flex items-center gap-3 min-w-0">
              <div className="w-10 h-10 rounded-xl bg-gradient-to-br from-brand-500 to-brand-600 flex items-center justify-center shrink-0">
                <Bell className="w-6 h-6 text-white" />
              </div>
              <div className="min-w-0">
                <h1 className="font-['Outfit'] font-semibold truncate" style={{ fontSize: '1.25rem', color: '#3E7BFA' }}>
                  {t('statusPage.publicBrandTitle')}
                </h1>
                <p style={{ fontSize: '0.75rem', color: '#94A3B8' }}>{t('statusPage.publicBrandSubtitle')}</p>
              </div>
            </div>

            <div className="flex items-center gap-2 shrink-0">
              <label className="sr-only" htmlFor="public-status-lang">
                {t('statusPage.publicLanguageAria')}
              </label>
              <select
                id="public-status-lang"
                value={getLocale()}
                onChange={(e) => void handleLocaleChange(e.target.value as LocaleCode)}
                className="rounded-lg border border-border bg-input-background px-3 py-2 text-sm text-foreground focus:outline-none focus:ring-2 focus:ring-brand-500"
              >
                {SUPPORTED_LOCALES.map((l) => (
                  <option key={l.code} value={l.code}>
                    {l.nativeName}
                  </option>
                ))}
              </select>
              <button
                type="button"
                onClick={handleRefresh}
                disabled={isRefreshing}
                className="flex items-center gap-2 px-4 py-2 rounded-lg bg-input-background hover:bg-surface-light border border-border transition-all"
                style={{ fontSize: '0.875rem' }}
              >
                <RefreshCw className={`w-4 h-4 ${isRefreshing ? 'animate-spin' : ''}`} />
                {t('statusPage.publicRefresh')}
              </button>
            </div>
          </div>
        </div>
      </header>

      <main className="max-w-5xl mx-auto px-6 py-8 space-y-8">
        <Card className={`p-6 border-2 ${getStatusBannerStyles(overallStatus)}`}>
          <div className="flex items-center gap-4">
            {getStatusIcon(overallStatus, 'w-12 h-12')}
            <div className="flex-1">
              <h2 style={{ fontSize: '1.5rem', fontWeight: 600, marginBottom: '0.25rem' }}>
                {getStatusTitle(overallStatus)}
              </h2>
              <p style={{ fontSize: '0.875rem', color: '#94A3B8' }}>{getStatusDescription(overallStatus)}</p>
            </div>
            <div className="text-right">
              <p style={{ fontSize: '0.75rem', color: '#94A3B8', marginBottom: '0.25rem' }}>
                {t('statusPage.publicLastUpdated')}
              </p>
              <p style={{ fontSize: '0.875rem', fontWeight: 500 }}>{formatTime(lastUpdated)}</p>
            </div>
          </div>
        </Card>

        {incidents.length > 0 && (
          <div className="space-y-4">
            <h2 style={{ fontSize: '1.25rem', fontWeight: 600 }}>{t('statusPage.publicActiveIncidentsHeading')}</h2>
            {incidents.map((incident) => (
              <IncidentCard key={incident.id} incident={incident} />
            ))}
          </div>
        )}

        <div className="space-y-4">
          <h2 style={{ fontSize: '1.25rem', fontWeight: 600 }}>{t('statusPage.publicServicesHeading')}</h2>
          <div className="space-y-3">
            {components.map((component) => (
              <ComponentCard key={component.id} component={component} />
            ))}
          </div>
        </div>

        <div className="space-y-4">
          <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
            <h2 style={{ fontSize: '1.25rem', fontWeight: 600 }}>{t('statusPage.publicUptimeHeading')}</h2>
            <span
              style={{
                fontSize: '0.75rem',
                color: '#94A3B8',
                background: 'rgba(148,163,184,0.08)',
                padding: '2px 10px',
                borderRadius: '999px',
                border: '1px solid rgba(148,163,184,0.15)',
              }}
            >
              {t('statusPage.publicLast30Days')}
            </span>
          </div>
          <Card className="p-6 bg-card/80 backdrop-blur-sm">
            <UptimeGraph components={uptimeData ?? []} isLoading={isUptimeLoading} />
          </Card>
        </div>

        {page?.allowSubscriptions && (
          <Card className="p-8 text-center bg-gradient-to-br from-brand-500/5 to-purple-500/5 border-brand-500/20">
            <Activity className="w-12 h-12 mx-auto mb-4 text-brand-500" />
            <h2 style={{ fontSize: '1.5rem', fontWeight: 600, marginBottom: '0.5rem' }}>
              {t('statusPage.publicUpdatesHeading')}
            </h2>
            <p style={{ fontSize: '0.875rem', color: '#94A3B8', marginBottom: '1.5rem' }}>
              {t('statusPage.publicUpdatesSub')}
            </p>
            <div className="flex gap-3 max-w-md mx-auto">
              <input
                type="email"
                placeholder={t('statusPage.publicEmailPlaceholder')}
                value={subscribeEmail}
                onChange={(e) => setSubscribeEmail(e.target.value)}
                onKeyDown={(e) => e.key === 'Enter' && handleSubscribe()}
                className="flex-1 px-4 py-2 rounded-lg bg-input-background border border-border focus:outline-none focus:ring-2 focus:ring-brand-500"
                style={{ fontSize: '0.875rem' }}
              />
              <button
                type="button"
                onClick={handleSubscribe}
                disabled={subscribeMutation.isPending || !subscribeEmail.trim()}
                className="px-6 py-2 rounded-lg bg-brand-500 hover:bg-brand-600 disabled:opacity-50 text-white font-medium transition-colors"
              >
                {subscribeMutation.isPending ? t('statusPage.publicSubscribing') : t('statusPage.publicSubscribe')}
              </button>
            </div>
          </Card>
        )}
      </main>

      <footer className="border-t border-border mt-16">
        <div className="max-w-5xl mx-auto px-6 py-8 text-center">
          <p style={{ fontSize: '0.875rem', color: '#94A3B8' }}>
            {t('statusPage.publicPoweredBy')}{' '}
            <span className="font-['Outfit'] font-semibold" style={{ color: '#3E7BFA' }}>
              CalluApp
            </span>{' '}
            © {new Date().getFullYear()}
          </p>
          <p style={{ fontSize: '0.75rem', color: '#94A3B8', marginTop: '0.5rem' }}>
            {t('statusPage.publicFooterHint')}
          </p>
        </div>
      </footer>
    </div>
  );
}

function ComponentCard({ component }: { component: StatusPageComponentDto }) {
  const status = (component.status?.toLowerCase() || 'operational') as ComponentStatus;
  return (
    <Card className="p-4 bg-card/80 backdrop-blur-sm hover:shadow-md transition-all">
      <div className="flex items-center justify-between">
        <div className="flex-1">
          <div className="flex items-center gap-3 mb-1">
            {getStatusIcon(status, 'w-5 h-5')}
            <h3 style={{ fontSize: '0.9375rem', fontWeight: 600 }}>{component.name}</h3>
            {component.healthCheckEnabled && component.lastHealthCheckResponseMs != null && (
              <span
                className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs"
                style={{ background: 'rgba(62,123,250,0.08)', color: '#94A3B8' }}
              >
                {component.lastHealthCheckResponseMs}ms
              </span>
            )}
          </div>
          <div className="flex items-center gap-2">
            {component.description && (
              <p style={{ fontSize: '0.8125rem', color: '#94A3B8' }}>{component.description}</p>
            )}
            {component.healthCheckEnabled && component.lastHealthCheckAt && (
              <span style={{ fontSize: '0.7rem', color: '#64748B' }}>
                {t('statusPage.publicChecked', {
                  time: formatRelativeTime(new Date(component.lastHealthCheckAt)),
                })}
              </span>
            )}
          </div>
        </div>
        <Badge className={getStatusBadgeStyles(status)}>{statusLabel(status)}</Badge>
      </div>
    </Card>
  );
}

function IncidentCard({ incident }: { incident: StatusPageIncidentDto }) {
  const impactColors: Record<string, string> = {
    critical: 'border-error-500 bg-error-500/5',
    major: 'border-warning-500 bg-warning-500/5',
    minor: 'border-brand-500 bg-brand-500/5',
  };

  const borderStyle = impactColors[incident.impact?.toLowerCase() ?? ''] ?? 'border-brand-500 bg-brand-500/5';

  return (
    <Card className={`p-6 border-l-4 ${borderStyle}`}>
      <div className="flex items-start justify-between mb-4">
        <div className="flex-1">
          <div className="flex items-center gap-3 mb-2">
            <h3 style={{ fontSize: '1.125rem', fontWeight: 600 }}>{incident.title}</h3>
            <Badge className={getIncidentStatusStyles(incident.status)}>
              {incidentStatusLabel(incident.status)}
            </Badge>
          </div>
          <p style={{ fontSize: '0.8125rem', color: '#94A3B8' }}>
            {t('statusPage.publicStarted', { time: formatRelativeTime(new Date(incident.createdAt)) })}
          </p>
        </div>
      </div>

      {incident.updates && incident.updates.length > 0 && (
        <div className="space-y-4 ml-4 border-l-2 border-border pl-6">
          {incident.updates.map((update) => (
            <div key={update.id} className="relative">
              <div className="absolute -left-[1.69rem] top-1 w-3 h-3 rounded-full bg-brand-500 border-2 border-background" />
              <div className="flex items-start justify-between gap-4">
                <div className="flex-1">
                  <p style={{ fontSize: '0.875rem', marginBottom: '0.25rem' }}>{update.message}</p>
                  <p style={{ fontSize: '0.75rem', color: '#94A3B8' }}>
                    {formatRelativeTime(new Date(update.createdAt))}
                  </p>
                </div>
                <Badge variant="outline" style={{ fontSize: '0.75rem' }}>
                  {incidentStatusLabel(update.status)}
                </Badge>
              </div>
            </div>
          ))}
        </div>
      )}
    </Card>
  );
}

function statusLabel(status: string): string {
  switch (status) {
    case 'operational':
      return t('statusPage.operational');
    case 'degraded':
      return t('statusPage.degraded');
    case 'partial_outage':
      return t('statusPage.partialOutage');
    case 'major_outage':
      return t('statusPage.majorOutage');
    case 'maintenance':
      return t('statusPage.maintenance');
    default:
      return status;
  }
}

function incidentStatusLabel(status: string): string {
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

function getOverallStatus(components: StatusPageComponentDto[]): ComponentStatus {
  if (components.some((s) => s.status?.toLowerCase() === 'major_outage')) return 'major_outage';
  if (components.some((s) => s.status?.toLowerCase() === 'partial_outage')) return 'partial_outage';
  if (components.some((s) => s.status?.toLowerCase() === 'maintenance')) return 'maintenance';
  if (components.some((s) => s.status?.toLowerCase() === 'degraded')) return 'degraded';
  if (components.every((s) => s.status?.toLowerCase() === 'operational')) return 'operational';
  return 'degraded';
}

function getStatusIcon(status: ComponentStatus, className: string) {
  switch (status) {
    case 'operational':
      return <CheckCircle className={`${className} text-success-500`} />;
    case 'degraded':
      return <AlertCircle className={`${className} text-warning-500`} />;
    case 'partial_outage':
      return <AlertCircle className={`${className} text-error-400`} />;
    case 'major_outage':
      return <XCircle className={`${className} text-error-500`} />;
    case 'maintenance':
      return <Clock className={`${className} text-brand-500`} />;
    default:
      return <CheckCircle className={`${className} text-success-500`} />;
  }
}

function getStatusTitle(status: ComponentStatus): string {
  return t(`statusPage.publicBannerTitle.${status}`);
}

function getStatusDescription(status: ComponentStatus): string {
  return t(`statusPage.publicBannerDesc.${status}`);
}

function getStatusBannerStyles(status: ComponentStatus): string {
  switch (status) {
    case 'operational':
      return 'border-success-500 bg-success-500/5';
    case 'degraded':
      return 'border-warning-500 bg-warning-500/5';
    case 'partial_outage':
      return 'border-error-400 bg-error-400/5';
    case 'major_outage':
      return 'border-error-500 bg-error-500/5';
    case 'maintenance':
      return 'border-brand-500 bg-brand-500/5';
    default:
      return 'border-border bg-card/5';
  }
}

function getStatusBadgeStyles(status: ComponentStatus): string {
  switch (status) {
    case 'operational':
      return 'bg-success-500/10 text-success-500 border border-success-500/20';
    case 'degraded':
      return 'bg-warning-500/10 text-warning-500 border border-warning-500/20';
    case 'partial_outage':
      return 'bg-error-400/10 text-error-400 border border-error-400/20';
    case 'major_outage':
      return 'bg-error-500/10 text-error-500 border border-error-500/20';
    case 'maintenance':
      return 'bg-brand-500/10 text-brand-500 border border-brand-500/20';
    default:
      return 'bg-muted/10 text-muted-foreground border border-border';
  }
}

function getIncidentStatusStyles(status: string): string {
  switch (status.toLowerCase()) {
    case 'investigating':
      return 'bg-error-500/10 text-error-500 border border-error-500/20';
    case 'identified':
      return 'bg-warning-500/10 text-warning-500 border border-warning-500/20';
    case 'monitoring':
      return 'bg-brand-500/10 text-brand-500 border border-brand-500/20';
    case 'resolved':
      return 'bg-success-500/10 text-success-500 border border-success-500/20';
    default:
      return 'bg-brand-500/10 text-brand-500 border border-brand-500/20';
  }
}

function formatTime(date: Date): string {
  const loc = getLocale() === 'tr' ? 'tr-TR' : 'en-US';
  return date.toLocaleTimeString(loc, { hour: '2-digit', minute: '2-digit' });
}

function formatRelativeTime(date: Date): string {
  const seconds = Math.floor((Date.now() - date.getTime()) / 1000);

  if (seconds < 60) return t('statusPage.publicSecondsAgo', { count: String(seconds) });
  if (seconds < 3600) return t('statusPage.publicMinutesAgo', { count: String(Math.floor(seconds / 60)) });
  if (seconds < 86400) return t('statusPage.publicHoursAgo', { count: String(Math.floor(seconds / 3600)) });
  return t('statusPage.publicDaysAgo', { count: String(Math.floor(seconds / 86400)) });
}
