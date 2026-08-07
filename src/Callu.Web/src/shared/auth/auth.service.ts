// The 15-minute access token lives in localStorage, so any script on the origin can read it; the
// 7-day refresh token is an HttpOnly cookie the browser replays to the auth endpoints on renewal.

import { API_URL, AUTH_TOKEN_KEY } from '@/shared/config';
import { ApiError, ApiErrorCategory } from '@/shared/api/api-errors';
import { withRefreshLock } from './refresh-lock';
import { getLocale } from '@/shared/locales/i18n';

/** Cap on a single /auth/refresh call, so a hung API cannot pin the app on the loading screen. */
const REFRESH_TIMEOUT_MS = 10_000;

/** Only the negative answer is trustworthy: offline means the request never left the machine. */
function isOffline(): boolean {
  return typeof navigator !== 'undefined' && navigator.onLine === false;
}

// The refresh could not be carried out — unreachable, 5xx or timed out — which is not a rejected
// session and must not be retried at any layer.
export class RefreshUnavailableError extends ApiError {
  constructor(cause: unknown) {
    super(0, 'Could not reach the server to renew the session', {
      category: ApiErrorCategory.Network,
      details: cause,
    });
    this.name = 'RefreshUnavailableError';
  }
}

export interface TokenPayload {
  exp: number;
  iat: number;
  sub: string;
  email?: string;
  name?: string;
  role?: string;
  [key: string]: unknown;
}

export interface AuthUser {
  id: string;
  email: string;
  name: string;
  role: string;
}

class AuthService {
  private readonly TOKEN_KEY = AUTH_TOKEN_KEY;
  private readonly API_BASE = API_URL;

  private refreshPromise: Promise<boolean> | null = null;

  // Set once a refresh ended without a usable answer but may still have rotated the single-use
  // cookie; from then on this tab does not present that cookie again.
  private cookieMaybeSpent = false;

  setAccessToken(token: string): void {
    localStorage.setItem(this.TOKEN_KEY, token);
  }

  getAccessToken(): string | null {
    return localStorage.getItem(this.TOKEN_KEY);
  }

  clearTokens(): void {
    localStorage.removeItem(this.TOKEN_KEY);
  }

  isAuthenticated(): boolean {
    const token = this.getAccessToken();
    if (!token) return false;

    try {
      const payload = this.decodeToken(token);
      return !this.isTokenExpired(payload);
    } catch {
      return false;
    }
  }

  /**
   * Check if the access token is close to expiring (within 2 minutes).
   * Used for proactive refresh before the token actually expires.
   */
  isTokenExpiringSoon(): boolean {
    const token = this.getAccessToken();
    if (!token) return false;

    try {
      const payload = this.decodeToken(token);
      const expiresIn = payload.exp - Date.now() / 1000;
      return expiresIn > 0 && expiresIn < 120;
    } catch {
      return false;
    }
  }

  /**
   * Get the current user from the JWT payload.
   * Returns null if not authenticated or token is invalid.
   */
  getCurrentUser(): AuthUser | null {
    const token = this.getAccessToken();
    if (!token) return null;

    try {
      const payload = this.decodeToken(token);
      if (this.isTokenExpired(payload)) return null;

      const id = payload.sub
        ?? payload['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier']
        ?? '';
      const email = payload.email
        ?? payload['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress']
        ?? '';
      const name = payload.name
        ?? payload['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name']
        ?? '';
      const role = payload.role
        ?? payload['http://schemas.microsoft.com/ws/2008/06/identity/claims/role']
        ?? 'Member';

      return {
        id: String(id),
        email: String(email),
        name: String(name),
        role: String(role),
      };
    } catch {
      return null;
    }
  }

