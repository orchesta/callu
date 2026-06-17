using System.Reflection;
using Callu.Api.Controllers;
using Microsoft.AspNetCore.Authorization;

namespace Callu.Tests;

/// <summary>
/// Guards the H3 fix: the analytics controllers must require the CanViewReports policy, not a bare
/// [Authorize]. A regression (dropping the policy) would re-expose org-wide MTTA/MTTR and team
/// performance to any authenticated principal. Reflection guard — cheaper and more robust than
/// booting the full API, which is impractical here because startup runs PostgreSQL migrations.
/// </summary>
public class AnalyticsAuthorizationTests
{
    [Theory]
    [InlineData(typeof(ReportsController))]
    [InlineData(typeof(DashboardController))]
    public void AnalyticsController_RequiresCanViewReportsPolicy(Type controller)
    {
        var authorize = controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true).ToList();

        Assert.NotEmpty(authorize);
        Assert.Contains(authorize, a => a.Policy == "CanViewReports");
    }
}
