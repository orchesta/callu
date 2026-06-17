/**
 * Shared API infrastructure — barrel export.
 *
 * Usage:
 *   import { apiClient, ApiError, apiQueryOptions, useApiQuery, queryClient } from '@/shared/api';
 */

export { apiClient, ApiClient } from './client';
export type { RequestConfig, ResponseInterceptor } from './client';

export {
    ApiError,
    ApiErrorCategory,
    isApiError,
    isNetworkError,
    isTimeoutError,
    isAuthError,
    isValidationError,
    getFieldErrors,
    getErrorMessage,
    createNetworkError,
    createTimeoutError,
} from './api-errors';

export { queryClient, createQueryClient } from './query-client';
export { useApiQuery, useApiMutation, apiQueryOptions } from './api-hooks';
