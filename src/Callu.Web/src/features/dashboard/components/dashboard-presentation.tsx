/** Renders the dashboard from props; fetching lives in DashboardContainer. */

import { memo, useMemo } from 'react';
import { t } from '@/shared/locales/i18n';
import { Link } from 'react-router';
import { Button } from '@/shared/components/ui/button';
import { Card } from '@/shared/components/ui/card';
import { Badge } from '@/shared/components/ui/badge';
import { Avatar, AvatarFallback } from '@/shared/components/ui/avatar';
import { DashboardSkeleton } from '@/shared/components/skeletons';
import { NoIncidentsEmptyState } from '@/shared/components/empty-states';
import { LoadingState } from '@/shared/components/loading-state';
import {
  Clock,
  CheckCircle,
  BarChart3,
  Users,
  Server,
  TrendingUp,
  TrendingDown,
  Flame,
  Activity,
  AlertCircle,
  CalendarClock,
} from 'lucide-react';
import type { IncidentListItem, IncidentMetrics } from '@/features/incidents/types/incident.types';
import { OnCallStrip } from '@/features/schedules/components/on-call-strip';
import { onCallAt, toBands } from '@/features/schedules/utils/on-call-window';
import { initialsFromName } from '@/features/users/utils/user-display';
import { useNow } from '@/shared/hooks/use-now';
import type {
  OnCallOverrideDto,
  ScheduleDto,
  ScheduleRotationDto,
} from '@/features/schedules/types/schedule.types';
import type { ServiceHealthItem } from '../types/dashboard.types';
import { getSeverityConfig, getStatusBadge } from '@/shared/utils/incident-styles';
import { getTimeAgo } from '@/shared/utils/time';
import type { TimeRange } from './dashboard-container';
import { dateLocale as appDateLocale } from "@/shared/utils/datetime";

/** Everything the on-call card draws, for one schedule at a time. */
interface OnCallNow {
  schedules: ScheduleDto[];
  selectedScheduleId: string;
  onSelectSchedule: (scheduleId: string) => void;
  occurrences: ScheduleRotationDto[];
  overrides: OnCallOverrideDto[];
  days: number;
  isLoading: boolean;
  hasError: boolean;
}

interface DashboardPresentationProps {
  metrics?: IncidentMetrics;
  recentIncidents: IncidentListItem[];
  severityCounts: Record<string, number>;
  services: ServiceHealthItem[];
  isLoading: boolean;
  timeRange: TimeRange;
  onTimeRangeChange: (range: TimeRange) => void;
  onCall: OnCallNow;
}

export function DashboardPresentation({
  metrics,
  recentIncidents,
  severityCounts,
  services,
  isLoading,
  timeRange,
  onTimeRangeChange,
  onCall,
}: DashboardPresentationProps) {
  if (isLoading) {
    return (
      <div className="p-6 space-y-6">
        <DashboardSkeleton />
      </div>
    );
  }

  return (
    <div className="p-6 space-y-6">
      <DashboardHeader timeRange={timeRange} onTimeRangeChange={onTimeRangeChange} />

      {metrics && <MetricsGrid metrics={metrics} />}

      <OnCallNowCard {...onCall} />

      <div className="grid grid-cols-1 xl:grid-cols-3 gap-6">
        <div className="xl:col-span-2">
          <RecentActivityCard incidents={recentIncidents} />
        </div>

        <div className="space-y-6">
          <SeverityImpactCard severityCounts={severityCounts} />
          <SystemHealthCard services={services} />
        </div>
      </div>
    </div>
  );
}

const TIME_RANGE_OPTIONS: { value: TimeRange; label: string }[] = [
  { value: 0, label: t("dashboard.allTime") },
  { value: 1, label: t("dashboard.last24h") },
  { value: 7, label: t("dashboard.last7d") },
  { value: 30, label: t("dashboard.last30d") },
  { value: 90, label: t("dashboard.last90d") },
];

