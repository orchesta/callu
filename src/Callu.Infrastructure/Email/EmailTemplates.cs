using System.Collections.Concurrent;
using System.Net;
using System.Reflection;

namespace Callu.Infrastructure.Email;

/// <summary>One built-in template in editor-ready form (B4 seed).</summary>
public sealed record SeedEmailTemplate(string Key, string Name, string Subject, string HtmlBody, string Description);

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
    /// Inline style for the severity badge — shared by the file path and the DB-template
    /// path (B4 review fix: the DB path must supply {{SeverityStyle}} too).
    /// </summary>
    public static string GetSeverityStyle(string severity) => severity.ToLower() switch
    {
        "critical" => "color: #dc2626; font-weight: bold;",
        "high" => "color: #ea580c; font-weight: bold;",
        "medium" => "color: #ca8a04; font-weight: bold;",
        _ => "color: #16a34a; font-weight: bold;"
    };

    /// <summary>HTML-encodes a user-controlled value before inlining it into an email template.</summary>
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
    /// Generate conference invite email HTML (join link for an incident's video war-room)
    /// </summary>
    public static string GetConferenceInviteEmail(string incidentTitle, string severity, string conferenceUrl, int durationMinutes)
    {
        var severityStyle = severity.ToLower() switch
        {
            "critical" => "color: #dc2626; font-weight: bold;",
            "high" => "color: #ea580c; font-weight: bold;",
            "medium" => "color: #ca8a04; font-weight: bold;",
            _ => "color: #16a34a; font-weight: bold;"
        };

        var template = LoadTemplate("conference_invite");
        template = template
            .Replace("{{IncidentTitle}}", H(incidentTitle))
            .Replace("{{Severity}}", H(severity))
            .Replace("{{SeverityStyle}}", severityStyle)
            .Replace("{{ConferenceUrl}}", H(conferenceUrl))
            .Replace("{{DurationMinutes}}", durationMinutes.ToString());

        return WrapWithBase(template, $"Video Conference: {incidentTitle}");
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
    /// Status-page subscription confirmation (double opt-in), with a minimal inline body.
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
    /// Seed content for the email-template editor: the built-in file templates with tokens intact.
    /// </summary>
    public static IReadOnlyList<SeedEmailTemplate> GetSeedTemplates() =>
    [
        new("invitation", "User Invitation",
            "You've been invited to CalluApp",
            SeedHtml("invitation", "You're Invited to CalluApp", stripUserNameConditionals: true),
            "Sent when an administrator invites a new user. Variables: {{UserName}}, {{InviteLink}}."),
        new("password_reset", "Password Reset",
            "Reset your CalluApp password",
            SeedHtml("password_reset", "Reset Your CalluApp Password"),
            "Sent on a password-reset request. Variables: {{ResetLink}}."),
        new("oncall_notification", "On-Call Incident Notification",
            "Incident Alert: {{IncidentTitle}}",
            SeedHtml("oncall_notification", "Incident Alert"),
            "Sent to a paged responder. Variables: {{IncidentTitle}}, {{Severity}}, {{SeverityStyle}}, {{IncidentUrl}}."),
        new("conference_invite", "Conference Invite",
            "Video Conference: {{IncidentTitle}}",
            SeedHtml("conference_invite", "Video Conference Invitation"),
            "Sent when an incident war-room is started. Variables: {{IncidentTitle}}, {{Severity}}, {{SeverityStyle}}, {{ConferenceUrl}}, {{DurationMinutes}}."),
        new("status_page_subscription_confirmation", "Status Page Subscription Confirmation",
            "Confirm your subscription to {{PageName}}",
            WrapWithBase("""
                <p>You requested email notifications from the <strong>{{PageName}}</strong> status page.</p>
                <p>Confirm your subscription by clicking the link below. The link expires in 24 hours.</p>
                <p><a href="{{ConfirmLink}}" style="display:inline-block;padding:10px 18px;background:#2563eb;color:#fff;text-decoration:none;border-radius:6px;">Confirm subscription</a></p>
                <p style="color:#6b7280;font-size:12px;">If you didn't request this, ignore the email — the subscription stays inactive and the link expires.</p>
                """, "Confirm your subscription"),
            "Status-page double opt-in confirmation. Variables: {{PageName}}, {{ConfirmLink}}."),
    ];

    private static string SeedHtml(string templateName, string title, bool stripUserNameConditionals = false)
    {
        var inner = LoadTemplate(templateName);
        if (stripUserNameConditionals)
        {
            // The file loader handles {{#UserName}} blocks in code; the WYSIWYG seed copy
            // keeps it simple: the greeting always shows {{UserName}}.
            inner = inner.Replace("{{#UserName}}", "").Replace("{{/UserName}}", "");
        }
        return WrapWithBase(inner, title);
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
            "conference_invite" => """
                <h2>Video Conference Invitation</h2>
                <p>Incident: {{IncidentTitle}} (<span style="{{SeverityStyle}}">{{Severity}}</span>)</p>
                <p><a href="{{ConferenceUrl}}">Join Conference</a></p>
                <p>The room stays open for {{DurationMinutes}} minutes.</p>
                """,
            "test" => """
                <h2>Test Email</h2>
                <p>Your SMTP configuration is working correctly.</p>
                """,
            _ => $"<p>Template '{templateName}' not found.</p>"
        };
    }
}
