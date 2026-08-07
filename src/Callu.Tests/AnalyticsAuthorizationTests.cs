using System.Reflection;
using Callu.Api.Controllers;
using Microsoft.AspNetCore.Authorization;

namespace Callu.Tests;

/// <summary>The analytics controllers require the reports policy rather than a bare [Authorize].</summary>
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
