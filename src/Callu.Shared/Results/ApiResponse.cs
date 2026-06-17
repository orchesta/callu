namespace Callu.Shared.Results;

/// <summary>
/// Standard API response envelope for consistent response formatting.
/// All API endpoints return this wrapper around their data.
/// </summary>
/// <typeparam name="T">The type of the response data</typeparam>
public record ApiResponse<T>
{
    public bool Success { get; init; } = true;
    public T? Data { get; init; }
    public string? Message { get; init; }
    public IDictionary<string, string[]>? Errors { get; init; }
}

/// <summary>
/// Static factory methods for creating ApiResponse instances
/// </summary>
public static class ApiResponse
{
    public static ApiResponse<T> Ok<T>(T data, string? message = null) => new()
    {
        Success = true,
        Data = data,
        Message = message
    };

    public static ApiResponse<T> Fail<T>(string message, IDictionary<string, string[]>? errors = null) => new()
    {
        Success = false,
        Message = message,
        Errors = errors
    };

    public static ApiResponse<object> Fail(string message) => new()
    {
        Success = false,
        Message = message
    };
}
