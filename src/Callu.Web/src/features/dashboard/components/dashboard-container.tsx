/**
 * Dashboard Container (Smart Component)
 * Following SRP - Only handles data fetching and state management
 * Delegates presentation to DashboardPresentation
 */

import { useState } from 'react';
import { useDashboardSummary } from '../hooks/use-dashboard';
import { DashboardPresentation } from './dashboard-presentation';

export type TimeRange = 0 | 1 | 7 | 30 | 90;

export function Dashboard() {
  const [timeRange, setTimeRange] = useState<TimeRange>(0);
  const { data: summary, isLoading } = useDashboardSummary(5, timeRange || undefined);

  const metrics = summary
    ? {
      open: summary.triggeredCount,
      acknowledged: summary.acknowledgedCount,
      resolved: summary.resolvedCount,
      healthRate: summary.resolvedRate,
      mtta: summary.mtta,
      mttr: summary.mttr,
    }
    : undefined;

  const recentIncidents = summary?.recentIncidents ?? [];

  const services = summary?.services ?? [];

  return (
    <DashboardPresentation
      metrics={metrics}
      recentIncidents={recentIncidents}
      services={services}
      isLoading={isLoading}
      timeRange={timeRange}
      onTimeRangeChange={setTimeRange}
    />
  );
}