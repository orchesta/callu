/**
 * API Error Infrastructure
 * Structured error hierarchy aligned with backend GlobalExceptionHandler.
 *
 * Backend exception → HTTP mapping:
 *   NotFoundException       → 404
 *   ValidationException     → 400 (+ field errors)
 *   ConflictException       → 409
 *   ForbiddenException      → 403
 *   UnauthorizedException   → 401
 *   BusinessRuleException   → 422
 *   DomainException         → 400
 *   Internal                → 500
 */

/**
 * Categorizes errors for UI decision-making.
 * Components can switch on this to decide what to render.
 */
export enum ApiErrorCategory {
    /** Connection failed, DNS, CORS, offline */
    Network = 'NETWORK',
    /** Request exceeded configured timeout */
    Timeout = 'TIMEOUT',
    /** 401 Unauthorized — token expired or missing */
    Unauthorized = 'UNAUTHORIZED',
    /** 403 Forbidden — insufficient permissions */
    Forbidden = 'FORBIDDEN',
    /** 400 Bad Request — validation or domain error, may include field errors */
    Validation = 'VALIDATION',
    /** 404 Not Found */
    NotFound = 'NOT_FOUND',
    /** 409 Conflict — e.g. duplicate resource */
    Conflict = 'CONFLICT',
    /** 422 Unprocessable Entity — business rule violation */
    BusinessRule = 'BUSINESS_RULE',
    /** 500+ Server Error */
    Server = 'SERVER',
    /** Catch-all for unexpected errors */
    Unknown = 'UNKNOWN',
}

/**
 * Structured API error with category and optional field-level validation errors.
 * All API failures are normalized into this shape before reaching the UI.
 */
export class ApiError extends Error {
    public readonly category: ApiErrorCategory;
    public readonly statusCode: number;
    /** Field-level validation errors from FluentValidation: { "Email": ["Required", "Invalid format"] } */
    public readonly errors?: Record<string, string[]>;
    /** Raw error details for debugging */
    public readonly details?: unknown;

    constructor(
        statusCode: number,
        message: string,
        options?: {
            category?: ApiErrorCategory;
            errors?: Record<string, string[]>;
            details?: unknown;
        }
    ) {
        super(message);
        this.name = 'ApiError';
        this.statusCode = statusCode;
        this.errors = options?.errors;
        this.details = options?.details;
        this.category = options?.category ?? mapStatusToCategory(statusCode);
    }

    /** Check if this error has field-level validation errors */
    get hasFieldErrors(): boolean {
        return !!this.errors && Object.keys(this.errors).length > 0;
    }

    /** Get all field names that have errors */
    get errorFields(): string[] {
        return this.errors ? Object.keys(this.errors) : [];
    }
}

/** Map an HTTP status code to an error category */
function mapStatusToCategory(statusCode: number): ApiErrorCategory {
    switch (statusCode) {
        case 400:
            return ApiErrorCategory.Validation;
        case 401:
            return ApiErrorCategory.Unauthorized;
        case 403:
            return ApiErrorCategory.Forbidden;
        case 404:
            return ApiErrorCategory.NotFound;
        case 408:
            return ApiErrorCategory.Timeout;
        case 409:
            return ApiErrorCategory.Conflict;
        case 422:
            return ApiErrorCategory.BusinessRule;
        default:
            if (statusCode >= 500) return ApiErrorCategory.Server;
            return ApiErrorCategory.Unknown;
    }
}

/** Check if an unknown error is an ApiError */
export function isApiError(error: unknown): error is ApiError {
    return error instanceof ApiError;
}

/** Check if the error is a network connectivity issue */
export function isNetworkError(error: unknown): boolean {
    if (isApiError(error)) return error.category === ApiErrorCategory.Network;
    return error instanceof TypeError && error.message.includes('fetch');
}

/** Check if the error is a timeout */
export function isTimeoutError(error: unknown): boolean {
    if (isApiError(error)) return error.category === ApiErrorCategory.Timeout;
    return error instanceof DOMException && error.name === 'AbortError';
}

/** Check if the error is an authentication/authorization issue */
export function isAuthError(error: unknown): boolean {
    if (isApiError(error)) {
        return (
            error.category === ApiErrorCategory.Unauthorized ||
            error.category === ApiErrorCategory.Forbidden
        );
    }
    return false;
}

/** Check if the error has field-level validation errors */
export function isValidationError(error: unknown): error is ApiError & { errors: Record<string, string[]> } {
    return isApiError(error) && error.hasFieldErrors;
}

/**
 * Get validation errors for a specific field.
 * Returns empty array if no errors for the field.
 *
 * @example
 * const emailErrors = getFieldErrors(error, 'email');
 * // => ["Email is required", "Email format is invalid"]
 */
export function getFieldErrors(error: unknown, field: string): string[] {
    if (!isApiError(error) || !error.errors) return [];
    return (
        error.errors[field] ??
        error.errors[field.charAt(0).toUpperCase() + field.slice(1)] ??
        []
    );
}

/**
 * Get a user-friendly error message for any error type.
 * Maps error categories to actionable messages.
 */
export function getErrorMessage(error: unknown): string {
    if (isApiError(error)) {
        switch (error.category) {
            case ApiErrorCategory.Network:
                return 'Unable to connect to the server. Please check your internet connection.';
            case ApiErrorCategory.Timeout:
                return 'The request timed out. Please try again.';
            case ApiErrorCategory.Unauthorized:
                return 'Your session has expired. Please log in again.';
            case ApiErrorCategory.Forbidden:
                return 'You do not have permission to perform this action.';
            case ApiErrorCategory.NotFound:
                return error.message || 'The requested resource was not found.';
            case ApiErrorCategory.Conflict:
                return error.message || 'A conflict occurred. The resource may have been modified.';
            case ApiErrorCategory.Validation:
            case ApiErrorCategory.BusinessRule:
                return error.message || 'Please check your input and try again.';
            case ApiErrorCategory.Server:
                return 'Something went wrong on our end. Please try again later.';
            default:
                return error.message || 'An unexpected error occurred.';
        }
    }

    if (error instanceof Error) {
        return error.message;
    }

    return 'An unexpected error occurred.';
}

/**
 * Create an ApiError from a network failure (TypeError from fetch).
 */
export function createNetworkError(originalError: Error): ApiError {
    return new ApiError(0, 'Network connection failed', {
        category: ApiErrorCategory.Network,
        details: originalError,
    });
}

/**
 * Create an ApiError from a timeout (AbortError).
 */
export function createTimeoutError(timeoutMs: number): ApiError {
    return new ApiError(408, `Request timed out after ${timeoutMs}ms`, {
        category: ApiErrorCategory.Timeout,
    });
}
