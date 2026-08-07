namespace Callu.Shared.Exceptions;

/// <summary>
/// Exception when user lacks permission for an action
/// </summary>
public class ForbiddenException : DomainException
{
    public ForbiddenException(string message = "Access denied")
        : base(message, "FORBIDDEN")
    {
    }
}