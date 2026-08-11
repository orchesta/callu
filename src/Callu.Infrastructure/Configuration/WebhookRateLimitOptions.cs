namespace Callu.Infrastructure.Configuration;

/// <summary>Binds to the <c>Callu:WebhookRateLimit</c> section; limits the anonymous webhook ingest endpoint per client IP.</summary>
public class WebhookRateLimitOptions
{
    public const string SectionName = "Callu:WebhookRateLimit";

    // Monitoring tools do not retry a 429, so the default absorbs one host funnelling an alert storm.
    public int PermitLimit { get; set; } = 1000;
    public int WindowSeconds { get; set; } = 60;
    public int QueueLimit { get; set; } = 10;
}
