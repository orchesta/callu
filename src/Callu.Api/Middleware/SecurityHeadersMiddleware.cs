namespace Callu.Api.Middleware;

/// <summary>
/// Adds standard security headers to all HTTP responses.
/// Mitigates XSS, clickjacking, MIME sniffing, referrer leakage, and protocol downgrade attacks.
/// </summary>
public class SecurityHeadersMiddleware(RequestDelegate next, IConfiguration configuration)
{
    public const int DefaultHstsMaxAgeSeconds = 300;

    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        headers.Append("X-Content-Type-Options", "nosniff");

        headers.Append("X-Frame-Options", "DENY");

        headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");

        headers.Append("X-Permitted-Cross-Domain-Policies", "none");

        headers.Append("Permissions-Policy", "camera=(), microphone=(self), geolocation=()");

        var isDevelopment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";

        if (!isDevelopment)
        {
            var hsts = BuildHstsValue(configuration);
            if (hsts is not null)
                headers.Append("Strict-Transport-Security", hsts);

            var allowedWs = ResolveAllowedWebSocketOrigin();
            headers.Append("Content-Security-Policy", string.Join("; ",
                "default-src 'self'",
                "script-src 'self'",
                "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com",
                "style-src-elem 'self' https://fonts.googleapis.com",
                "style-src-attr 'unsafe-inline'",
                "font-src 'self' https://fonts.gstatic.com",
                "img-src 'self' data: blob:",
                $"connect-src 'self' {allowedWs}",
                "frame-ancestors 'none'",
                "base-uri 'self'",
                "form-action 'self'",
                "object-src 'none'",
                "upgrade-insecure-requests"
            ));
        }

        headers.Append("Cross-Origin-Opener-Policy", "same-origin");

        headers.Append("X-XSS-Protection", "1; mode=block");

        await next(context);
    }

    /// <summary>Builds the HSTS value, or null when the operator disabled it.</summary>
    // Deliberately short by default and never `preload`: a self-hosted install whose TLS breaks is
    // unreachable for the whole max-age, and only the domain owner may ask to be preloaded.
    public static string? BuildHstsValue(IConfiguration configuration)
    {
        var section = configuration.GetSection("Callu:Security");

        var maxAge = section.GetValue("HstsMaxAgeSeconds", DefaultHstsMaxAgeSeconds);
        if (maxAge <= 0) return null;

        var value = $"max-age={maxAge}";
        if (section.GetValue("HstsIncludeSubDomains", false)) value += "; includeSubDomains";
        if (section.GetValue("HstsPreload", false)) value += "; preload";

        return value;
    }

    /// <summary>Resolves the CSP <c>connect-src</c> WebSocket origin from configuration.</summary>
    private string ResolveAllowedWebSocketOrigin()
        => ResolveWebSocketOrigin(
            configuration["CalluSettings:ApiUrl"],
            configuration["CalluSettings:FrontendUrl"]);

    /// <summary>Translates the first well-formed of <paramref name="apiUrl"/>, <paramref name="frontendUrl"/> into a
    /// WebSocket host the CSP accepts; falls back to the permissive <c>"wss:"</c> rather than breaking the SPA.</summary>
    public static string ResolveWebSocketOrigin(string? apiUrl, string? frontendUrl)
    {
        foreach (var raw in new[] { apiUrl, frontendUrl })
        {
            if (string.IsNullOrWhiteSpace(raw) || !Uri.TryCreate(raw, UriKind.Absolute, out var uri))
                continue;

            var host = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
            return uri.Scheme == "https" ? $"wss://{host}" : $"wss://{host} ws://{host}";
        }

        return "wss:";
    }
}

public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        return app.UseMiddleware<SecurityHeadersMiddleware>();
    }
}
