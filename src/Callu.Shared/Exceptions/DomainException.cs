namespace Callu.Shared.Exceptions;

/// <summary>
/// Base exception for domain-related errors
/// </summary>
public class DomainException : Exception
{
    public string? ErrorCode { get; }
    
    public DomainException(string message, string? errorCode = null) : base(message)
    {
        ErrorCode = errorCode;
    }
    
    public DomainException(string message, Exception innerException, string? errorCode = null) 
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}