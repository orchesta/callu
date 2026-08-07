import { useEffect, useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { HubConnection } from '@microsoft/signalr';
import { useAuth } from '@/shared/auth/auth.context';
import { createNotificationHubConnection, disposeNotificationHubConnection } from './signalr.client';
import { notificationKeys } from '@/features/notifications/hooks/use-notifications';
import { incidentKeys } from '@/features/incidents/hooks/use-incidents';
import { serviceKeys } from '@/features/services/hooks/use-services';
import { teamKeys } from '@/features/teams/hooks/use-teams';
import { scheduleKeys } from '@/features/schedules/hooks/use-schedules';
import { settingsKeys } from '@/features/settings/hooks/use-settings';
import { dashboardKeys } from '@/features/dashboard/hooks/use-dashboard';
import type { NotificationItemDto } from '@/features/notifications/types/notification.types';

// Owns the SignalR connection for the authenticated session and invalidates the query caches each
// incoming event touches. Mount once, inside AppShell.
export function useSignalR() {
  const { isAuthenticated } = useAuth();
  const qc = useQueryClient();
  const connectionRef = useRef<HubConnection | null>(null);
  const [isConnected, setIsConnected] = useState(false);

  useEffect(() => {
    if (!isAuthenticated) {
      if (connectionRef.current) {
        disposeNotificationHubConnection(connectionRef.current);
        connectionRef.current = null;
        setIsConnected(false);
      }
      return;
    }

    // A manual self-heal restart does not fire onreconnected, so reconciliation is wired to
    // it explicitly — otherwise a tab regains its socket and still shows stale data.
    const reconcileAfterOutage = () => {
      setIsConnected(true);
      qc.invalidateQueries({ queryKey: incidentKeys.all });
      qc.invalidateQueries({ queryKey: dashboardKeys.all });
      qc.invalidateQueries({ queryKey: notificationKeys.recentAll() });
      qc.invalidateQueries({ queryKey: notificationKeys.unreadCount() });
    };

    const connection = createNotificationHubConnection(reconcileAfterOutage);
    connectionRef.current = connection;

    /** New notification pushed to this user → refresh notification list */
    connection.on('ReceiveNotification', (_notification: NotificationItemDto) => {
      qc.invalidateQueries({ queryKey: notificationKeys.recentAll() });
    });

    /** Unread count changed → update cache directly without a network request */
    connection.on('UpdateUnreadCount', (count: number) => {
      qc.setQueryData(notificationKeys.unreadCount(), { count });
    });

    /** Incident status changed (server sends IncidentUpdated; legacy name kept too) */
    const invalidateIncidents = () => {
      qc.invalidateQueries({ queryKey: incidentKeys.all });
      qc.invalidateQueries({ queryKey: dashboardKeys.all });
    };
    connection.on('IncidentUpdated', (_incidentId: string, _status: string) => {
      invalidateIncidents();
    });
    connection.on('BroadcastIncidentUpdate', (_incidentId: string, _status: string) => {
      invalidateIncidents();
    });

    connection.on('ServiceUpdated', (_serviceId: string) => {
      qc.invalidateQueries({ queryKey: serviceKeys.all });
    });

    connection.on('TeamUpdated', (_teamId: string) => {
      qc.invalidateQueries({ queryKey: teamKeys.all });
    });

    connection.on('ScheduleUpdated', (_scheduleId: string) => {
      qc.invalidateQueries({ queryKey: scheduleKeys.all });
    });

    connection.on('SettingsUpdated', (section: string) => {
      switch (section) {
        case 'organization':
          qc.invalidateQueries({ queryKey: settingsKeys.organization() });
          break;
        case 'smtp':
          qc.invalidateQueries({ queryKey: settingsKeys.smtp() });
          break;
        case 'alert-rules':
          qc.invalidateQueries({ queryKey: ['alert-rules'] });
          break;
        case 'notification-channels':
          qc.invalidateQueries({ queryKey: ['notification-channels'] });
          break;
        default:
          qc.invalidateQueries({ queryKey: settingsKeys.all });
      }
    });

    connection.onreconnecting(() => setIsConnected(false));
    connection.onreconnected(() => {
      setIsConnected(true);
      qc.invalidateQueries({ queryKey: incidentKeys.all });
      qc.invalidateQueries({ queryKey: notificationKeys.recentAll() });
    });
    connection.onclose(() => setIsConnected(false));

    connection.start()
      .then(() => setIsConnected(true))
      .catch((err) => {
        if (import.meta.env.DEV) {
          console.warn('[SignalR] Connection failed:', err);
        }
      });

    return () => {
      connection.off('ReceiveNotification');
      connection.off('UpdateUnreadCount');
      connection.off('IncidentUpdated');
      connection.off('BroadcastIncidentUpdate');
      connection.off('ServiceUpdated');
      connection.off('TeamUpdated');
      connection.off('ScheduleUpdated');
      connection.off('SettingsUpdated');

      disposeNotificationHubConnection(connection);
    };
  }, [isAuthenticated, qc]);

  return { isConnected };
}
