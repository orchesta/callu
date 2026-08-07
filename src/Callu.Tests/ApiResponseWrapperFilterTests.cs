using Callu.Api.Filters;
using Callu.Shared.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace Callu.Tests;

/// <summary>The wrapper keeps a failing endpoint's body, which is the diagnosis.</summary>
public class ApiResponseWrapperFilterTests
{
    private static ObjectResult Wrap(IActionResult result)
    {
        var context = new ResultExecutingContext(
            new ActionContext(new DefaultHttpContext(), new RouteData(), new ControllerActionDescriptor()),
            [],
            result,
            controller: new object());

        new ApiResponseWrapperFilter().OnResultExecuting(context);

        return (ObjectResult)context.Result;
    }

    private sealed record ReadinessDetail(string Status, string Database, string Cache);

    [Fact]
    public void AFailingObjectBody_SurvivesInTheEnvelope()
    {
        var detail = new ReadinessDetail("Unhealthy", "Disconnected", "Connected");

        var wrapped = (ApiResponse<ReadinessDetail>)Wrap(new ObjectResult(detail) { StatusCode = 503 }).Value!;

        Assert.False(wrapped.Success);
        Assert.Same(detail, wrapped.Data);
    }

    [Fact]
    public void ASuccessfulObjectBody_IsStillTheData()
    {
        var detail = new ReadinessDetail("Ready", "Connected", "Connected");

        var wrapped = (ApiResponse<ReadinessDetail>)Wrap(new OkObjectResult(detail)).Value!;

        Assert.True(wrapped.Success);
        Assert.Same(detail, wrapped.Data);
    }

    [Fact]
    public void AFailingStringBody_IsStillTheMessage()
    {
        var wrapped = (ApiResponse<string>)Wrap(new ObjectResult("Nope") { StatusCode = 400 }).Value!;

        Assert.False(wrapped.Success);
        Assert.Equal("Nope", wrapped.Message);
        Assert.Null(wrapped.Data);
    }

    [Fact]
    public void AProblemDetails_StillCollapsesToItsDetailText()
    {
        var wrapped = (ApiResponse<object>)Wrap(
            new ObjectResult(new ProblemDetails { Title = "Bad", Detail = "Field X is required" })
            { StatusCode = 422 }).Value!;

        Assert.False(wrapped.Success);
        Assert.Equal("Field X is required", wrapped.Message);
    }
}
