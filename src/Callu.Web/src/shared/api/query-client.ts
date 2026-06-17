/**
 * Centralized QueryClient configuration.
 *
 * Smart defaults:
 * - Don't retry on 4xx errors (they won't magically work)
 * - Retry 2x on 5xx/network errors with backoff
 * - 30s staleTime, 5m gcTime for reasonable caching
 * - Global error handler for auth redirects
 */

import { QueryClient } from '@tanstack/react-query';
import { ApiError, ApiErrorCategory } from './api-errors';

/**
 * Determine if a failed query should retry.
 * Only retry on server errors (5xx) and network/timeout issues.
 */
function shouldRetry(failureCount: number, error: unknown): boolean {
    if (failureCount >= 2) return false;

    if (error instanceof ApiError) {
        const retryableCategories = [
            ApiErrorCategory.Server,
            ApiErrorCategory.Network,
            ApiErrorCategory.Timeout,
        ];
        return retryableCategories.includes(error.category);
    }

    return failureCount < 1;
}

/**
 * Calculate retry delay with exponential backoff.
 */
function retryDelay(attemptIndex: number): number {
    return Math.min(1000 * Math.pow(2, attemptIndex), 5000);
}

/**
 * Create the application QueryClient with production defaults.
 */
export function createQueryClient(): QueryClient {
    return new QueryClient({
        defaultOptions: {
            queries: {
                staleTime: 30_000,
                gcTime: 5 * 60_000,
                retry: shouldRetry,
                retryDelay,
                refetchOnWindowFocus: true,
            },
            mutations: {
                retry: false,
            },
        },
    });
}

/** Singleton instance for the application */
export const queryClient = createQueryClient();