  // The name and email the server holds, which an access token issued before an edit still shows
  // as they were. The role is deliberately not taken from here: authorization is decided from the
  // token, so a UI gated on anything else would offer controls the API then refuses.
  async fetchStoredIdentity(): Promise<Pick<AuthUser, 'name' | 'email'> | null> {
    const token = this.getAccessToken();
    if (!token) return null;

    try {
      const response = await fetch(`${this.API_BASE}/api/v1/auth/me`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      if (!response.ok) return null;

      const data = (await response.json())?.data;
      if (!data?.name && !data?.email) return null;

      return { name: String(data.name ?? ''), email: String(data.email ?? '') };
    } catch {
      return null;
    }
  }

  /** Signs in with raw fetch, because apiClient depends on this service for the token. */
  async login(email: string, password: string): Promise<AuthUser> {
    const response = await fetch(`${this.API_BASE}/api/v1/auth/login`, {
      method: 'POST',
      // The rejection message is shown as-is, so it has to come back in the language on screen.
      headers: { 'Content-Type': 'application/json', 'Accept-Language': getLocale() },
      credentials: 'include',
      body: JSON.stringify({ email, password }),
    });

    if (!response.ok) {
      const body = await response.json().catch(() => null);
      throw new Error(body?.message || 'Login failed');
    }

    const envelope = await response.json();
    const data = envelope.data;

    if (!data?.accessToken) {
      throw new Error('Invalid login response');
    }

    this.setAccessToken(data.accessToken);

    // A fresh sign-in issues a new cookie family, so whatever we were unsure about is moot.
    this.cookieMaybeSpent = false;

    return data.user ?? this.getCurrentUser()!;
  }

  // True when a new token was issued, false when the server rejected the refresh, and
  // RefreshUnavailableError when the answer never arrived — which must not end the session.
  async refreshAccessToken(): Promise<boolean> {
    if (this.refreshPromise) {
      return this.refreshPromise;
    }

    this.refreshPromise = this.refreshOnce();

    try {
      return await this.refreshPromise;
    } finally {
      this.refreshPromise = null;
    }
  }

  private async refreshOnce(): Promise<boolean> {
    const tokenBeforeLock = this.getAccessToken();

    return withRefreshLock(() => this.refreshUnderLock(tokenBeforeLock));
  }

  private async refreshUnderLock(tokenBeforeLock: string | null): Promise<boolean> {
    // Whoever held the lock may have already rotated the cookie and stored the new access
    // token. Adopt it rather than presenting the spent cookie again.
    if (this.getAccessToken() !== tokenBeforeLock && this.isAuthenticated()) {
      // Their refresh was accepted, which means the cookie we were unsure about had not been
      // consumed — had it been, their presentation of it would have been the reuse. Clean again.
      this.cookieMaybeSpent = false;
      return true;
    }

    if (this.cookieMaybeSpent) {
      // A token that is live again — another tab renewed it, or it never expired in the first
      // place — is all the caller needed. Hand it back instead of going near the cookie. The flag
      // stays set: a live access token is not evidence about the cookie, only a successful refresh
      // by someone else is (the branch above).
      if (this.isAuthenticated()) return true;

      throw new RefreshUnavailableError(
        new Error('Refresh abandoned: an earlier attempt may have consumed the refresh cookie')
      );
    }

    return this.doRefresh();
  }

  /**
   * Logout — clear local tokens and call server endpoint.
   */
  async logout(): Promise<void> {
    const token = this.getAccessToken();

    this.clearTokens();

    if (token) {
      try {
        await fetch(`${this.API_BASE}/api/v1/auth/logout`, {
          method: 'POST',
          credentials: 'include',
          headers: {
            'Authorization': `Bearer ${token}`,
            'Content-Type': 'application/json',
          },
        });
      } catch {
        /* empty */
      }
    }

    window.location.href = '/login';
  }

  // Only a 4xx clears the session; a transport failure, 5xx or timeout keeps the stored token and
  // raises RefreshUnavailableError. Runs at most once per ambiguous outcome.
  private async doRefresh(): Promise<boolean> {
    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), REFRESH_TIMEOUT_MS);

    let response: Response;
    try {
      response = await fetch(`${this.API_BASE}/api/v1/auth/refresh`, {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        signal: controller.signal,
      });
    } catch (error) {
      // No answer at all — the abort lands here too. An unanswered refresh is not an authentication
      // failure, but unless the browser was plainly offline the request may have been served and
      // the cookie rotated, so this tab stops presenting it.
      if (!isOffline()) this.cookieMaybeSpent = true;

      throw new RefreshUnavailableError(error);
    } finally {
      clearTimeout(timeoutId);
    }

    if (!response.ok) {
      if (response.status >= 400 && response.status < 500) {
        this.clearTokens();
        return false;
      }

      // A gateway reporting that it could not complete the call to the API (502/504) may still have
      // delivered it, so the rotation could have gone through. The API's own errors are safe to
      // treat as "nothing happened": the rotation runs inside a transaction (AuthService.
      // RefreshTokenAsync), so a failure rolls it back and the cookie stays valid.
      if (response.status === 502 || response.status === 504) this.cookieMaybeSpent = true;

      throw new RefreshUnavailableError(new Error(`Refresh failed with HTTP ${response.status}`));
    }

    let data: { accessToken?: string } | undefined;
    try {
      data = (await response.json())?.data;
    } catch (error) {
      // The server accepted the refresh, so it rotated the cookie; we simply cannot read what it
      // sent back. The copy the browser holds is spent for certain.
      this.cookieMaybeSpent = true;

      throw new RefreshUnavailableError(error);
    }

    if (!data?.accessToken) {
      this.clearTokens();
      return false;
    }

    this.setAccessToken(data.accessToken);

    return true;
  }

  private decodeToken(token: string): TokenPayload {
    const base64Url = token.split('.')[1];
    if (!base64Url) throw new Error('Invalid token format');

    const base64 = base64Url.replace(/-/g, '+').replace(/_/g, '/');
    const padded = base64.padEnd(base64.length + ((4 - (base64.length % 4)) % 4), '=');

    const bytes = Uint8Array.from(atob(padded), (c) => c.charCodeAt(0));
    const jsonPayload = new TextDecoder().decode(bytes);

    return JSON.parse(jsonPayload);
  }

  private isTokenExpired(payload: TokenPayload): boolean {
    return payload.exp < Date.now() / 1000;
  }
}

export const authService = new AuthService();

export { AuthService };