interface DashboardHeaderProps {
  timeRange: TimeRange;
  onTimeRangeChange: (range: TimeRange) => void;
}

function DashboardHeader({ timeRange, onTimeRangeChange }: DashboardHeaderProps) {
  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
        <div>
          <h1 className="text-3xl font-semibold">{t("dashboard.title")}</h1>
          <p className="text-sm text-muted-foreground mt-1">
            {t("dashboard.subtitle")}
          </p>
        </div>
        <div className="flex gap-2">
          <Link to="/teams">
            <Button variant="outline" className="bg-input-background">
              <Users className="w-4 h-4 mr-2" />
              {t("common.teams")}
            </Button>
          </Link>
          <Link to="/services">
            <Button variant="outline" className="bg-input-background">
              <Server className="w-4 h-4 mr-2" />
              {t("common.services")}
            </Button>
          </Link>
        </div>
      </div>
      <div className="flex flex-wrap gap-2">
        {TIME_RANGE_OPTIONS.map((option) => (
          <Button
            key={option.value}
            size="sm"
            variant={timeRange === option.value ? "default" : "outline"}
            onClick={() => onTimeRangeChange(option.value)}
            className={timeRange === option.value
              ? "bg-brand-500 hover:bg-brand-600 text-white"
              : "bg-input-background"
            }
          >
            {option.label}
          </Button>
        ))}
      </div>
    </div>
  );
}

interface MetricsGridProps {
  metrics: IncidentMetrics;
}

function MetricsGrid({ metrics }: MetricsGridProps) {
  const metricCards = [
    {
      title: t("dashboard.metricOpen"),
      value: metrics.open,
      icon: Flame,
      iconColor: '#FF4D4D',
      bgColor: 'bg-error-500/10',
      borderColor: 'border-error-500/20',
    },
    {
      title: t("dashboard.metricAcknowledged"),
      value: metrics.acknowledged,
      icon: Clock,
      iconColor: '#FB923C',
      bgColor: 'bg-warning-500/10',
      borderColor: 'border-warning-500/20',
    },
    {
      title: t("dashboard.metricResolved"),
      value: metrics.resolved,
      icon: CheckCircle,
      iconColor: '#22C55E',
      bgColor: 'bg-success-500/10',
      borderColor: 'border-success-500/20',
    },
    {
      title: t("dashboard.metricHealthRate"),
      value: `${metrics.healthRate}%`,
      icon: BarChart3,
      iconColor: '#3E7BFA',
      bgColor: 'bg-brand-500/10',
      borderColor: 'border-brand-500/20',
      trend: metrics.healthRate >= 95 ? 'up' : 'down',
    },
    {
      title: t("dashboard.metricMtta"),
      value: metrics.mtta,
      icon: Clock,
      iconColor: '#A855F7',
      bgColor: 'bg-purple-500/10',
      borderColor: 'border-purple-500/20',
      tooltip: t("dashboard.tooltipMtta"),
    },
    {
      title: t("dashboard.metricMttr"),
      value: metrics.mttr,
      icon: Activity,
      iconColor: '#22C55E',
      bgColor: 'bg-success-500/10',
      borderColor: 'border-success-500/20',
      tooltip: t("dashboard.tooltipMttr"),
    },
  ];

  return (
    <div className="grid grid-cols-2 md:grid-cols-3 xl:grid-cols-6 gap-4">
      {metricCards.map((metric) => (
        <Card
          key={metric.title}
          className={`p-4 ${metric.bgColor} border ${metric.borderColor} backdrop-blur-sm hover:shadow-lg transition-all`}
        >
          <div className="flex items-start justify-between mb-2">
            <metric.icon className="w-5 h-5" style={{ color: metric.iconColor }} />
            {metric.trend && (
              <div className="flex items-center gap-1">
                {metric.trend === 'up' ? (
                  <TrendingUp className="w-4 h-4 text-success-500" />
                ) : (
                  <TrendingDown className="w-4 h-4 text-error-500" />
                )}
              </div>
            )}
          </div>
          <p className="text-2xl font-bold" style={{ color: metric.iconColor }}>
            {metric.value}
          </p>
          <p className="text-xs text-muted-foreground font-semibold tracking-wider mt-1">
            {metric.title}
          </p>
        </Card>
      ))}
    </div>
  );
}

