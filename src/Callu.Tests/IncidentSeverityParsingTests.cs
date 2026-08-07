using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Mapping;
using Callu.Shared.Models.Incidents;
using Mapster;
using Microsoft.Extensions.DependencyInjection;

namespace Callu.Tests;

/// <summary>Severity parses case-insensitively, matching its validator, and still rejects out-of-range values.</summary>
public class IncidentSeverityParsingTests
{
    static IncidentSeverityParsingTests() => new ServiceCollection().AddMappingConfig();

    private static Incident Adapt(string? severity) =>
        new CreateIncidentRequest { Title = "Database unreachable", Severity = severity! }.Adapt<Incident>();

    [Theory]
    [InlineData("critical", IncidentSeverity.Critical)]
    [InlineData("CRITICAL", IncidentSeverity.Critical)]
    [InlineData("Critical", IncidentSeverity.Critical)]
    [InlineData("HIGH", IncidentSeverity.High)]
    [InlineData("high", IncidentSeverity.High)]
    [InlineData("low", IncidentSeverity.Low)]
    [InlineData("Medium", IncidentSeverity.Medium)]
    public void Severity_IsParsedCaseInsensitively(string input, IncidentSeverity expected)
    {
        Assert.Equal(expected, Adapt(input).Severity);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not-a-severity")]
    public void UnparseableSeverity_FallsBackToMedium(string? input)
    {
        Assert.Equal(IncidentSeverity.Medium, Adapt(input).Severity);
    }

    [Fact]
    public void NumericSeverity_OutsideTheEnum_FallsBackToMedium()
    {
        // Enum.TryParse happily accepts any numeric string; IsDefined is what keeps a bogus
        // ordinal from being stored as a severity nobody can render.
        Assert.Equal(IncidentSeverity.Medium, Adapt("999").Severity);
    }

    [Fact]
    public void CreatePath_DefaultsToAnOpenIncident()
    {
        var incident = Adapt("critical");

        Assert.Equal(IncidentStatus.Open, incident.Status);
        Assert.NotEqual(Guid.Empty, incident.Id);
    }
}
