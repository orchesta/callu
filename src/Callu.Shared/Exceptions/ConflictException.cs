namespace Callu.Shared.Exceptions;

/// <summary>
/// Exception when a conflicting state is detected
/// </summary>
public class ConflictException : DomainException
{
    public ConflictException(string message)
        : base(message, "CONFLICT")
    {
    }
}