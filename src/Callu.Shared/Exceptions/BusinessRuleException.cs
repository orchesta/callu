namespace Callu.Shared.Exceptions;

/// <summary>
/// Exception for business rule violations
/// </summary>
public class BusinessRuleException : DomainException
{
    public BusinessRuleException(string message)
        : base(message, "BUSINESS_RULE_VIOLATION")
    {
    }
}