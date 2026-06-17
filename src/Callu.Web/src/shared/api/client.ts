/**
 * API Client — Centralized HTTP communication layer.
 *
 * Features:
 * - Type-safe request/response with backend ApiResponse<T> envelope
 * - Automatic retry with exponential backoff (5xx & network errors only)
 * - Request timeout via AbortController
 * - JWT authentication with silent refresh (401 → refresh token → retry → logout if fails)
 * - Request/response interceptor pipeline
 * - Structured error categorization via ApiError
 * - File upload support
 */

import type { ApiResponse } from '../types/common.types';
import { ApiError, ApiErrorCategory, createNetworkError, createTimeoutError } from './api-errors';
import { API_URL, API_TIMEOUT } from '@/shared/config';
import { authService } from '../auth/auth.service';

export interface RequestConfig extends Omit<RequestInit, 'body'> {
  params?: Record<string, string | number | boolean | undefined>;
  retry?: number;
  timeout?: number;
  skipAuth?: boolean;
  body?: RequestInit['body'] | unknown;
  /**
   * Internal-use flag: set when the request is the post-refresh retry of a
   * previously 401-rejected request, so we never recurse into a second
   * refresh attempt. Do not set from caller code. Fix 01.F7.
   */
  _retriedAfterRefresh?: boolean;
}

export interface ResponseInterceptor {
  onResponse?: <T>(response: ApiResponse<T>) => ApiResponse<T> | Promise<ApiResponse<T>>;
  onError?: (error: ApiError) => void;
}

class ApiClient {
  private readonly baseUrl: string;
  private readonly defaultTimeout: number;
  private readonly defaultRetry: number;
  private responseInterceptors: ResponseInterceptor[] = [];

  constructor() {
    this.baseUrl = API_URL;
    this.defaultTimeout = API_TIMEOUT;
    this.defaultRetry = 2;
  }

  addResponseInterceptor(interceptor: ResponseInterceptor): void {
    this.responseInterceptors.push(interceptor);
  }

  async get<T>(endpoint: string, config?: RequestConfig): Promise<ApiResponse<T>> {
    return this.request<T>(endpoint, { ...config, method: 'GET' });
  }

  async post<T>(endpoint: string, data?: unknown, config?: RequestConfig): Promise<ApiResponse<T>> {
    return this.request<T>(endpoint, {
      ...config,
      method: 'POST',
      body: data !== undefined ? JSON.stringify(data) : undefined,
    });
  }

  async put<T>(endpoint: string, data?: unknown, config?: RequestConfig): Promise<ApiResponse<T>> {
    return this.request<T>(endpoint, {
      ...config,
      method: 'PUT',
      body: data !== undefined ? JSON.stringify(data) : undefined,
    });
  }

  async patch<T>(endpoint: string, data?: unknown, config?: RequestConfig): Promise<ApiResponse<T>> {
    return this.request<T>(endpoint, {
      ...config,
      method: 'PATCH',
      body: data !== undefined ? JSON.stringify(data) : undefined,
    });
  }

  async delete<T>(endpoint: string, config?: RequestConfig): Promise<ApiResponse<T>> {
    return this.request<T>(endpoint, { ...config, method: 'DELETE' });
  }

