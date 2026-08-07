// Every VITE_* environment variable is read once here, with defaults that keep
// the SPA working same-origin when nothing was injected at build time.

/** Base API URL — defaults to current origin for Docker/nginx proxy setups */
export const API_URL = import.meta.env.VITE_API_URL || window.location.origin;

/** Auth token localStorage key */
export const AUTH_TOKEN_KEY = import.meta.env.VITE_AUTH_TOKEN_KEY || 'calluapp_auth_token';

/** API request timeout in ms */
export const API_TIMEOUT = parseInt(import.meta.env.VITE_API_TIMEOUT || '30000');
