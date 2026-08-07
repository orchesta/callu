using System.Text.Json;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Services.Models;
using Callu.Infrastructure.Utilities;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Template-based webhook payload parser
/// Converts incoming webhooks to incident data using configured templates
/// </summary>
public interface IWebhookPayloadParser
{
    /// <summary>
    /// Parses a webhook payload using the specified template
    /// </summary>
    ParsedWebhookPayload Parse(string payload, WebhookTemplate template);
    
    /// <summary>
    /// Validates a template against a sample payload
    /// </summary>
    TemplateValidationResult Validate(string samplePayload, WebhookTemplate template);
}

public class WebhookPayloadParser : IWebhookPayloadParser
{
    public ParsedWebhookPayload Parse(string payload, WebhookTemplate template)
    {
        var result = new ParsedWebhookPayload();

        if (string.IsNullOrEmpty(payload) || string.IsNullOrEmpty(template.FieldMappings))
        {
            result.Success = false;
            result.Error = "Invalid payload or template";
            return result;
        }

        try
        {
            var fieldMappings = JsonSerializer.Deserialize<Dictionary<string, object>>(template.FieldMappings) 
                ?? new Dictionary<string, object>();

            var titlePath = fieldMappings.GetValueOrDefault("title")?.ToString() ?? "";
            result.Title = JsonPathExtractor.ExtractValue(payload, titlePath);
            if (string.IsNullOrEmpty(result.Title))
            {
                result.Success = false;
                result.Error = "Could not extract title from payload";
                return result;
            }

            result.Description = JsonPathExtractor.ExtractValue(payload, fieldMappings.GetValueOrDefault("description")?.ToString() ?? "");
            result.ExternalId = JsonPathExtractor.ExtractValue(payload, fieldMappings.GetValueOrDefault("externalId")?.ToString() ?? "");

            var severityPath = fieldMappings.GetValueOrDefault("severity")?.ToString() ?? "";
            var severityValue = JsonPathExtractor.ExtractValue(payload, severityPath);
            result.Severity = MapSeverity(severityValue, template);

            result.State = DetermineState(payload, template);

            result.Success = true;
        }
        catch (JsonException ex)
        {
            result.Success = false;
            result.Error = ex.Message;
        }

        return result;
    }

    public TemplateValidationResult Validate(string samplePayload, WebhookTemplate template)
    {
        var result = new TemplateValidationResult();

        try
        {
            var parseResult = Parse(samplePayload, template);
            result.IsValid = parseResult.Success;
            
            if (parseResult.Success)
            {
                result.ExtractedFields = new Dictionary<string, string?>
                {
                    ["title"] = parseResult.Title,
                    ["description"] = parseResult.Description,
                    ["severity"] = parseResult.Severity.ToString(),
                    ["state"] = parseResult.State.ToString(),
                    ["externalId"] = parseResult.ExternalId
                };
            }
            else
            {
                result.Errors.Add(parseResult.Error ?? "Unknown error");
            }
        }
        catch (JsonException ex)
        {
            result.IsValid = false;
            result.Errors.Add(ex.Message);
        }

        return result;
    }

    private IncidentSeverity MapSeverity(string? severityValue, WebhookTemplate template)
    {
        if (string.IsNullOrEmpty(severityValue))
            return IncidentSeverity.Medium;

        if (!string.IsNullOrEmpty(template.StateMapping))
        {
            try
            {
                using var doc = JsonDocument.Parse(template.StateMapping);
                if (doc.RootElement.TryGetProperty("severityMappings", out var mappingsElement)
                    && mappingsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var mapping in mappingsElement.EnumerateArray())
                    {
                        var source = mapping.TryGetProperty("sourceValue", out var sv) ? sv.GetString() : null;
                        var target = mapping.TryGetProperty("targetSeverity", out var ts) ? ts.GetString() : null;

                        if (source != null && severityValue.Equals(source, StringComparison.OrdinalIgnoreCase))
                        {
                            if (Enum.TryParse<IncidentSeverity>(target, true, out var severity))
                            {
                                return severity;
                            }
                        }
                    }
                }
            }
            catch
            {
            }
        }

        return severityValue.ToLowerInvariant() switch
        {
            "critical" or "p1" or "sev1" or "emergency" => IncidentSeverity.Critical,
            "high" or "p2" or "sev2" or "major" or "error" => IncidentSeverity.High,
            "medium" or "p3" or "sev3" or "moderate" => IncidentSeverity.Medium,
            "low" or "p4" or "sev4" or "minor" => IncidentSeverity.Low,
            "info" or "informational" or "p5" or "sev5" => IncidentSeverity.Low,
            _ => IncidentSeverity.Medium
        };
    }

    private WebhookState DetermineState(string payload, WebhookTemplate template)
    {
        if (string.IsNullOrEmpty(template.StateMapping))
            return WebhookState.Open;

        try
        {
            var stateConfig = JsonSerializer.Deserialize<Dictionary<string, string>>(template.StateMapping);
            if (stateConfig == null)
                return WebhookState.Open;

            var fieldPath = stateConfig.GetValueOrDefault("stateField", stateConfig.GetValueOrDefault("field", ""));
            var openValue = stateConfig.GetValueOrDefault("openValue", "firing");
            var resolvedValue = stateConfig.GetValueOrDefault("resolvedValue", "resolved");

            var stateValue = JsonPathExtractor.ExtractValue(payload, fieldPath);

            if (string.IsNullOrEmpty(stateValue))
                return WebhookState.Open;

            if (stateValue.Equals(resolvedValue, StringComparison.OrdinalIgnoreCase))
                return WebhookState.Resolved;

            return WebhookState.Open;
        }
        catch
        {
            return WebhookState.Open;
        }
    }
}