  async uploadFile<T>(
    endpoint: string,
    file: File,
    additionalData?: Record<string, string>,
    config?: RequestConfig
  ): Promise<ApiResponse<T>> {
    const formData = new FormData();
    formData.append('file', file);

    if (additionalData) {
      Object.entries(additionalData).forEach(([key, value]) => {
        formData.append(key, value);
      });
    }

    const headers: HeadersInit = {};
    const token = authService.getAccessToken();
    if (token && !config?.skipAuth) {
      headers['Authorization'] = `Bearer ${token}`;
    }

    const timeout = config?.timeout ?? this.defaultTimeout;
    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), timeout);

    try {
      const response = await fetch(this.buildUrl(endpoint, config?.params), {
        method: 'POST',
        headers,
        body: formData,
        signal: controller.signal,
        credentials: 'include',
      });

      clearTimeout(timeoutId);
      return await this.handleResponse<T>(response, endpoint, config ?? {});
    } catch (error) {
      clearTimeout(timeoutId);
      throw this.normalizeError(error, timeout);
    }
  }

  private async request<T>(endpoint: string, config: RequestConfig): Promise<ApiResponse<T>> {
    const retries = config.retry ?? this.defaultRetry;
    const timeout = config.timeout ?? this.defaultTimeout;
    let lastError: Error | null = null;

    for (let attempt = 0; attempt <= retries; attempt++) {
      try {
        const response = await this.fetchWithTimeout<T>(endpoint, config, timeout);

        let interceptedResponse = response;
        for (const interceptor of this.responseInterceptors) {
          if (interceptor.onResponse) {
            interceptedResponse = await interceptor.onResponse(interceptedResponse);
          }
        }

        return interceptedResponse;
      } catch (error) {
        lastError = error as Error;

        if (error instanceof ApiError) {
          if (error.statusCode >= 400 && error.statusCode < 500) throw error;
          if (error.category === ApiErrorCategory.Network) throw error;
        }

        if (attempt < retries) {
          const backoffMs = Math.min(1000 * Math.pow(2, attempt), 5000);
          if (import.meta.env.DEV) {
            console.warn(`[API] Retry ${attempt + 1}/${retries} in ${backoffMs}ms`, error);
          }
          await this.delay(backoffMs);
        }
      }
    }

    throw lastError ?? new Error('Request failed after retries');
  }

  private async fetchWithTimeout<T>(
    endpoint: string,
    config: RequestConfig,
    timeout: number
  ): Promise<ApiResponse<T>> {
    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), timeout);

    try {
      const url = this.buildUrl(endpoint, config.params);
      const response = await fetch(url, {
        ...config,
        headers: this.getHeaders(config.skipAuth),
        signal: controller.signal,
        body: config.body as BodyInit | undefined,
        credentials: 'include',
      });

      clearTimeout(timeoutId);
      return await this.handleResponse<T>(response, endpoint, config);
    } catch (error) {
      clearTimeout(timeoutId);
      throw this.normalizeError(error, timeout);
    }
  }

  /**
   * Handle HTTP response: parse backend envelope, handle 401, categorize errors.
   */
  private async handleResponse<T>(
    response: Response,
    endpoint: string,
    config: RequestConfig
  ): Promise<ApiResponse<T>> {
    if (response.ok) {
      return this.parseSuccessResponse<T>(response);
    }

    if (response.status === 401 && !config._retriedAfterRefresh) {
      const refreshed = await authService.refreshAccessToken();
      if (refreshed) {
        return this.request<T>(endpoint, {
          ...config,
          _retriedAfterRefresh: true,
          retry: 0,
        });
      }

      await authService.logout();
    }

    const apiError = await this.parseErrorResponse(response);

    for (const interceptor of this.responseInterceptors) {
      if (interceptor.onError) {
        interceptor.onError(apiError);
      }
    }

    throw apiError;
  }

  /**
   * Parse a successful response (2xx).
   * Returns the backend ApiResponse<T> envelope directly.
   */
  private async parseSuccessResponse<T>(response: Response): Promise<ApiResponse<T>> {
    if (response.status === 204) {
      return { success: true, data: null as T };
    }

    const body = await response.json();

    if (body && typeof body === 'object' && 'success' in body) {
      return body as ApiResponse<T>;
    }

    return {
      success: true,
      data: body as T
    };
  }

  /**
   * Parse an error response into a structured ApiError.
   * Backend returns: { success: false, message: "...", errors?: { field: ["msg"] } }
   */
  private async parseErrorResponse(response: Response): Promise<ApiError> {
    let message = `HTTP ${response.status}: ${response.statusText}`;
    let errors: Record<string, string[]> | undefined;

    try {
      const body = await response.json();
      if (body && typeof body === 'object') {
        message = body.message || message;
        errors = body.errors;
      }
    } catch {
      try {
        const text = await response.text();
        if (text) message = text;
      } catch {
        /* empty */
      }
    }

    return new ApiError(response.status, message, { errors });
  }

  private buildUrl(endpoint: string, params?: Record<string, string | number | boolean | undefined>): string {
    const url = new URL(`${this.baseUrl}${endpoint}`);
    if (params) {
      Object.entries(params).forEach(([key, value]) => {
        if (value !== undefined) {
          url.searchParams.append(key, String(value));
        }
      });
    }
    return url.toString();
  }

  private getHeaders(skipAuth?: boolean): HeadersInit {
    const headers: HeadersInit = {
      'Content-Type': 'application/json',
    };

    if (!skipAuth) {
      const token = authService.getAccessToken();
      if (token) {
        headers['Authorization'] = `Bearer ${token}`;
      }
    }

    return headers;
  }

  private delay(ms: number): Promise<void> {
    return new Promise(resolve => setTimeout(resolve, ms));
  }

  /**
   * Normalize fetch errors into ApiError instances.
   */
  private normalizeError(error: unknown, timeoutMs: number): ApiError {
    if (error instanceof ApiError) return error;

    if (error instanceof DOMException && error.name === 'AbortError') {
      return createTimeoutError(timeoutMs);
    }

    if (error instanceof TypeError) {
      return createNetworkError(error);
    }

    return new ApiError(0, error instanceof Error ? error.message : 'Unknown error');
  }
}

export const apiClient = new ApiClient();

apiClient.addResponseInterceptor({
  onError: (error) => {
    if (import.meta.env.DEV) {
      console.error(`[API Error] ${error.category} (${error.statusCode}):`, error.message);
    }
  },
});

export { ApiClient };