namespace Callu.Shared.Exceptions;

/// <summary>
/// Exception for validation errors
/// </summary>
public class ValidationException : DomainException
{
    public IDictionary<string, string[]>? Errors { get; }
    
    public ValidationException(string message) 
        : base(message, "VALIDATION_ERROR")
    {
    }
    
    public ValidationException(IDictionary<string, string[]> errors)
        : base("One or more validation errors occurred", "VALIDATION_ERROR")
    {
        Errors = errors;
    }
}