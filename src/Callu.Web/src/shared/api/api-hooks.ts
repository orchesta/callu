// React Query wrappers that unwrap the ApiResponse envelope, normalize failures
// into ApiError, and toast mutation outcomes.

import {
    useQuery,
    useMutation,
    queryOptions,
    type UseQueryOptions,
    type UseMutationOptions,
    type QueryKey,
} from '@tanstack/react-query';
import type { ApiResponse } from '../types/common.types';
import { ApiError, getErrorMessage } from './api-errors';
import { toast } from '../utils/toast';
import { t } from '../locales/i18n';

async function unwrapApiResponse<T>(queryFn: () => Promise<ApiResponse<T>>): Promise<T> {
    const response = await queryFn();
    if (!response.success) {
        throw new ApiError(0, response.message ?? 'Request failed');
    }
    return response.data as T;
}

/**
 * Builds options for `useQuery` / `queryClient.prefetchQuery` / `useQueries` with
 * automatic ApiResponse unwrapping (same contract as useApiQuery).
 */
export function apiQueryOptions<T>(
    queryKey: QueryKey,
    queryFn: () => Promise<ApiResponse<T>>,
    options?: Omit<UseQueryOptions<T, ApiError>, 'queryKey' | 'queryFn'>,
) {
    return queryOptions<T, ApiError>({
        queryKey,
        queryFn: () => unwrapApiResponse(queryFn),
        ...options,
    });
}

/** useQuery for an ApiResponse<T> endpoint, handing the component `T` directly. */
export function useApiQuery<T>(
    queryKey: QueryKey,
    queryFn: () => Promise<ApiResponse<T>>,
    options?: Omit<UseQueryOptions<T, ApiError>, 'queryKey' | 'queryFn'>,
) {
    return useQuery<T, ApiError>(apiQueryOptions(queryKey, queryFn, options));
}

interface UseApiMutationOptions<TData, TVariables> extends
    Omit<UseMutationOptions<TData, ApiError, TVariables>, 'mutationFn'> {
    /** Success toast message. Set to false to disable. */
    successMessage?: string | false;
    /** Error toast message override. Set to false to disable. */
    errorMessage?: string | false;
}

/** useMutation for an ApiResponse<T> endpoint, toasting success and failure. */
export function useApiMutation<TData, TVariables = void>(
    mutationFn: (variables: TVariables) => Promise<ApiResponse<TData>>,
    options?: UseApiMutationOptions<TData, TVariables>
) {
    const { successMessage, errorMessage, ...restOptions } = options ?? {};

    return useMutation<TData, ApiError, TVariables>({
        mutationFn: async (variables) => {
            const response = await mutationFn(variables);
            if (!response.success) {
                throw new ApiError(0, response.message ?? 'Request failed');
            }
            return response.data as TData;
        },
        ...restOptions,
        onSuccess: (...args) => {
            if (successMessage !== false) {
                toast.success(successMessage ?? t('toast.operationSuccess'));
            }
            restOptions.onSuccess?.(...args);
        },
        onError: (...args) => {
            if (errorMessage !== false) {
                const message = errorMessage ?? getErrorMessage(args[0]);
                toast.error(t('common.error'), message);
            }
            restOptions.onError?.(...args);
        },
    });
}
