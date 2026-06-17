namespace Callu.Api.Middleware;

/// <summary>
/// Adds standard security headers to all HTTP responses.
/// Mitigates XSS, clickjacking, MIME sniffing, referrer leakage, and protocol downgrade attacks.
/// </summary>
public class SecurityHeadersMiddleware(RequestDelegate next, IConfiguration configuration)
{
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
            headers.Append("Strict-Transport-Security", "max-age=31536000; includeSubDomains; preload");

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

    /// <summary>
    /// Translate the configured FrontendUrl into a wss:// (and ws:// for
    /// browsers that still negotiate plaintext for HTTP origins) host suffix
    /// the CSP can accept. Returns "wss:" as a permissive fallback when the
    /// config is missing or malformed — preserves connectivity rather than
    /// breaking the SPA on misconfiguration.
    /// </summary>
    private string ResolveAllowedWebSocketOrigin()
    {
        var raw = configuration["CalluSettings:FrontendUrl"];
        if (string.IsNullOrWhiteSpace(raw) || !Uri.TryCreate(raw, UriKind.Absolute, out var uri))
            return "wss:";

        var host = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
        return uri.Scheme == "https" ? $"wss://{host}" : $"wss://{host} ws://{host}";
    }
}

public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        return app.UseMiddleware<SecurityHeadersMiddleware>();
    }
}
