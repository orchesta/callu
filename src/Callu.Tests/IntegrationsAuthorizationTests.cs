using System.Reflection;
using Callu.Api.Controllers;
using Microsoft.AspNetCore.Authorization;

namespace Callu.Tests;

/// <summary>The Applications area is webhook territory: team leads manage endpoints, while provider setup stays admin-only.</summary>
public class IntegrationsAuthorizationTests
{
    [Fact]
    public void IntegrationsController_RequiresCanManageWebhooks()
    {
        var authorize = typeof(IntegrationsController).GetCustomAttributes<AuthorizeAttribute>(inherit: true).ToList();

        Assert.NotEmpty(authorize);
        Assert.Contains(authorize, a => a.Policy == "CanManageWebhooks");
    }

    [Fact]
    public void ProvidersController_StaysBehindCanManageIntegrations()
    {
        var authorize = typeof(ProvidersController).GetCustomAttributes<AuthorizeAttribute>(inherit: true).ToList();

        Assert.NotEmpty(authorize);
        Assert.Contains(authorize, a => a.Policy == "CanManageIntegrations");
    }
}
