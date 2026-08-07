import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

/**
 * A deliberately disposed connection must not resurrect itself when a previously
 * scheduled self-heal timer later fires.
 */
const h = vi.hoisted(() => {
  const makeConnection = () => ({
    oncloseCb: null as ((err?: Error) => void) | null,
    state: 'Disconnected',
    start: vi.fn(() => Promise.resolve()),
    stop: vi.fn(() => Promise.resolve()),
    onclose(cb: (err?: Error) => void) {
      this.oncloseCb = cb;
    },
  });
  const authMock = {
    isAuthenticated: vi.fn(() => true),
    isTokenExpiringSoon: vi.fn(() => false),
    refreshAccessToken: vi.fn(() => Promise.resolve()),
    getAccessToken: vi.fn(() => 'token'),
  };
  return { current: makeConnection(), makeConnection, authMock };
});

vi.mock('@microsoft/signalr', () => {
  class HubConnectionBuilder {
    withUrl() {
      return this;
    }
    withAutomaticReconnect() {
      return this;
    }
    configureLogging() {
      return this;
    }
    build() {
      h.current = h.makeConnection();
      return h.current;
    }
  }
  return {
    HubConnectionBuilder,
    HubConnectionState: { Disconnected: 'Disconnected', Connected: 'Connected' },
    LogLevel: { Information: 2, Warning: 3 },
    HttpTransportType: { WebSockets: 1, LongPolling: 4 },
  };
});

vi.mock('@/shared/auth/auth.service', () => ({ authService: h.authMock }));
vi.mock('@/shared/config', () => ({ API_URL: 'http://test' }));

import {
  createNotificationHubConnection,
  disposeNotificationHubConnection,
} from './signalr.client';

describe('SignalR self-heal', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    h.authMock.isAuthenticated.mockReturnValue(true);
  });
  afterEach(() => vi.useRealTimers());

  it('restarts a dropped connection after the backoff delay', () => {
    const conn = createNotificationHubConnection();
    h.current.oncloseCb?.(new Error('transport drop'));

    vi.advanceTimersByTime(5000);

    expect(h.current.start).toHaveBeenCalledTimes(1);
    void conn;
  });

  it('does not resurrect after dispose cancels the pending timer', async () => {
    const conn = createNotificationHubConnection();
    h.current.oncloseCb?.(new Error('transport drop'));

    await disposeNotificationHubConnection(conn);
    vi.advanceTimersByTime(60000);

    expect(h.current.start).not.toHaveBeenCalled();
    expect(h.current.stop).toHaveBeenCalledTimes(1);
  });
});
