using System.Text.Json.Serialization;

namespace Callu.Infrastructure.Providers.Voximplant.Models;

public class VoxGetCallHistoryResponse : VoxBaseResponse
{
    [JsonPropertyName("result")]
    public new List<VoxCallSessionInfoType>? Result { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("total_count")]
    public int TotalCount { get; set; }

    [JsonPropertyName("timezone")]
    public string? Timezone { get; set; }
}

public class VoxCallSessionInfoType
{
    [JsonPropertyName("call_session_history_id")]
    public long CallSessionHistoryId { get; set; }

    [JsonPropertyName("account_id")]
    public long AccountId { get; set; }

    [JsonPropertyName("application_id")]
    public long ApplicationId { get; set; }

    [JsonPropertyName("application_name")]
    public string? ApplicationName { get; set; }

    [JsonPropertyName("user_id")]
    public long UserId { get; set; }

    [JsonPropertyName("start_date")]
    public string? StartDate { get; set; }

    [JsonPropertyName("duration")]
    public int? Duration { get; set; }

    [JsonPropertyName("initiator_address")]
    public string? InitiatorAddress { get; set; }

    [JsonPropertyName("media_server_address")]
    public string? MediaServerAddress { get; set; }

    [JsonPropertyName("log_file_url")]
    public string? LogFileUrl { get; set; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }

    [JsonPropertyName("rule_name")]
    public string? RuleName { get; set; }

    [JsonPropertyName("custom_data")]
    public string? CustomData { get; set; }

    [JsonPropertyName("calls")]
    public List<VoxCallInfoType>? Calls { get; set; }
}

public class VoxCallInfoType
{
    [JsonPropertyName("call_id")]
    public long CallId { get; set; }

    [JsonPropertyName("local_number")]
    public string? LocalNumber { get; set; }

    [JsonPropertyName("remote_number")]
    public string? RemoteNumber { get; set; }

    [JsonPropertyName("start_time")]
    public string? StartTime { get; set; }

    [JsonPropertyName("duration")]
    public int? Duration { get; set; }

    [JsonPropertyName("cost")]
    public decimal? Cost { get; set; }

    [JsonPropertyName("call_type")]
    public string? CallType { get; set; }

    [JsonPropertyName("successful")]
    public bool? Successful { get; set; }

    [JsonPropertyName("transaction_id")]
    public long? TransactionId { get; set; }
}
