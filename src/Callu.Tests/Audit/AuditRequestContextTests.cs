using System.Security.Claims;
using Callu.Domain.Enums;
using Callu.Infrastructure.Audit;
using Callu.Shared.Models.Audit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace Callu.Tests.Audit;

/// <summary>What the audit writer reads off the request it is serving.</summary>
public class AuditRequestContextTests
{
    [Theory]
    [InlineData("api/v{version:apiVersion}/incidents/{id:guid}/resolve", "/api/v{version}/incidents/{id}/resolve")]
    [InlineData("api/v{version:apiVersion}/audit-logs", "/api/v{version}/audit-logs")]
    [InlineData("files/{**path}", "/files/{path}")]
    [InlineData("items/{id?}", "/items/{id}")]
    [InlineData("items/{id:int=5}", "/items/{id}")]
    public void ARoutePattern_IsWrittenAsItsTemplate_WithoutConstraintsOrDefaults(string pattern, string expected) =>
        Assert.Equal(expected, AuditRequestContext.Normalize(RoutePatternFactory.Parse(pattern)));

    private static HttpContext Request(bool authenticated, params object[] metadata)
    {
        var http = new DefaultHttpContext();
        if (authenticated)
            http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "u-1")], "test"));
        http.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask,
            RoutePatternFactory.Parse("api/v{version:apiVersion}/incidents/{id:guid}/resolve"), 0,
            new EndpointMetadataCollection(metadata), "test"));
        return http;
    }

    [Fact]
    public void AnEndpointWithAPolicy_RecordsAnAllowUnderThatPolicy()
    {
        var authorization = AuditRequestContext.Authorization(
            Request(true, new AuthorizeAttribute(), new AuthorizeAttribute { Policy = "CanResolveIncidents" }));

        Assert.Equal("allow", authorization.Decision);
        Assert.Equal("aspnetcore-authorization", authorization.Engine);
        Assert.Equal("CanResolveIncidents", authorization.Policy);
        Assert.Equal(["CanResolveIncidents"], authorization.Permissions);
    }

    [Fact]
    public void TheMostSpecificPolicy_IsThePolicy_AndEveryPolicyIsAPermission()
    {
        var authorization = AuditRequestContext.Authorization(
            Request(true, new AuthorizeAttribute { Policy = "CanViewAuditLog" }, new AuthorizeAttribute { Policy = "CanManageSettings" }));

        Assert.Equal("CanManageSettings", authorization.Policy);
        Assert.Equal(["CanViewAuditLog", "CanManageSettings"], authorization.Permissions);
    }

    [Fact]
    public void AnAuthenticatedEndpointWithoutAPolicy_RecordsAnAllowWithoutOne()
    {
        var authorization = AuditRequestContext.Authorization(Request(true, new AuthorizeAttribute()));

        Assert.Equal("allow", authorization.Decision);
        Assert.Null(authorization.Policy);
        Assert.Null(authorization.Permissions);
    }

    [Fact]
    public void AnonymousEndpoints_NoRequest_AndNoUser_AreNotApplicable()
    {
        Assert.Same(AuditAuthorization.NotApplicable, AuditRequestContext.Authorization(null));
        Assert.Same(AuditAuthorization.NotApplicable, AuditRequestContext.Authorization(Request(false)));
        Assert.Same(AuditAuthorization.NotApplicable,
            AuditRequestContext.Authorization(Request(true, new AuthorizeAttribute(), new AllowAnonymousAttribute())));
    }

    [Theory]
    [InlineData(null, AuditActorType.System)]
    [InlineData("system:webhook", AuditActorType.System)]
    [InlineData("system:maintenance", AuditActorType.System)]
    [InlineData("system", AuditActorType.System)]
    [InlineData("8d3f…user", AuditActorType.User)]
    public void SystemSentinels_AreSystemActors(string? userId, AuditActorType expected) =>
        Assert.Equal(expected, AuditRequestContext.ActorType(userId));

    [Fact]
    public void ARequestWithoutARouteEndpoint_HasNoRoute()
    {
        Assert.Null(AuditRequestContext.RouteTemplate(new DefaultHttpContext()));
        Assert.Null(AuditRequestContext.RouteTemplate(null));
    }

    [Fact]
    public void TheAmbientOverride_WinsInsideItsScope_AndEndsWithIt()
    {
        Assert.Null(AuditAuthorizationScope.Current);
        using (AuditAuthorizationScope.Begin(AuditAuthorization.WebhookKey))
            Assert.Same(AuditAuthorization.WebhookKey, AuditAuthorizationScope.Current);
        Assert.Null(AuditAuthorizationScope.Current);
    }
}