const ON_CALL_TICK_MS = 60_000;

function OnCallNowCard({
  schedules,
  selectedScheduleId,
  onSelectSchedule,
  occurrences,
  overrides,
  days,
  isLoading,
  hasError,
}: OnCallNow) {
  const now = useNow(ON_CALL_TICK_MS);
  const schedule = schedules.find((s) => s.id === selectedScheduleId);
  const dateLocale = appDateLocale();

  // Both the headline and the strip read the same bands, so the name here is always the one drawn
  // under the now marker.
  const bands = useMemo(() => toBands(occurrences, overrides), [occurrences, overrides]);
  const current = onCallAt(bands, now);

  return (
    <Card className="p-6 bg-card/80 backdrop-blur-sm">
      <div className="flex items-center justify-between gap-4 mb-4">
        <h2 className="text-lg font-semibold">{t("dashboard.onCallNow")}</h2>
        <Link to="/schedules" className="text-brand-500 hover:text-brand-600 transition-colors">
          <span className="text-sm font-medium">{t("dashboard.manage")}</span>
        </Link>
      </div>

      {isLoading ? (
        <LoadingState size="sm" message={t("dashboard.onCallLoading")} />
      ) : hasError ? (
        // A failed read leaves no bands behind, and "nobody is on call" is the wrong thing to say
        // about a screen that simply could not fetch the rota.
        <div className="flex items-start gap-3 rounded-lg border border-warning-500/30 bg-warning-500/10 p-4">
          <AlertCircle className="mt-0.5 h-5 w-5 shrink-0 text-warning-500" />
          <div className="min-w-0">
            <p className="font-semibold text-warning-500">{t("dashboard.onCallLoadFailed")}</p>
            <p className="mt-0.5 text-xs text-muted-foreground">
              {t("dashboard.onCallLoadFailedHint")}
            </p>
          </div>
        </div>
      ) : !schedule ? (
        <div className="rounded-lg border border-border bg-surface-light/10 p-6 text-center">
          <CalendarClock className="mx-auto mb-3 h-8 w-8 text-muted-foreground opacity-60" />
          <p className="font-semibold">{t("dashboard.onCallNoSchedules")}</p>
          <p className="mt-1 text-sm text-muted-foreground">
            {t("dashboard.onCallNoSchedulesHint")}
          </p>
          <Link to="/schedules/new">
            <Button className="mt-4 bg-brand-500 hover:bg-brand-600 text-white">
              {t("schedules.createSchedule")}
            </Button>
          </Link>
        </div>
      ) : (
        <div className="space-y-4">
          {schedules.length > 1 && (
            <div
              role="group"
              aria-label={t("dashboard.onCallPickSchedule")}
              className="flex flex-wrap gap-2"
            >
              {schedules.map((option) => (
                <Button
                  key={option.id}
                  size="sm"
                  variant={option.id === schedule.id ? "default" : "outline"}
                  aria-pressed={option.id === schedule.id}
                  onClick={() => onSelectSchedule(option.id)}
                  className={option.id === schedule.id
                    ? "bg-brand-500 hover:bg-brand-600 text-white"
                    : "bg-input-background"
                  }
                >
                  {option.name}
                </Button>
              ))}
            </div>
          )}

          {current.length > 0 ? (
            <div className="flex flex-wrap items-center gap-3 rounded-lg border border-success-500/20 bg-success-500/5 p-4">
              <span
                className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full text-sm font-bold text-black/80"
                style={{ background: current[0].colour }}
              >
                {initialsFromName(current[0].label)}
              </span>
              <div className="min-w-0 flex-1">
                <p className="truncate font-semibold">
                  {current.map((band) => band.label).join(", ")}
                </p>
                <p className="truncate text-xs text-muted-foreground">
                  {schedule.name} · {schedule.timezone}
                </p>
              </div>
              {current.some((band) => band.isOverride) && (
                <Badge className="border border-border bg-muted/20 text-muted-foreground text-xs">
                  {t("onCallStrip.override")}
                </Badge>
              )}
            </div>
          ) : (
            <div className="flex items-start gap-3 rounded-lg border border-error-500/30 bg-error-500/10 p-4">
              <AlertCircle className="mt-0.5 h-5 w-5 shrink-0 text-error-500" />
              <div className="min-w-0">
                <p className="font-semibold text-error-400">{t("dashboard.onCallNobody")}</p>
                <p className="mt-0.5 text-xs text-muted-foreground">
                  {t("dashboard.onCallNobodyHint")}
                </p>
              </div>
            </div>
          )}

          {/* This card answers "who is on call now and next", and the rota it is handed only holds
              shifts that have not ended, so the window opens at now rather than at midnight. */}
          <OnCallStrip
            occurrences={occurrences}
            overrides={overrides}
            days={days}
            timeZone={schedule.timezone}
            dateLocale={dateLocale}
            windowStart={now}
          />
        </div>
      )}
    </Card>
  );
}

