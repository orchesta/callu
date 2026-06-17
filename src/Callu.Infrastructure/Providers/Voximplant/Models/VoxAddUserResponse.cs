using System.Text.Json.Serialization;

namespace Callu.Infrastructure.Providers.Voximplant.Models;

public class VoxAddUserResponse : VoxBaseResponse
{
    [JsonPropertyName("user_id")]
    public long UserId { get; set; }
}
