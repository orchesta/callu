using Callu.Infrastructure.Telemetry;
using Microsoft.AspNetCore.Http;
using Serilog.Events;

namespace Callu.Api.Configuration;

/// <summary>The message-template properties for Serilog's request log, with the request path redacted.</summary>
public static class RequestLoggingProperties
{
    public static IEnumerable<LogEventProperty> Get(
        HttpContext httpContext, string requestPath, double elapsedMs, int statusCode) =>
    [
        new LogEventProperty("RequestMethod", new ScalarValue(httpContext.Request.Method)),
        new LogEventProperty("RequestPath", new ScalarValue(SensitiveQueryRedactor.RedactTarget(requestPath))),
        new LogEventProperty("StatusCode", new ScalarValue(statusCode)),
        new LogEventProperty("Elapsed", new ScalarValue(elapsedMs))
    ];
}