interface RecentActivityCardProps {
  incidents: IncidentListItem[];
}

function RecentActivityCard({ incidents }: RecentActivityCardProps) {
  return (
    <Card className="p-6 bg-card/80 backdrop-blur-sm">
      <div className="flex items-center justify-between mb-4">
        <h2 className="text-lg font-semibold">{t("dashboard.recentActivity")}</h2>
        <Link to="/incidents" className="text-brand-500 hover:text-brand-600 transition-colors">
          <span className="text-sm font-medium">{t("dashboard.viewDetailedLog")}</span>
        </Link>
      </div>

      {incidents.length > 0 ? (
        <div className="flex flex-col gap-4">
          {incidents.map((incident) => (
            <IncidentRow key={incident.id} incident={incident} />
          ))}
        </div>
      ) : (
        <NoIncidentsEmptyState />
      )}
    </Card>
  );
}

interface IncidentRowProps {
  incident: IncidentListItem;
}

const IncidentRow = memo(({ incident }: IncidentRowProps) => {
  const severityConfig = useMemo(() => getSeverityConfig(incident.severity), [incident.severity]);
  const statusBadge = useMemo(() => getStatusBadge(incident.status), [incident.status]);

  return (
    <Link to={`/incidents/${incident.id}`} className="block">
      <div
        className={`p-4 rounded-lg border ${severityConfig.bg} border-l-4 hover:shadow-md transition-transform active:scale-[0.98] cursor-pointer`}
        style={{ borderLeftColor: severityConfig.hex }}
      >
        <div className="flex items-start justify-between gap-4">
          <div className="flex-1 min-w-0">
            <div className="flex items-center gap-2 mb-1">
              <h3 className="text-[0.9375rem] font-semibold truncate">
                {incident.title}
              </h3>
              <Badge className={`${statusBadge} text-xs`}>
                {incident.status}
              </Badge>
            </div>
            <div className="flex items-center gap-3 text-sm text-muted-foreground">
              <span className="flex items-center gap-1">
                <Server className="w-3.5 h-3.5" />
                {incident.serviceName}
              </span>
              <span>•</span>
              <span>{getTimeAgo(incident.startedAt)}</span>
            </div>
          </div>
          {incident.acknowledgedBy && (
            <Avatar className="w-8 h-8">
              <AvatarFallback className="bg-brand-500/20 text-brand-500 text-xs">
                {incident.acknowledgedBy.charAt(0).toUpperCase()}
              </AvatarFallback>
            </Avatar>
          )}
        </div>
      </div>
    </Link>
  );
});

