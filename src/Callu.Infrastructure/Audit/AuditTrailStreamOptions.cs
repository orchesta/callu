using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;
using Serilog.Filters;
using Serilog.Formatting.Compact;

namespace Callu.Infrastructure.Audit;

/// <summary>Binds to the <c>Callu:AuditTrailStream</c> section; an empty path turns the stream off.</summary>
public class AuditTrailStreamOptions
{
    public const string SectionName = "Callu:AuditTrailStream";

    public string? Path { get; set; }
    public int RetainedFileCountLimit { get; set; } = 90;
    public long FileSizeLimitBytes { get; set; } = 52_428_800;
}

public static class AuditTrailStreamLogging
{
    /// <summary>Adds a sub-logger carrying only audit entries, one compact JSON object per line.</summary>
    public static LoggerConfiguration WriteToAuditTrailStream(
        this LoggerConfiguration logger, IConfiguration configuration)
    {
        var options = new AuditTrailStreamOptions();
        configuration.GetSection(AuditTrailStreamOptions.SectionName).Bind(options);

        if (string.IsNullOrWhiteSpace(options.Path)) return logger;

        // The host's own minimum level is Warning; without this override the audit entries would be
        // filtered out before reaching the sub-logger.
        return logger
            .MinimumLevel.Override(AuditTrailStream.SourceContext, LogEventLevel.Information)
            .WriteTo.Logger(stream => stream
                .Filter.ByIncludingOnly(Matching.FromSource(AuditTrailStream.SourceContext))
                .WriteTo.File(
                    new CompactJsonFormatter(),
                    options.Path,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: options.RetainedFileCountLimit,
                    fileSizeLimitBytes: options.FileSizeLimitBytes,
                    rollOnFileSizeLimit: true));
    }
}
