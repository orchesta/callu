using System.Text.Json;
using System.Text.Json.Serialization;

namespace Callu.Infrastructure.Providers.Voximplant.Models;

public class VoxAccountInfoResponse : VoxBaseResponse
{
    /// <summary>
    /// Parsed account info from the result object
    /// </summary>
    [JsonIgnore]
    public AccountInfoData? AccountInfo
    {
        get
        {
            if (Result?.ValueKind == JsonValueKind.Object)
            {
                return JsonSerializer.Deserialize<AccountInfoData>(Result.Value.GetRawText(), 
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            return null;
        }
    }
}