IncidentRow.displayName = 'IncidentRow';

interface SeverityImpactCardProps {
  severityCounts: Record<string, number>;
}

// Counted over the whole selected range, not over the handful of rows the recent-activity
// list happens to show — otherwise two live criticals can read as zero here.
function SeverityImpactCard({ severityCounts: counts }: SeverityImpactCardProps) {
  const severityCounts = useMemo(() => ({
    critical: counts.Critical ?? 0,
    high: counts.High ?? 0,
    medium: counts.Medium ?? 0,
    low: counts.Low ?? 0,
  }), [counts]);

  return (
    <Card className="p-6 bg-card/80 backdrop-blur-sm">
      <h2 className="text-lg font-semibold mb-4">
        {t("dashboard.severityImpact")}
      </h2>
      <div className="space-y-3">
        <SeverityRow label="Critical" count={severityCounts.critical} color="#FF4D4D" />
        <SeverityRow label="High" count={severityCounts.high} color="#FB923C" />
        <SeverityRow label="Medium" count={severityCounts.medium} color="#3E7BFA" />
        <SeverityRow label="Low" count={severityCounts.low} color="#94A3B8" />
      </div>
    </Card>
  );
}

interface SeverityRowProps {
  label: string;
  count: number;
  color: string;
}

function SeverityRow({ label, count, color }: SeverityRowProps) {
  return (
    <div className="flex items-center justify-between p-3 rounded-lg" style={{ backgroundColor: `${color}10` }}>
      <div className="flex items-center gap-2">
        <div className="w-2 h-2 rounded-full" style={{ backgroundColor: color }} />
        <span className="text-sm font-medium text-foreground">{label}</span>
      </div>
      <span className="text-sm font-semibold text-foreground">{count}</span>
    </div>
  );
}

interface SystemHealthCardProps {
  services: ServiceHealthItem[];
}

function SystemHealthCard({ services }: SystemHealthCardProps) {
  const getStatusStyle = (status: string) => {
    switch (status.toLowerCase()) {
      case 'operational':
        return 'bg-success-500/10 text-success-500 border-success-500/20';
      case 'degraded':
        return 'bg-warning-500/10 text-warning-500 border-warning-500/20';
      case 'outage':
      case 'down':
        return 'bg-error-500/10 text-error-500 border-error-500/20';
      default:
        return 'bg-muted/10 text-muted-foreground border-muted/20';
    }
  };

  return (
    <Card className="p-6 bg-card/80 backdrop-blur-sm">
      <div className="flex items-center justify-between mb-4">
        <h2 className="text-lg font-semibold">{t("dashboard.systemHealth")}</h2>
        <Link to="/services" className="text-brand-500 hover:text-brand-600 transition-colors">
          <span className="text-sm font-medium">{t("dashboard.manage")}</span>
        </Link>
      </div>
      <div className="space-y-3">
        {services.length > 0 ? (
          services.slice(0, 5).map((service) => (
            <div key={service.id} className="flex items-center justify-between">
              <div className="flex items-center gap-2 flex-1 min-w-0">
                <Avatar className="w-8 h-8">
                  <AvatarFallback className="bg-brand-500/20 text-brand-500 text-xs">
                    {service.name.charAt(0)}
                  </AvatarFallback>
                </Avatar>
                <span className="text-sm font-medium truncate">
                  {service.name}
                </span>
              </div>
              <Badge className={`text-xs border ${getStatusStyle(service.status)}`}>
                <div className="w-1.5 h-1.5 rounded-full bg-current mr-1.5" />
                {service.status}
              </Badge>
            </div>
          ))
        ) : (
          <p className="text-sm text-muted-foreground">{t("dashboard.noServicesConfigured")}</p>
        )}
      </div>
    </Card>
  );
}

