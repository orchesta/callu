using Callu.Application.Common.Interfaces;

namespace Callu.Infrastructure.Services;

/// <summary>
/// ICurrentUserService implementation for non-HTTP hosts (Worker, background jobs,
/// MassTransit consumers). Reports a synthetic "system" principal so audit writes
/// still get a stable CreatedBy/UpdatedBy value when no end-user context exists.
/// </summary>
public sealed class SystemCurrentUserService : ICurrentUserService
{
    public const string SystemUserId = "system";

    public string? UserId => SystemUserId;
    public string? UserName => SystemUserId;
    public string? Email => null;
    public bool IsAuthenticated => false;
    public IEnumerable<string> Roles => [];
    public bool IsInRole(string role) => false;
}
