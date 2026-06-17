using System.Text.Json;
using Callu.Application.Services;
using Callu.Infrastructure.Utilities;
using Callu.Shared.Constants;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Parses health check HTTP responses using JSON path field mappings.
/// Mirrors WebhookPayloadParser for health check responses.
/// 
/// StateMapping format:
/// {
///   "field": "$.status",
///   "operationalValue": "UP",
///   "degradedValue": "DEGRADED",
///   "partialOutageValue": "PARTIAL",
///   "majorOutageValue": "DOWN"
/// }
/// </summary>
public class HealthCheckResponseParser(ILogger<HealthCheckResponseParser> logger) : IHealthCheckResponseParser
{
    public HealthCheckParseResult Parse(string responseBody, int httpStatusCode, string? fieldMappings, string? stateMapping)
    {
        var result = new HealthCheckParseResult();

        if (string.IsNullOrEmpty(fieldMappings) && string.IsNullOrEmpty(stateMapping))
        {
            result.Success = true;
            result.Status = MapFromHttpStatus(httpStatusCode);
            result.Message = $"HTTP {httpStatusCode}";
            return result;
        }

        try
        {
            if (!string.IsNullOrEmpty(fieldMappings))
            {
                var mappings = JsonSerializer.Deserialize<Dictionary<string, string>>(fieldMappings);
                if (mappings != null)
                {
                    foreach (var (key, jsonPath) in mappings)
                    {
                        var value = JsonPathExtractor.ExtractValue(responseBody, jsonPath);
                        result.ExtractedFields[key] = value;
                    }

                    result.Message = result.ExtractedFields.GetValueOrDefault("message") 
                                  ?? result.ExtractedFields.GetValueOrDefault("status");
                }
            }

            if (!string.IsNullOrEmpty(stateMapping))
            {
                result.Status = DetermineStatusFromMapping(responseBody, stateMapping);
            }
            else
            {
                result.Status = MapFromHttpStatus(httpStatusCode);
            }

            result.Success = true;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "JSON parse error during health check response parsing");
            result.Success = false;
            result.Error = $"JSON parse error: {ex.Message}";
            result.Status = ComponentStatuses.MajorOutage;
        }

        return result;
    }

    private string DetermineStatusFromMapping(string responseBody, string stateMapping)
    {
        try
        {
            var config = JsonSerializer.Deserialize<Dictionary<string, string>>(stateMapping);
            if (config == null) return ComponentStatuses.Operational;

            var fieldPath = config.GetValueOrDefault("field", "$.status");
            var statusValue = JsonPathExtractor.ExtractValue(responseBody, fieldPath);

            if (string.IsNullOrEmpty(statusValue)) return ComponentStatuses.MajorOutage;

            var operationalValue = config.GetValueOrDefault("operationalValue", "UP");
            var degradedValue = config.GetValueOrDefault("degradedValue", "DEGRADED");
            var partialOutageValue = config.GetValueOrDefault("partialOutageValue", "PARTIAL");
            var majorOutageValue = config.GetValueOrDefault("majorOutageValue", "DOWN");
            var maintenanceValue = config.GetValueOrDefault("maintenanceValue", "MAINTENANCE");

            if (statusValue.Equals(operationalValue, StringComparison.OrdinalIgnoreCase))
                return ComponentStatuses.Operational;
            if (statusValue.Equals(degradedValue, StringComparison.OrdinalIgnoreCase))
                return ComponentStatuses.Degraded;
            if (statusValue.Equals(partialOutageValue, StringComparison.OrdinalIgnoreCase))
                return ComponentStatuses.PartialOutage;
            if (statusValue.Equals(majorOutageValue, StringComparison.OrdinalIgnoreCase))
                return ComponentStatuses.MajorOutage;
            if (statusValue.Equals(maintenanceValue, StringComparison.OrdinalIgnoreCase))
                return ComponentStatuses.Maintenance;

            return ComponentStatuses.Degraded;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse state mapping for health check response");
            return ComponentStatuses.MajorOutage;
        }
    }

    private static string MapFromHttpStatus(int httpStatusCode) => httpStatusCode switch
    {
        >= 200 and < 300 => ComponentStatuses.Operational,
        >= 300 and < 400 => ComponentStatuses.Operational,
        408 or 429 => ComponentStatuses.Degraded,
        >= 400 and < 500 => ComponentStatuses.PartialOutage,
        >= 500 => ComponentStatuses.MajorOutage,
        _ => ComponentStatuses.MajorOutage
    };
}
