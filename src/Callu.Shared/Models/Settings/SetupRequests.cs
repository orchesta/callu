using System.ComponentModel.DataAnnotations;
using Callu.Domain.Entities;

namespace Callu.Shared.Models.Settings;

public record InitialSetupRequest(
    string Email,
    string Password,
    string? Name = null,
    string? DefaultTimezone = null,
    // Public address this install is reached at; the request host is used when omitted.
    [property: StringLength(OrganizationSettings.MaxBaseUrlLength)] string? BaseUrl = null);
