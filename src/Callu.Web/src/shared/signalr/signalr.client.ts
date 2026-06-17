import {
  HubConnectionBuilder,
  HubConnection,
  LogLevel,
  HttpTransportType,
} from '@microsoft/signalr';
import { API_URL, AUTH_TOKEN_KEY } from '@/shared/config';

/**
 * Creates a SignalR HubConnection for the NotificationHub.
 *
 * - JWT is passed via query string (?access_token=...) which the backend
 *   already handles in AuthenticationExtensions.cs.
 * - Automatic reconnect with exponential backoff: 0s, 2s, 5s, 10s, 30s.
 */
export function createNotificationHubConnection(): HubConnection {
  return new HubConnectionBuilder()
    .withUrl(`${API_URL}/hubs/notifications`, {
      accessTokenFactory: () => localStorage.getItem(AUTH_TOKEN_KEY) ?? '',
      transport: HttpTransportType.WebSockets | HttpTransportType.LongPolling,
    })
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    .configureLogging(
      import.meta.env.DEV ? LogLevel.Information : LogLevel.Warning
    )
    .build();
}
