using Callu.Application.Validators;
using Callu.Shared.Models.Schedules;

namespace Callu.Tests;

/// <summary>
/// SCH-1: the override CREATE validator must reject Local-kind timestamps (an offset-bearing
/// ISO string deserializes to Kind=Local and the service's SpecifyKind(Utc) would silently
/// shift it by the offset — paging the wrong person), matching the UPDATE validator.
/// </summary>
public class CreateOverrideValidatorTests
{
    private static readonly CreateOverrideRequestValidator Validator = new();

    private static CreateOverrideRequest Request(DateTime start, DateTime end) => new()
    {
        ScheduleId = Guid.NewGuid(),
        OverrideUserId = "user-1",
        StartUtc = start,
        EndUtc = end,
    };

    private static DateTime At(int hour, DateTimeKind kind) =>
        DateTime.SpecifyKind(new DateTime(2026, 6, 15, hour, 0, 0), kind);

    [Fact]
    public void Accepts_Utc_Timestamps()
    {
        var result = Validator.Validate(Request(At(14, DateTimeKind.Utc), At(22, DateTimeKind.Utc)));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Accepts_Unspecified_Timestamps()
    {
        var result = Validator.Validate(Request(At(14, DateTimeKind.Unspecified), At(22, DateTimeKind.Unspecified)));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Rejects_Local_Start_Timestamp()
    {
        var result = Validator.Validate(Request(At(17, DateTimeKind.Local), At(19, DateTimeKind.Utc)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateOverrideRequest.StartUtc));
    }

    [Fact]
    public void Rejects_Local_End_Timestamp()
    {
        var result = Validator.Validate(Request(At(14, DateTimeKind.Utc), At(19, DateTimeKind.Local)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateOverrideRequest.EndUtc));
    }
}
