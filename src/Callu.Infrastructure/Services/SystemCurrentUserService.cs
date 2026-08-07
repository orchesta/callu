using Callu.Application.Common.Interfaces;

namespace Callu.Infrastructure.Services;

/// <summary>ICurrentUserService for non-HTTP hosts; reports a synthetic "system" principal.</summary>
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
