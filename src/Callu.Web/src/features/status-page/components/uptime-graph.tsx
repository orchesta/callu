/**
 * UptimeGraph — 30-day uptime history visualization
 * Shows per-component colored day cells (like GitHub's contribution graph)
 * with tooltip on hover showing date, status, and uptime %.
 */

import { useState, useEffect, useReducer } from 'react';
import type { ComponentUptimeDto, UptimeDayDto } from '../types/status-page.types';
import { getLocale, onLocaleChange, t } from '@/shared/locales/i18n';

const STATUS_STYLES: Record<string, { color: string; bg: string; dotColor: string }> = {
  operational: { color: '#22C55E', bg: 'rgba(34,197,94,0.15)', dotColor: '#22C55E' },
  degraded: { color: '#F59E0B', bg: 'rgba(245,158,11,0.15)', dotColor: '#F59E0B' },
  partial_outage: { color: '#F97316', bg: 'rgba(249,115,22,0.15)', dotColor: '#F97316' },
  major_outage: { color: '#EF4444', bg: 'rgba(239,68,68,0.15)', dotColor: '#EF4444' },
  maintenance: { color: '#3E7BFA', bg: 'rgba(62,123,250,0.15)', dotColor: '#3E7BFA' },
  no_data: { color: '#334155', bg: 'rgba(51,65,85,0.3)', dotColor: '#475569' },
};

function getStatusStyle(status: string) {
  return STATUS_STYLES[status] ?? STATUS_STYLES.no_data;
}

function uptimeStatusLabel(status: string): string {
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
    case 'no_data':
      return t('statusPage.uptimeNoData');
    default:
      return status;
  }
}

function dateLocaleTag(): string {
  return getLocale() === 'tr' ? 'tr-TR' : 'en-GB';
}

interface DayCellProps {
  day: UptimeDayDto;
}

function DayCell({ day }: DayCellProps) {
  const [hovering, setHovering] = useState(false);
  const style = getStatusStyle(day.status);
  const label = uptimeStatusLabel(day.status);

  const dateLabel = new Date(day.date + 'T00:00:00Z').toLocaleDateString(dateLocaleTag(), {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
    timeZone: 'UTC',
  });

  return (
    <div style={{ position: 'relative' }}>
      <div
        onMouseEnter={() => setHovering(true)}
        onMouseLeave={() => setHovering(false)}
        style={{
          width: '100%',
          height: '28px',
          borderRadius: '3px',
          backgroundColor: style.color,
          opacity: day.status === 'no_data' ? 0.25 : 0.85,
          cursor: 'default',
          transition: 'opacity 0.15s, transform 0.15s',
          transform: hovering ? 'scaleY(1.2)' : 'scaleY(1)',
        }}
      />
      {hovering && (
        <div
          style={{
            position: 'absolute',
            bottom: '36px',
            left: '50%',
            transform: 'translateX(-50%)',
            backgroundColor: '#0F172A',
            border: '1px solid #1E293B',
            borderRadius: '8px',
            padding: '8px 12px',
            zIndex: 50,
            minWidth: '160px',
            pointerEvents: 'none',
            boxShadow: '0 8px 24px rgba(0,0,0,0.4)',
          }}
        >
          <p style={{ fontSize: '0.75rem', color: '#94A3B8', marginBottom: '4px' }}>{dateLabel}</p>
          <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
            <div
              style={{
                width: '8px',
                height: '8px',
                borderRadius: '50%',
                backgroundColor: style.color,
                flexShrink: 0,
              }}
            />
            <p style={{ fontSize: '0.8125rem', fontWeight: 600, color: '#F1F5F9' }}>{label}</p>
          </div>
          {day.uptimePercent !== null && (
            <p style={{ fontSize: '0.75rem', color: '#94A3B8', marginTop: '2px' }}>
              {t('statusPage.uptimePercentLine', { percent: day.uptimePercent.toFixed(1) })}
            </p>
          )}
        </div>
      )}
    </div>
  );
}

interface ComponentRowProps {
  data: ComponentUptimeDto;
}

