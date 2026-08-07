using System.Security.Claims;
using Callu.Application.Common.Interfaces;

namespace Callu.Api.Services;

/// <summary>
/// Implements ICurrentUserService for the REST API using HttpContext JWT claims.
/// </summary>
public class HttpContextCurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public string? UserId => Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
    public string? UserName => Principal?.FindFirstValue(ClaimTypes.Name);
    public string? Email => Principal?.FindFirstValue(ClaimTypes.Email);
    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public IEnumerable<string> Roles => Principal?
        .FindAll(ClaimTypes.Role)
        .Select(c => c.Value) ?? Enumerable.Empty<string>();

    public bool IsInRole(string role) => Principal?.IsInRole(role) ?? false;
}
