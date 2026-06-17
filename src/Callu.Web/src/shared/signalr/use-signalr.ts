import { useEffect, useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { HubConnection, HubConnectionState } from '@microsoft/signalr';
import { useAuth } from '@/shared/auth/auth.context';
import { createNotificationHubConnection } from './signalr.client';
import { notificationKeys } from '@/features/notifications/hooks/use-notifications';
import { incidentKeys } from '@/features/incidents/hooks/use-incidents';
import { serviceKeys } from '@/features/services/hooks/use-services';
import { teamKeys } from '@/features/teams/hooks/use-teams';
import { scheduleKeys } from '@/features/schedules/hooks/use-schedules';
import { settingsKeys } from '@/features/settings/hooks/use-settings';
import type { NotificationItemDto } from '@/features/notifications/types/notification.types';

/**
 * Manages the SignalR connection lifecycle for the authenticated session.
 *
 * - Connects when the user is authenticated.
 * - Disconnects cleanly on logout.
 * - Invalidates TanStack Query caches on incoming push events.
 *
 * Mount once inside AppShell.
 */
export function useSignalR() {
  const { isAuthenticated } = useAuth();
  const qc = useQueryClient();
  const connectionRef = useRef<HubConnection | null>(null);
  const [isConnected, setIsConnected] = useState(false);

  useEffect(() => {
    if (!isAuthenticated) {
      if (connectionRef.current) {
        connectionRef.current.stop();
        connectionRef.current = null;
        setIsConnected(false);
      }
      return;
    }

    const connection = createNotificationHubConnection();
    connectionRef.current = connection;

    /** New notification pushed to this user → refresh notification list */
    connection.on('ReceiveNotification', (_notification: NotificationItemDto) => {
      qc.invalidateQueries({ queryKey: notificationKeys.recent() });
    });

    /** Unread count changed → update cache directly without a network request */
    connection.on('UpdateUnreadCount', (count: number) => {
      qc.setQueryData(notificationKeys.unreadCount(), { count });
    });

    /** Incident status changed (server sends IncidentUpdated; legacy name kept too) */
    const invalidateIncidents = () => {
      qc.invalidateQueries({ queryKey: incidentKeys.all });
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
      qc.invalidateQueries({ queryKey: notificationKeys.recent() });
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

      if (connection.state !== HubConnectionState.Disconnected) {
        connection.stop();
      }
    };
  }, [isAuthenticated, qc]);

  return { isConnected };
}
