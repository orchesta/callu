using Callu.Application.Validators;
using Callu.Shared.Models.Schedules;
using NodaTime;

namespace Callu.Tests;

/// <summary>
/// SCH-4: a rotation whose RecurrenceEndDate precedes its HandoverStartLocal materializes zero
/// occurrences (pages nobody). The create validator must reject it.
/// </summary>
public class CreateRotationValidatorTests
{
    private static readonly CreateRotationRequestValidator Validator = new();

    private static CreateRotationRequest Request(LocalDate? endDate) => new()
    {
        UserId = "user-1",
        HandoverStartLocal = new LocalDateTime(2026, 6, 15, 9, 0),
        ShiftLengthMinutes = 480,
        Order = 0,
        RecurrenceEndDate = endDate,
    };

    [Fact]
    public void Rejects_End_Date_Before_Start()
    {
        var result = Validator.Validate(Request(new LocalDate(2026, 6, 1)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.Contains("RecurrenceEndDate"));
    }

    [Fact]
    public void Accepts_End_Date_On_Or_After_Start()
    {
        Assert.True(Validator.Validate(Request(new LocalDate(2026, 6, 15))).IsValid);
        Assert.True(Validator.Validate(Request(new LocalDate(2026, 7, 1))).IsValid);
    }

    [Fact]
    public void Accepts_No_End_Date()
    {
        Assert.True(Validator.Validate(Request(null)).IsValid);
    }
}
