using System.ComponentModel.DataAnnotations;

namespace Callu.Shared.Validation;

/// <summary>
/// Validates that string length is within bounds
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public class SafeStringLengthAttribute : StringLengthAttribute
{
    public SafeStringLengthAttribute(int maximumLength) : base(maximumLength)
    {
        ErrorMessage = "The {0} field must be between {2} and {1} characters";
    }
    
    public SafeStringLengthAttribute(int minimumLength, int maximumLength) : base(maximumLength)
    {
        MinimumLength = minimumLength;
        ErrorMessage = "The {0} field must be between {2} and {1} characters";
    }
}