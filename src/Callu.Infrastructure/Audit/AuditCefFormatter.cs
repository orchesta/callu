using System.Globalization;
using System.Text;
using Callu.Domain.Entities;
using Callu.Domain.Enums;

namespace Callu.Infrastructure.Audit;

/// <summary>Renders an audit entry as an RFC 5424 frame carrying a CEF message.</summary>
public static class AuditCefFormatter
{
    private const string Vendor = "Callu";
    private const string Product = "Callu";
    private const string CefVersion = "0";
    private const string MsgId = "AUDIT";
    private const int Informational = 6;

    /// <summary>Signals that MSG is UTF-8, which RFC 5424 requires when it is not pure ASCII.</summary>
    private const string Utf8Bom = "﻿";

    public static string Frame(AuditLog entry, AuditSyslogOptions options, string hostName, string productVersion)
    {
        var priority = options.Facility * 8 + Informational;
        var timestamp = entry.CreatedAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);

        return $"<{priority}>1 {timestamp} {Nil(hostName)} {Nil(options.AppName)} - {MsgId} - "
            + Utf8Bom + Message(entry, productVersion);
    }

    /// <summary>The CEF line: seven header fields, then key=value extensions.</summary>
    public static string Message(AuditLog entry, string productVersion)
    {
        var header = string.Join('|',
            "CEF:" + CefVersion,
            Header(Vendor),
            Header(Product),
            Header(productVersion),
            Header(entry.Action.ToString()),
            Header(Name(entry)),
            Severity(entry.Action).ToString(CultureInfo.InvariantCulture));

        var extension = new StringBuilder();
        Extend(extension, "rt", entry.CreatedAt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
        Extend(extension, "externalId", entry.Id.ToString("D"));
        Extend(extension, "suser", entry.ActorDisplayName);
        Extend(extension, "suid", entry.ActorId);
        Extend(extension, "src", entry.RequestIpAddress);
        Extend(extension, "requestClientApplication", entry.RequestUserAgent);
        Extend(extension, "request", entry.RequestRoute);
        Extend(extension, "cs1Label", "resourceType");
        Extend(extension, "cs1", entry.ResourceType);
        Extend(extension, "cs2Label", "resourceId");
        Extend(extension, "cs2", entry.ResourceId?.ToString("D"));
        Extend(extension, "cs3Label", "chainSequence");
        Extend(extension, "cs3", entry.Sequence?.ToString(CultureInfo.InvariantCulture));
        Extend(extension, "msg", entry.Summary);

        return header + "|" + extension.ToString().TrimEnd();
    }

    private static string Name(AuditLog entry) =>
        string.IsNullOrWhiteSpace(entry.ResourceType)
            ? entry.Action.ToString()
            : $"{entry.Action} {entry.ResourceType}";

    // A reviewer scanning by severity should see the security-relevant entries first; everything
    // else is ordinary activity.
    private static int Severity(AuditAction action) => action switch
    {
        AuditAction.IntegrityBroken => 10,
        AuditAction.LoginFailed => 7,
        AuditAction.RoleAssigned or AuditAction.RoleRemoved => 7,
        AuditAction.SettingsChanged or AuditAction.PasswordChanged => 6,
        AuditAction.Deleted or AuditAction.Exported => 6,
        _ => 3,
    };

    private static void Extend(StringBuilder text, string key, string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        text.Append(key).Append('=').Append(ExtensionValue(value)).Append(' ');
    }

    /// <summary>Header fields escape a backslash and a pipe; a newline would break the frame.</summary>
    internal static string Header(string? value) =>
        (value ?? string.Empty)
            .Replace("\\", "\\\\")
            .Replace("|", "\\|")
            .Replace("\r", " ")
            .Replace("\n", " ");

    /// <summary>Extension values escape a backslash, an equals sign and a newline.</summary>
    internal static string ExtensionValue(string value) =>
        value
            .Replace("\\", "\\\\")
            .Replace("=", "\\=")
            .Replace("\r\n", "\\n")
            .Replace("\r", "\\n")
            .Replace("\n", "\\n");

    private static string Nil(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "-" : value.Replace(' ', '_');
}
