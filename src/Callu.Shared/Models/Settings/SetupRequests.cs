namespace Callu.Shared.Models.Settings;

public record InitialSetupRequest(
    string Email,
    string Password,
    string? Name = null,
    string? DefaultTimezone = null);
