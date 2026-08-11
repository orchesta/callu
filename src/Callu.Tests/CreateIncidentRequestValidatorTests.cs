using Callu.Application.Validators;
using Callu.Domain.Entities;
using Callu.Shared.Models.Incidents;

namespace Callu.Tests;

/// <summary>The incident create bounds come from the entity, so webhook ingest's clip and the validator agree.</summary>
public class CreateIncidentRequestValidatorTests
{
    private static readonly CreateIncidentRequestValidator Validator = new();

    private static CreateIncidentRequest Request(string? title = "incident", string? description = null) => new()
    {
        Title = title ?? string.Empty,
        Description = description,
        Severity = "High",
    };

    [Fact]
    public void Description_AtTheColumnLimit_IsAccepted()
    {
        var result = Validator.Validate(Request(description: new string('a', Incident.MaxDescriptionLength)));

        Assert.DoesNotContain(result.Errors, e => e.PropertyName == nameof(CreateIncidentRequest.Description));
    }

    [Fact]
    public void Description_BetweenTheOldBoundAndTheColumn_IsAccepted()
    {
        var result = Validator.Validate(Request(description: new string('a', 3000)));

        Assert.DoesNotContain(result.Errors, e => e.PropertyName == nameof(CreateIncidentRequest.Description));
    }

    [Fact]
    public void Description_OverTheColumnLimit_IsRejected()
    {
        var result = Validator.Validate(Request(description: new string('a', Incident.MaxDescriptionLength + 1)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateIncidentRequest.Description));
    }

    [Fact]
    public void Title_AtTheColumnLimit_IsAccepted()
    {
        var result = Validator.Validate(Request(title: new string('a', Incident.MaxTitleLength)));

        Assert.DoesNotContain(result.Errors, e => e.PropertyName == nameof(CreateIncidentRequest.Title));
    }

    [Fact]
    public void Title_OverTheColumnLimit_IsRejected()
    {
        var result = Validator.Validate(Request(title: new string('a', Incident.MaxTitleLength + 1)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateIncidentRequest.Title));
    }
}
