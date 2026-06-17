/**
 * Auth Token Management Service
 * Handles JWT access token storage and silent renewal via HttpOnly cookie.
 *
 * Access token: 15 minutes (short-lived, stored in localStorage).
 * Refresh token: 7 days, stored in HttpOnly cookie by the server.
 *   → JS can never read it — XSS-proof.
 *
 * On 401, apiClient calls refreshAccessToken() to silently renew.
 * The browser automatically sends the HttpOnly cookie to /api/v1/auth/*.
 * If refresh fails, user is redirected to login.
 *
 * Note: login() uses raw fetch() instead of apiClient
 * to avoid circular dependency (apiClient depends on authService for token).
 */

import { API_URL, AUTH_TOKEN_KEY } from '@/shared/config';

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

  /**
   * Login with email and password.
   * Uses raw fetch to avoid circular dependency with apiClient.
   * Backend returns ApiResponse<LoginResponse> with { accessToken, refreshToken, expiresAt, user }.
   */
  async login(email: string, password: string): Promise<AuthUser> {
    const response = await fetch(`${this.API_BASE}/api/v1/auth/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
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

    return data.user ?? this.getCurrentUser()!;
  }

  /**
   * Refresh the access token using the stored refresh token.
   * Returns true if refresh succeeded, false if it failed.
   * Uses a concurrent guard to prevent multiple simultaneous refresh requests.
   */
  async refreshAccessToken(): Promise<boolean> {
    if (this.refreshPromise) {
      return this.refreshPromise;
    }

    this.refreshPromise = this.doRefresh();

    try {
      return await this.refreshPromise;
    } finally {
      this.refreshPromise = null;
    }
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

  private async doRefresh(): Promise<boolean> {
    try {
      const response = await fetch(`${this.API_BASE}/api/v1/auth/refresh`, {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
      });

      if (!response.ok) {
        this.clearTokens();
        return false;
      }

      const envelope = await response.json();
      const data = envelope.data;

      if (!data?.accessToken) {
        this.clearTokens();
        return false;
      }

      this.setAccessToken(data.accessToken);

      return true;
    } catch {
      this.clearTokens();
      return false;
    }
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
