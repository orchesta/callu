namespace Callu.Shared.Results;

/// <summary>
/// Represents the result of an operation that may succeed or fail
/// </summary>
public class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public string? Error { get; }
    public string? ErrorCode { get; }

    protected Result(bool isSuccess, string? error = null, string? errorCode = null)
    {
        IsSuccess = isSuccess;
        Error = error;
        ErrorCode = errorCode;
    }

    public static Result Success() => new(true);
    public static Result Failure(string error, string? errorCode = null) => new(false, error, errorCode);
    
    public static Result<T> Success<T>(T value) => Result<T>.Success(value);
    public static Result<T> Failure<T>(string error, string? errorCode = null) => Result<T>.Failure(error, errorCode);
}

/// <summary>
/// Represents the result of an operation that returns a value
/// </summary>
/// <typeparam name="T">The type of the value</typeparam>
public class Result<T> : Result
{
    public T? Value { get; }
    
    private Result(bool isSuccess, T? value, string? error = null, string? errorCode = null) 
        : base(isSuccess, error, errorCode)
    {
        Value = value;
    }

    public static Result<T> Success(T value) => new(true, value);
    public static new Result<T> Failure(string error, string? errorCode = null) => new(false, default, error, errorCode);
    
    /// <summary>
    /// Implicitly convert a value to a successful result
    /// </summary>
    public static implicit operator Result<T>(T value) => Success(value);
}