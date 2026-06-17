using System.Text.Json.Serialization;

namespace Callu.Infrastructure.Providers.Voximplant.Models;

/// <summary>
/// Internal wrapper over <see cref="VoximplantConfig"/> used by the lifecycle code.
/// Must carry every field the base config has — the lifecycle round-trips the whole
/// object (read → mutate → write), and any field missing here would be silently
/// dropped on save, breaking the runtime (e.g. empty ApplicationName/AccountName
/// would yield a malformed FQDN when computing the Web SDK login hash).
/// </summary>
internal class VoximplantConfigWithProvisioning
{
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long AccountId { get; set; }
    public string ApiKey { get; set; } = string.Empty;
    public string ApplicationName { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long? IncidentCallRuleId { get; set; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long? ConferenceRuleId { get; set; }

    public string? CallbackBaseUrl { get; set; }
    public string? DefaultCallerId { get; set; }
    public string? ServiceAccountJson { get; set; }
    public ProvisioningConfig? Provisioning { get; set; }
}