function ComponentRow({ data }: ComponentRowProps) {
  const style = getStatusStyle(data.currentStatus);
  const pctColor =
    data.averageUptimePercent >= 99.9 ? '#22C55E' : data.averageUptimePercent >= 95 ? '#F59E0B' : '#EF4444';

  return (
    <div style={{ marginBottom: '20px' }}>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: '8px' }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
          <div
            style={{
              width: '8px',
              height: '8px',
              borderRadius: '50%',
              backgroundColor: style.color,
              flexShrink: 0,
              boxShadow: `0 0 6px ${style.color}`,
            }}
          />
          <span style={{ fontSize: '0.875rem', fontWeight: 600, color: '#F1F5F9' }}>{data.componentName}</span>
        </div>
        <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
          <span style={{ fontSize: '0.75rem', color: '#94A3B8' }}>{t('statusPage.uptime30DayAvg')}</span>
          <span style={{ fontSize: '0.875rem', fontWeight: 700, color: pctColor }}>
            {data.averageUptimePercent.toFixed(2)}%
          </span>
        </div>
      </div>

      <div
        style={{
          display: 'grid',
          gridTemplateColumns: `repeat(${data.days.length}, 1fr)`,
          gap: '2px',
          alignItems: 'center',
        }}
      >
        {data.days.map((day) => (
          <DayCell key={day.date} day={day} />
        ))}
      </div>

      <div style={{ display: 'flex', justifyContent: 'space-between', marginTop: '4px' }}>
        {data.days.length > 0 && (
          <>
            <span style={{ fontSize: '0.6875rem', color: '#475569' }}>
              {new Date(data.days[0].date + 'T00:00:00Z').toLocaleDateString(dateLocaleTag(), {
                day: 'numeric',
                month: 'short',
                timeZone: 'UTC',
              })}
            </span>
            <span style={{ fontSize: '0.6875rem', color: '#475569' }}>{t('statusPage.uptimeToday')}</span>
          </>
        )}
      </div>
    </div>
  );
}

function Legend() {
  const items = ['operational', 'degraded', 'partial_outage', 'major_outage', 'maintenance', 'no_data'];
  return (
    <div style={{ display: 'flex', flexWrap: 'wrap', gap: '12px', marginTop: '8px' }}>
      {items.map((s) => {
        const c = STATUS_STYLES[s];
        return (
          <div key={s} style={{ display: 'flex', alignItems: 'center', gap: '5px' }}>
            <div
              style={{
                width: '10px',
                height: '10px',
                borderRadius: '2px',
                backgroundColor: c.color,
                opacity: s === 'no_data' ? 0.3 : 0.85,
              }}
            />
            <span style={{ fontSize: '0.6875rem', color: '#64748B' }}>{uptimeStatusLabel(s)}</span>
          </div>
        );
      })}
    </div>
  );
}

interface UptimeGraphProps {
  components: ComponentUptimeDto[];
  isLoading?: boolean;
}

export function UptimeGraph({ components, isLoading }: UptimeGraphProps) {
  const [, bumpLocale] = useReducer((n: number) => n + 1, 0);
  useEffect(() => onLocaleChange(() => bumpLocale()), []);

  if (isLoading) {
    return (
      <div style={{ padding: '24px', textAlign: 'center' }}>
        <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
          {[1, 2, 3].map((i) => (
            <div key={i}>
              <div
                style={{
                  height: '14px',
                  width: '120px',
                  borderRadius: '4px',
                  backgroundColor: '#1E293B',
                  marginBottom: '8px',
                  animation: 'pulse 1.5s infinite',
                }}
              />
              <div
                style={{
                  height: '28px',
                  borderRadius: '3px',
                  backgroundColor: '#1E293B',
                  animation: 'pulse 1.5s infinite',
                }}
              />
            </div>
          ))}
        </div>
      </div>
    );
  }

  if (!components || components.length === 0) {
    return (
      <div style={{ padding: '32px', textAlign: 'center' }}>
        <p style={{ fontSize: '0.875rem', color: '#64748B' }}>{t('statusPage.uptimeEmpty')}</p>
      </div>
    );
  }

  return (
    <div>
      {components.map((c) => (
        <ComponentRow key={c.componentId} data={c} />
      ))}
      <Legend />
    </div>
  );
}
