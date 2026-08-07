// The single retry authority for queries — apiClient does not retry on its own,
// so a failing GET is attempted at most three times in total.

import { QueryClient } from '@tanstack/react-query';
import { ApiError, ApiErrorCategory } from './api-errors';
import { RefreshUnavailableError } from '../auth/auth.service';

/**
 * Determine if a failed query should retry.
 * Only retry on server errors (5xx) and network/timeout issues.
 */
function shouldRetry(failureCount: number, error: unknown): boolean {
    if (failureCount >= 2) return false;

    // The refresh cookie is single-use: re-running the query presents a possibly rotated cookie a
    // second time, which the server reads as reuse and answers by revoking the whole token family.
    if (error instanceof RefreshUnavailableError) return false;

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
