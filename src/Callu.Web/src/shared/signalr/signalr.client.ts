import {
  HubConnectionBuilder,
  HubConnection,
  HubConnectionState,
  LogLevel,
  HttpTransportType,
} from '@microsoft/signalr';
import { API_URL } from '@/shared/config';
import { authService } from '@/shared/auth/auth.service';

/** Backoff for self-heal restarts after automatic reconnect has given up. */
const SELF_HEAL_DELAYS_MS = [5000, 15000, 30000, 60000];

interface SelfHealState {
  timer: ReturnType<typeof setTimeout> | null;
  disposed: boolean;
  /** Run after a manual restart succeeds: automatic reconnect fires onreconnected, this does not. */
  onRecovered?: () => void;
}

const selfHealState = new WeakMap<HubConnection, SelfHealState>();

function stateFor(connection: HubConnection): SelfHealState {
  let state = selfHealState.get(connection);
  if (!state) {
    state = { timer: null, disposed: false };
    selfHealState.set(connection, state);
  }
  return state;
}

// Hub connection whose token factory renews an expiring JWT before the handshake, so a long-idle tab
// never hands the hub a stale token; once automatic reconnect gives up, self-heal restarts it.
export function createNotificationHubConnection(onRecovered?: () => void): HubConnection {
  const connection = new HubConnectionBuilder()
    .withUrl(`${API_URL}/hubs/notifications`, {
      accessTokenFactory: async () => {
        if (!authService.isAuthenticated() || authService.isTokenExpiringSoon()) {
          await authService.refreshAccessToken();
        }
        return authService.getAccessToken() ?? '';
      },
      transport: HttpTransportType.WebSockets | HttpTransportType.LongPolling,
    })
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    .configureLogging(
      import.meta.env.DEV ? LogLevel.Information : LogLevel.Warning
    )
    .build();

  connection.onclose((error) => {
    if (!error || !authService.isAuthenticated()) return;
    stateFor(connection).onRecovered = onRecovered;
    scheduleSelfHeal(connection, 0);
  });

  return connection;
}

/**
 * Stops the connection and cancels any pending self-heal restart so a deliberately
 * torn-down connection cannot resurrect itself when a scheduled timer later fires.
 */
export function disposeNotificationHubConnection(connection: HubConnection): Promise<void> {
  const state = stateFor(connection);
  state.disposed = true;
  if (state.timer !== null) {
    clearTimeout(state.timer);
    state.timer = null;
  }
  return connection.stop();
}

/**
 * Retries forever while the session lasts, holding at the last delay: an upgrade that migrates
 * the database on API startup outlasts any fixed attempt budget, and giving up is silent.
 */
function scheduleSelfHeal(connection: HubConnection, attempt: number): void {
  const delay = SELF_HEAL_DELAYS_MS[Math.min(attempt, SELF_HEAL_DELAYS_MS.length - 1)];

  const state = stateFor(connection);
  if (state.disposed) return;

  if (state.timer !== null) clearTimeout(state.timer);
  state.timer = setTimeout(() => {
    state.timer = null;
    if (
      state.disposed ||
      connection.state !== HubConnectionState.Disconnected ||
      !authService.isAuthenticated()
    ) {
      return;
    }

    connection
      .start()
      .then(() => {
        // Whatever arrived while the socket was down was never pushed, so the caches
        // are stale in a way no later event will correct.
        state.onRecovered?.();
      })
      .catch((err) => {
        if (import.meta.env.DEV) {
          console.warn('[SignalR] Self-heal restart failed:', err);
        }
        scheduleSelfHeal(connection, attempt + 1);
      });
  }, delay);
}
