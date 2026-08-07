using System.Text.Json;
using System.Text.Json.Serialization;

namespace Callu.Infrastructure.Providers.Voximplant.Models;

public class VoxBaseResponse
{
    /// <summary>
    /// Voximplant API returns "result" as either an int (1 for success) or an object (for query responses).
    /// Using JsonElement to handle both cases.
    /// </summary>
    [JsonPropertyName("result")]
    public JsonElement? Result { get; set; }
    
    [JsonPropertyName("error")]
    public VoxApiError? Error { get; set; }
    
    public bool IsSuccess => Error == null && Result.HasValue;
}
