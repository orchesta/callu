using System.Net;
using Callu.Shared.Exceptions;
using Callu.Shared.Localization;
using Callu.Shared.Results;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SharedValidationException = Callu.Shared.Exceptions.ValidationException;

namespace Callu.Api.ExceptionHandlers;

/// <summary>
/// Global exception handler using .NET 8+ IExceptionHandler interface.
/// Maps domain exceptions to appropriate HTTP status codes and ApiResponse format.
/// Replaces the legacy GlobalExceptionMiddleware.
/// </summary>
public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogDebug("Request cancelled by client: {Path}", httpContext.Request.Path);
            httpContext.Response.StatusCode = 499;
            return true;
        }

        var (statusCode, response) = MapExceptionToResponse(exception);

        if (statusCode == HttpStatusCode.InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception: {Message}", exception.Message);
        }
        else
        {
            logger.LogWarning("Handled exception ({StatusCode}): {Message}", (int)statusCode, exception.Message);
        }

        httpContext.Response.StatusCode = (int)statusCode;
        await httpContext.Response.WriteAsJsonAsync(response, cancellationToken);

        return true;
    }

    private static (HttpStatusCode StatusCode, ApiResponse<object> Response) MapExceptionToResponse(Exception exception) =>
        exception switch
        {
            NotFoundException ex => (HttpStatusCode.NotFound, ApiResponse.Fail<object>(ex.Message)),

            SharedValidationException vex when vex.Errors is { Count: > 0 } => (
                HttpStatusCode.BadRequest,
                ApiResponse.Fail<object>(vex.Message, vex.Errors)),

            SharedValidationException vex => (
                HttpStatusCode.BadRequest,
                ApiResponse.Fail<object>(vex.Message)),

            FluentValidation.ValidationException fvex => (
                HttpStatusCode.BadRequest,
                ApiResponse.Fail<object>(
                    string.IsNullOrWhiteSpace(fvex.Message)
                        ? "One or more validation errors occurred."
                        : fvex.Message,
                    fvex.Errors
                        .GroupBy(e => string.IsNullOrEmpty(e.PropertyName) ? "General" : e.PropertyName)
                        .ToDictionary(
                            g => g.Key,
                            g => g.Select(x => x.ErrorMessage).ToArray()))),

            ConflictException ex => (HttpStatusCode.Conflict, ApiResponse.Fail<object>(ex.Message)),

            DbUpdateException { InnerException: PostgresException { SqlState: "23505" } } => (
                HttpStatusCode.Conflict,
                ApiResponse.Fail<object>("A record with the same unique value already exists.")),

            ForbiddenException => (
                HttpStatusCode.Forbidden,
                ApiResponse.Fail<object>(Messages.Get("errors.forbidden"))),

            UnauthorizedException => (
                HttpStatusCode.Unauthorized,
                ApiResponse.Fail<object>(Messages.Get("errors.unauthorized"))),

            BusinessRuleException ex => (
                HttpStatusCode.UnprocessableEntity,
                ApiResponse.Fail<object>(ex.Message)),

            DomainException ex => (HttpStatusCode.BadRequest, ApiResponse.Fail<object>(ex.Message)),

            ArgumentException => (
                HttpStatusCode.BadRequest,
                ApiResponse.Fail<object>(Messages.Get("errors.invalidParams"))),

            _ => (
                HttpStatusCode.InternalServerError,
                ApiResponse.Fail<object>(Messages.Get("errors.unexpected")))
        };
}
