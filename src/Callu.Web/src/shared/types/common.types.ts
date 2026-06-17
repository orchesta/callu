/**
 * Common types used across the application
 */

/**
 * Standard API response envelope — matches backend ApiResponse<T>.
 * See: Callu.Shared.Results.ApiResponse<T>
 */
export interface ApiResponse<T> {
  success: boolean;
  data: T | null;
  message?: string;
  /** Field-level validation errors from FluentValidation */
  errors?: Record<string, string[]>;
}

/**
 * Paginated result from list endpoints.
 * Mirrors BE: Callu.Shared.Results.PagedResult<T>
 * Backend wraps these inside ApiResponse<PagedResult<T>>.
 */
export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
}
