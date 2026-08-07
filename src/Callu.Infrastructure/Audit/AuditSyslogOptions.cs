namespace Callu.Infrastructure.Audit;

/// <summary>Binds to the <c>Callu:AuditSyslog</c> section; an empty host turns forwarding off.</summary>
public class AuditSyslogOptions
{
    public const string SectionName = "Callu:AuditSyslog";

    public string? Host { get; set; }

    public int Port { get; set; } = 6514;

    /// <summary>TLS is the default; plaintext has to be asked for.</summary>
    public bool UseTls { get; set; } = true;

    /// <summary>Only for a collector with a certificate this host cannot chain, and it is logged.</summary>
    public bool AcceptAnyCertificate { get; set; }

    /// <summary>Syslog facility; 13 is "log audit".</summary>
    public int Facility { get; set; } = 13;

    public string AppName { get; set; } = "callu";

    /// <summary>Entries held while the collector is unreachable, before the oldest are dropped.</summary>
    public int QueueCapacity { get; set; } = 10_000;

    public int ConnectTimeoutSeconds { get; set; } = 10;

    public bool Enabled => !string.IsNullOrWhiteSpace(Host);
}
