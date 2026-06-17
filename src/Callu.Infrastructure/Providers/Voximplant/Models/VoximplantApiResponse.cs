using System.Text.Json.Serialization;

namespace Callu.Infrastructure.Providers.Voximplant.Models;

internal class VoximplantApiResponse
{
    [JsonPropertyName("error")]
    public VoximplantApiError? Error { get; set; }
}
