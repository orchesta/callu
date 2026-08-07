/** Fetches the dashboard data and hands it to DashboardPresentation. */

import { useState } from 'react';
import {
  useSchedule,
  useScheduleOccurrences,
  useSchedules,
} from '@/features/schedules/hooks/use-schedules';
import { useDashboardSummary } from '../hooks/use-dashboard';
import { DashboardPresentation } from './dashboard-presentation';

export type TimeRange = 0 | 1 | 7 | 30 | 90;

/** How far ahead the on-call card looks: the rest of today, plus tomorrow. */
const ON_CALL_DAYS = 2;

export function Dashboard() {
  const [timeRange, setTimeRange] = useState<TimeRange>(0);
  const { data: summary, isLoading } = useDashboardSummary(5, timeRange || undefined);

  const [pickedScheduleId, setPickedScheduleId] = useState<string | null>(null);
  const { data: schedules = [], isLoading: schedulesLoading, error: schedulesError } = useSchedules();

  // Falls back to the first schedule rather than to nothing: a pick can outlive the schedule it
  // named, and an empty card answers nobody.
  const selectedSchedule = schedules.find((s) => s.id === pickedScheduleId) ?? schedules[0];
  const selectedScheduleId = selectedSchedule?.id ?? '';

  // The overrides ride on the schedule detail, a separate request from the occurrences. Naming
  // anyone before both have landed puts the displaced rotation holder on screen for a moment.
  const {
    data: scheduleDetail,
    isLoading: overridesLoading,
    error: overridesError,
  } = useSchedule(selectedScheduleId);
  const {
    data: occurrences = [],
    isLoading: occurrencesLoading,
    error: occurrencesError,
  } = useScheduleOccurrences(selectedScheduleId, ON_CALL_DAYS);

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
      severityCounts={summary?.severityCounts ?? {}}
      services={services}
      isLoading={isLoading}
      timeRange={timeRange}
      onTimeRangeChange={setTimeRange}
      onCall={{
        schedules,
        selectedScheduleId,
        onSelectSchedule: setPickedScheduleId,
        occurrences,
        overrides: scheduleDetail?.overrides ?? [],
        days: ON_CALL_DAYS,
        isLoading: schedulesLoading || occurrencesLoading || overridesLoading,
        hasError: !!(schedulesError || occurrencesError || overridesError),
      }}
    />
  );
}
