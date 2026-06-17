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

            var wrappedType = typeof(ApiResponse<>).MakeGenericType(value?.GetType() ?? typeof(object));
            var wrapped = Activator.CreateInstance(wrappedType);
            
            bool isSuccess = objectResult.StatusCode is null or >= 200 and < 300;
            
            wrappedType.GetProperty("Success")!.SetValue(wrapped, isSuccess);
            
            if (isSuccess)
            {
                wrappedType.GetProperty("Data")!.SetValue(wrapped, value);
            }
            else
            {
                var message = value as string ?? "Request failed";
                wrappedType.GetProperty("Message")!.SetValue(wrapped, message);
            }

            objectResult.Value = wrapped;
        }
        else if (context.Result is NoContentResult)
        {
        }
        else if (context.Result is NotFoundResult)
        {
            context.Result = new NotFoundObjectResult(ApiResponse.Fail("Resource not found"));
        }
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
    }
}
