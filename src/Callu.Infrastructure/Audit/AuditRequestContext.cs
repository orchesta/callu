using Callu.Domain.Enums;
using Callu.Shared.Models.Audit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace Callu.Infrastructure.Audit;

/// <summary>What the audit writer reads off the request it is serving.</summary>
public static class AuditRequestContext
{
    private const string Engine = "aspnetcore-authorization";
    private const string SystemActorPrefix = "system:";
    private const string ServiceActorPrefix = "service:";
    private const string BareSystemActor = "system";

    /// <summary>The endpoint's route template, or null when no route endpoint served the request.</summary>
    // The template, never the resolved path: a path carries the identifiers of whatever was touched,
    // and the 1.0 schema describes route as a template for exactly that reason.
    public static string? RouteTemplate(HttpContext? http) =>
        http?.GetEndpoint() is RouteEndpoint endpoint ? Normalize(endpoint.RoutePattern) : null;

    public static string Normalize(RoutePattern pattern) =>
        "/" + string.Join('/', pattern.PathSegments.Select(segment => string.Concat(segment.Parts.Select(Part))));

    // Literals are lower-cased: `[controller]` expands to the class name ("Incidents"), routing matches
    // either case, and a stored event keeps its route for good, so one spelling has to be chosen once.
    private static string Part(RoutePatternPart part) => part switch
    {
        RoutePatternLiteralPart literal => literal.Content.ToLowerInvariant(),
        RoutePatternParameterPart parameter => "{" + parameter.Name + "}",
        RoutePatternSeparatorPart separator => separator.Content,
        _ => string.Empty,
    };

    /// <summary>The decision that let this request through, as the endpoint declares it.</summary>
    // Reaching the writer inside a request means authorization already allowed it: callu does not
    // audit denials, so this never says deny. The policy is the action's, the most specific one.
    public static AuditAuthorization Authorization(HttpContext? http)
    {
        var endpoint = http?.GetEndpoint();
        if (http?.User.Identity?.IsAuthenticated != true || endpoint is null
            || endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            return AuditAuthorization.NotApplicable;

        var authorizeData = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
        if (authorizeData.Count == 0) return AuditAuthorization.NotApplicable;

        var policies = authorizeData
            .Select(a => a.Policy)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return policies.Count == 0
            ? new AuditAuthorization("allow", Engine)
            : new AuditAuthorization("allow", Engine, policies[^1], policies);
    }

    /// <summary>Who did it: nobody named, the bare "system" id, or a "system:*" sentinel is the system;
    /// a "service:*" id is a service of callu's own, such as the inbound alert webhook; anything else is a user.</summary>
    public static AuditActorType ActorType(string? userId) => userId switch
    {
        null or BareSystemActor => AuditActorType.System,
        _ when userId.StartsWith(SystemActorPrefix, StringComparison.Ordinal) => AuditActorType.System,
        _ when userId.StartsWith(ServiceActorPrefix, StringComparison.Ordinal) => AuditActorType.Service,
        _ => AuditActorType.User,
    };
}
