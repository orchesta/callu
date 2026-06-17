namespace Callu.Shared.Exceptions;

/// <summary>
/// Exception when user is not authorized for an action
/// </summary>
public class UnauthorizedException : DomainException
{
    public UnauthorizedException(string message = "Unauthorized access")
        : base(message, "UNAUTHORIZED")
    {
    }
}