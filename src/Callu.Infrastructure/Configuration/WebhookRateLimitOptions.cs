namespace Callu.Infrastructure.Configuration;

/// <summary>Binds to the <c>Callu:WebhookRateLimit</c> section; limits the anonymous webhook ingest endpoint per client IP.</summary>
public class WebhookRateLimitOptions
{
    public const string SectionName = "Callu:WebhookRateLimit";

    // A 429 is a 4xx, and monitoring tools do not retry those — an alarm refused here is lost.
    // The default is sized for one monitoring host funnelling a storm through a single address.
    public int PermitLimit { get; set; } = 1000;
    public int WindowSeconds { get; set; } = 60;
    public int QueueLimit { get; set; } = 10;
}
