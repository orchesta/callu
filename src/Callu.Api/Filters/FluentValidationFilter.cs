using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Callu.Shared.Results;

namespace Callu.Api.Filters;

/// <summary>
/// Action filter that auto-validates request body parameters using FluentValidation.
/// Returns 400 with ApiResponse envelope on validation failure.
/// </summary>
public class FluentValidationFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var (key, value) in context.ActionArguments)
        {
            if (value is null) continue;

            var valueType = value.GetType();
            var validatorType = typeof(IValidator<>).MakeGenericType(valueType);

            if (context.HttpContext.RequestServices.GetService(validatorType) is not IValidator validator)
                continue;

            var validationContext = new ValidationContext<object>(value);
            var result = await validator.ValidateAsync(validationContext, context.HttpContext.RequestAborted);

            if (!result.IsValid)
            {
                var errors = result.Errors
                    .GroupBy(f => f.PropertyName)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(f => f.ErrorMessage).ToArray());

                context.Result = new BadRequestObjectResult(
                    ApiResponse.Fail<object>("One or more validation errors occurred.", errors));
                return;
            }
        }

        await next();
    }
}
