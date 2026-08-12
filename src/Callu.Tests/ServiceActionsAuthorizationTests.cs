using System.Reflection;
using Callu.Api.Controllers;
using Microsoft.AspNetCore.Authorization;

namespace Callu.Tests;

/// <summary>Viewing actions rides the service page; changing them is a manage act; running one against a real system is its own permission.</summary>
public class ServiceActionsAuthorizationTests
{
    [Fact]
    public void ServiceActionsController_ReadsBehindCanViewServices()
    {
        var authorize = typeof(ServiceActionsController).GetCustomAttributes<AuthorizeAttribute>(inherit: true).ToList();

        Assert.NotEmpty(authorize);
        Assert.Contains(authorize, a => a.Policy == "CanViewServices");
    }

    [Theory]
    [InlineData(nameof(ServiceActionsController.Create))]
    [InlineData(nameof(ServiceActionsController.Update))]
    [InlineData(nameof(ServiceActionsController.Delete))]
    public void EveryWrite_RequiresCanManageServices(string method)
    {
        var authorize = typeof(ServiceActionsController)
            .GetMethod(method)!
            .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .ToList();

        Assert.Contains(authorize, a => a.Policy == "CanManageServices");
    }

    [Fact]
    public void ExecutingAnAction_RequiresItsOwnClaim_NotJustAcknowledge()
    {
        var authorize = typeof(IncidentsController)
            .GetMethod(nameof(IncidentsController.ExecuteServiceAction))!
            .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .ToList();

        Assert.Contains(authorize, a => a.Policy == "CanExecuteServiceActions");
    }
}
