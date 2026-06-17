namespace Callu.Application.Common.Models.Persistence;

/// <summary>Tenant user directory row (no Identity entity in the application layer API).</summary>
public record TenantUserDirectoryRow(
    string Id,
    string Email,
    string? DisplayName,
    string? FirstName,
    string? LastName,
    string? PhoneNumber,
    string? Timezone,
    bool EmailConfirmed,
    DateTime CreatedAt,
    DateTime? LastLoginAt,
    bool IsDeleted,
    string PrimaryRole);

/// <summary>Minimal user fields for notifications / conferencing.</summary>
public record UserContactSnapshot(
    string Id,
    string? DisplayName,
    string? PhoneNumber,
    string? Email);
