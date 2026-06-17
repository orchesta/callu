using System.Collections.Concurrent;
using System.Net;
using System.Reflection;

namespace Callu.Infrastructure.Email;

/// <summary>
/// Email template manager - loads templates from files with caching
/// Templates are stored in the Templates folder and can be modified without recompilation
/// </summary>
public static class EmailTemplates
{
    private static readonly string TemplatesPath;
    private static readonly ConcurrentDictionary<string, string> TemplateCache = new();
    private static readonly object CacheLock = new();
    private static DateTime _lastCacheRefresh = DateTime.MinValue;
    private static readonly TimeSpan CacheRefreshInterval = TimeSpan.FromMinutes(5);

    static EmailTemplates()
    {
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        var assemblyDirectory = Path.GetDirectoryName(assemblyLocation) ?? AppContext.BaseDirectory;
        TemplatesPath = Path.Combine(assemblyDirectory, "Email", "Templates");

        if (!Directory.Exists(TemplatesPath))
        {
            TemplatesPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", 
                "Callu.Infrastructure", "Email", "Templates");
        }
    }

    /// <summary>
    /// Clear template cache to force reload
    /// </summary>
    public static void ClearCache()
    {
        TemplateCache.Clear();
        _lastCacheRefresh = DateTime.MinValue;
    }

    /// <summary>
    /// Load a template from file with caching
    /// </summary>
    private static string LoadTemplate(string templateName)
    {
        if (DateTime.UtcNow - _lastCacheRefresh > CacheRefreshInterval)
        {
            lock (CacheLock)
            {
                if (DateTime.UtcNow - _lastCacheRefresh > CacheRefreshInterval)
                {
                    TemplateCache.Clear();
                    _lastCacheRefresh = DateTime.UtcNow;
                }
            }
        }

        return TemplateCache.GetOrAdd(templateName, name =>
        {
            var filePath = Path.Combine(TemplatesPath, $"{name}.html");
            
            if (File.Exists(filePath))
            {
                return File.ReadAllText(filePath);
            }

            return GetFallbackTemplate(name);
        });
    }

    /// <summary>
    /// HTML-encode a user-controlled value before inlining it into an email template.
    /// All data-bound placeholders in this file flow through this helper so an
    /// incident title like <c>&lt;img src=x onerror=...&gt;</c> cannot execute in
    /// the recipient's email client.
    /// </summary>
    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    /// <summary>
    /// Wrap content with base template
    /// </summary>
    private static string WrapWithBase(string content, string title)
    {
        var baseTemplate = LoadTemplate("_base");
        return baseTemplate
            .Replace("{{Title}}", H(title))
            .Replace("{{Content}}", content);
    }

    /// <summary>
    /// Generate invitation email HTML
    /// </summary>
    public static string GetInvitationEmail(string userName, string inviteLink)
    {
        var template = LoadTemplate("invitation");

        if (string.IsNullOrEmpty(userName))
        {
            template = template
                .Replace("{{#UserName}}", "")
                .Replace("{{/UserName}}", "")
                .Replace(" <strong>{{UserName}}</strong>", "");
        }
        else
        {
            template = template
                .Replace("{{#UserName}}", "")
                .Replace("{{/UserName}}", "")
                .Replace("{{UserName}}", H(userName));
        }

        template = template.Replace("{{InviteLink}}", H(inviteLink));

        return WrapWithBase(template, "You're Invited to CalluApp");
    }

    /// <summary>
    /// Generate password reset email HTML
    /// </summary>
    public static string GetPasswordResetEmail(string resetLink)
    {
        var template = LoadTemplate("password_reset");
        template = template.Replace("{{ResetLink}}", H(resetLink));
        
        return WrapWithBase(template, "Reset Your CalluApp Password");
    }

    /// <summary>
    /// Generate on-call notification email HTML
    /// </summary>
    public static string GetOnCallNotificationEmail(string incidentTitle, string severity, string incidentUrl)
    {
        var severityStyle = severity.ToLower() switch
        {
            "critical" => "color: #dc2626; font-weight: bold;",
            "high" => "color: #ea580c; font-weight: bold;",
            "medium" => "color: #ca8a04; font-weight: bold;",
            _ => "color: #16a34a; font-weight: bold;"
        };

        var template = LoadTemplate("oncall_notification");
        template = template
            .Replace("{{IncidentTitle}}", H(incidentTitle))
            .Replace("{{Severity}}", H(severity))
            .Replace("{{SeverityStyle}}", severityStyle)
            .Replace("{{IncidentUrl}}", H(incidentUrl));

        return WrapWithBase(template, $"Incident Alert: {incidentTitle}");
    }

    /// <summary>
    /// Generate test email HTML
    /// </summary>
    public static string GetTestEmail()
    {
        var template = LoadTemplate("test");
        template = template.Replace("{{Timestamp}}", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC"));
        return WrapWithBase(template, "CalluApp - Test Email");
    }

    /// <summary>
    /// Status-page subscription confirmation (double opt-in). Inline minimal body —
    /// the proper template lands with fix 07.F5's EmailTemplate DB system.
    /// </summary>
    public static string GetStatusPageSubscriptionConfirmationEmail(string pageName, string confirmLink)
    {
        var safePageName = H(pageName);
        var safeLink = H(confirmLink);
        var body = $"""
            <p>You requested email notifications from the <strong>{safePageName}</strong> status page.</p>
            <p>Confirm your subscription by clicking the link below. The link expires in 24 hours.</p>
            <p><a href="{safeLink}" style="display:inline-block;padding:10px 18px;background:#2563eb;color:#fff;text-decoration:none;border-radius:6px;">Confirm subscription</a></p>
            <p style="color:#6b7280;font-size:12px;">If you didn't request this, ignore the email — the subscription stays inactive and the link expires.</p>
            """;
        return WrapWithBase(body, $"Confirm your subscription to {pageName}");
    }

    /// <summary>
    /// Get fallback template if file not found
    /// </summary>
    private static string GetFallbackTemplate(string templateName)
    {
        return templateName switch
        {
            "_base" => """
                <!DOCTYPE html>
                <html>
                <head><meta charset="utf-8"><title>{{Title}}</title></head>
                <body style="font-family: Arial, sans-serif; padding: 20px;">
                    <div style="max-width: 600px; margin: 0 auto; background: #fff; padding: 20px; border-radius: 8px;">
                        <h1 style="color: #6366f1;">CalluApp</h1>
                        {{Content}}
                    </div>
                </body>
                </html>
                """,
            "invitation" => """
                <h2>You've been invited to CalluApp!</h2>
                <p>Click here to accept: <a href="{{InviteLink}}">{{InviteLink}}</a></p>
                """,
            "password_reset" => """
                <h2>Reset Your Password</h2>
                <p>Click here to reset: <a href="{{ResetLink}}">{{ResetLink}}</a></p>
                """,
            "oncall_notification" => """
                <h2>Incident Alert: {{IncidentTitle}}</h2>
                <p>Severity: {{Severity}}</p>
                <p><a href="{{IncidentUrl}}">View Incident</a></p>
                """,
            "test" => """
                <h2>Test Email</h2>
                <p>Your SMTP configuration is working correctly.</p>
                """,
            _ => $"<p>Template '{templateName}' not found.</p>"
        };
    }
}
