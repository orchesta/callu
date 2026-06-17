using System.Text.Json;

namespace Callu.Infrastructure.Providers.Voximplant.Models;

/// <summary>
/// Single options instance for READING a Voximplant provider ConfigJson blob. Case-insensitive
/// so it tolerates the camelCase write policy regardless of whether a DTO carries
/// [JsonPropertyName]. Sharing one instance removes the previous three-way divergence
/// (case-insensitive deserialize vs attribute-based deserialize vs raw JsonDocument walk),
/// where any reader that forgot the case-insensitive flag silently bound null. (VOX-5)
/// </summary>
internal static class VoximplantJsonOptions
{
    public static readonly JsonSerializerOptions Read = new() { PropertyNameCaseInsensitive = true };
}
