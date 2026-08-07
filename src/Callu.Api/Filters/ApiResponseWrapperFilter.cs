using Callu.Shared.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Callu.Api.Filters;

/// <summary>
/// Automatically wraps successful OkObjectResult responses in ApiResponse{T} envelope.
/// Error responses are handled by GlobalExceptionMiddleware.
/// </summary>
public class ApiResponseWrapperFilter : IResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        var hasSkipAttribute = context.ActionDescriptor.EndpointMetadata
            .Any(m => m.GetType() == typeof(SkipApiResponseWrapperAttribute));

        if (hasSkipAttribute)
            return;

        if (context.Result is ObjectResult objectResult)
        {
            var value = objectResult.Value;
            if (value != null)
            {
                var type = value.GetType();
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ApiResponse<>))
                    return;
            }

            bool isSuccess = objectResult.StatusCode is null or >= 200 and < 300;

            if (!isSuccess && value is ProblemDetails problemDetails)
            {
                objectResult.Value = ApiResponse.Fail<object>(
                    FirstNonEmpty(problemDetails.Detail, problemDetails.Title) ?? "Request failed",
                    problemDetails is ValidationProblemDetails { Errors.Count: > 0 } validationProblem
                        ? new Dictionary<string, string[]>(validationProblem.Errors)
                        : null);
                return;
            }

            var wrappedType = typeof(ApiResponse<>).MakeGenericType(value?.GetType() ?? typeof(object));
            var wrapped = Activator.CreateInstance(wrappedType);

            wrappedType.GetProperty("Success")!.SetValue(wrapped, isSuccess);
            
            if (isSuccess)
            {
                wrappedType.GetProperty("Data")!.SetValue(wrapped, value);
            }
            else
            {
                wrappedType.GetProperty("Message")!.SetValue(wrapped, value as string ?? "Request failed");

                // A non-string failure body is the diagnosis, not noise.
                if (value is not string)
                    wrappedType.GetProperty("Data")!.SetValue(wrapped, value);
            }

            objectResult.Value = wrapped;
        }
        else if (context.Result is NoContentResult)
        {
            // A 204 stays bodiless; wrapping it would invent a body.
        }
        else if (context.Result is NotFoundResult)
        {
            context.Result = new NotFoundObjectResult(ApiResponse.Fail("Resource not found"));
        }
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
    }

    private static string? FirstNonEmpty(string? primary, string? fallback) =>
        !string.IsNullOrWhiteSpace(primary) ? primary
        : !string.IsNullOrWhiteSpace(fallback) ? fallback
        : null;
}
