// Centralized HTTP layer: ApiResponse<T> envelope, AbortController timeout,
// 401 → silent refresh → retry, and ApiError categorization.

import type { ApiResponse } from '../types/common.types';
import { ApiError, ApiErrorCategory, createNetworkError, createTimeoutError } from './api-errors';
import { API_URL, API_TIMEOUT } from '@/shared/config';
import { authService } from '../auth/auth.service';
import { getLocale } from '@/shared/locales/i18n';

type QueryParamValue = string | number | boolean | undefined;

export type QueryParams = Record<string, QueryParamValue | QueryParamValue[]>;

export interface RequestConfig extends Omit<RequestInit, 'body'> {
  params?: QueryParams;
  retry?: number;
  timeout?: number;
  skipAuth?: boolean;
  body?: RequestInit['body'] | unknown;
  /** Internal: marks the post-refresh retry so a 401 cannot trigger a second refresh. */
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
    this.defaultRetry = 0;
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

  /** Multipart upload, with its own 401 → refresh → retry loop because the FormData body only exists here. */
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

    const timeout = config?.timeout ?? this.defaultTimeout;

    let response = await this.sendMultipart(endpoint, formData, config, timeout);

    if (response.status === 401 && !config?.skipAuth && !config?._retriedAfterRefresh) {
      const refreshed = await authService.refreshAccessToken();
      if (refreshed) {
        response = await this.sendMultipart(endpoint, formData, config, timeout);
      } else {
        await authService.logout();
      }
    }

    if (response.ok) {
      return this.parseSuccessResponse<T>(response);
    }

    const apiError = await this.parseErrorResponse(response);
    this.notifyErrorInterceptors(apiError);
    throw apiError;
  }

  private async sendMultipart(
    endpoint: string,
    formData: FormData,
    config: RequestConfig | undefined,
    timeout: number
  ): Promise<Response> {
    const headers: Record<string, string> = { 'Accept-Language': getLocale() };
    const token = authService.getAccessToken();
    if (token && !config?.skipAuth) {
      headers['Authorization'] = `Bearer ${token}`;
    }

    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), timeout);

    try {
      return await fetch(this.buildUrl(endpoint, config?.params), {
        method: 'POST',
        headers,
        body: formData,
        signal: controller.signal,
        credentials: 'include',
      });
    } catch (error) {
      throw this.normalizeError(error, timeout);
    } finally {
      clearTimeout(timeoutId);
    }
  }

  private async request<T>(endpoint: string, config: RequestConfig): Promise<ApiResponse<T>> {
    const retries = config.retry ?? this.defaultRetry;
    const timeout = config.timeout ?? this.defaultTimeout;
    const method = (config.method ?? 'GET').toUpperCase();
    const isIdempotent = method === 'GET' || method === 'HEAD' || method === 'OPTIONS';
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

        if (!isIdempotent) throw error;

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
    this.notifyErrorInterceptors(apiError);

    throw apiError;
  }

  private notifyErrorInterceptors(error: ApiError): void {
    for (const interceptor of this.responseInterceptors) {
      if (interceptor.onError) {
        interceptor.onError(error);
      }
    }
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

  private buildUrl(endpoint: string, params?: QueryParams): string {
    const url = new URL(`${this.baseUrl}${endpoint}`);
    if (params) {
      Object.entries(params).forEach(([key, value]) => {
        if (Array.isArray(value)) {
          // Repeated, not comma-joined: the server reads one list element per occurrence of the key.
          value.forEach(item => {
            if (item !== undefined) {
              url.searchParams.append(key, String(item));
            }
          });
        } else if (value !== undefined) {
          url.searchParams.append(key, String(value));
        }
      });
    }
    return url.toString();
  }

  private getHeaders(skipAuth?: boolean): HeadersInit {
    const headers: HeadersInit = {
      'Content-Type': 'application/json',
      // The language the user picked here, not the one the browser was installed with: without it
      // the backend answers an English screen in whatever the browser happens to prefer.
      'Accept-Language': getLocale(),
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