/**
 * Application Configuration
 * 
 * All environment variables are read here once.
 * In development, Vite injects VITE_* from .env files.
 * In Docker (nginx proxy), env vars may not be set at build time —
 * sensible defaults are used so the SPA works on same-origin.
 */

/** Base API URL — defaults to current origin for Docker/nginx proxy setups */
export const API_URL = import.meta.env.VITE_API_URL || window.location.origin;

/** Auth token localStorage key */
export const AUTH_TOKEN_KEY = import.meta.env.VITE_AUTH_TOKEN_KEY || 'calluapp_auth_token';

/** API request timeout in ms */
export const API_TIMEOUT = parseInt(import.meta.env.VITE_API_TIMEOUT || '30000');
