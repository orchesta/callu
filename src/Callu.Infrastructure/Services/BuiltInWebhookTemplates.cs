using System.Text.Json;
using Callu.Infrastructure.Services.Models;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Provides built-in webhook templates for common monitoring systems
/// </summary>
public static class BuiltInWebhookTemplates
{
    /// <summary>
    /// Gets all built-in templates
    /// </summary>
    public static IEnumerable<BuiltInTemplate> GetAll()
    {
        yield return Prometheus();
        yield return Grafana();
        yield return Generic();
    }

    /// <summary>
    /// Prometheus Alertmanager template
    /// </summary>
    public static BuiltInTemplate Prometheus() => new()
    {
        Name = "Prometheus Alertmanager",
        Description = "Template for Prometheus Alertmanager webhooks",
        FieldMappings = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["title"] = "$.alerts[0].labels.alertname",
            ["description"] = "$.alerts[0].annotations.description",
            ["severity"] = "$.alerts[0].labels.severity",
            ["externalId"] = "$.alerts[0].fingerprint"
        }),
        StateMapping = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["field"] = "$.alerts[0].status",
            ["openValue"] = "firing",
            ["resolvedValue"] = "resolved"
        }),
        SamplePayload = """
{
  "status": "firing",
  "alerts": [
    {
      "status": "firing",
      "labels": {
        "alertname": "HighMemoryUsage",
        "instance": "server-01:9100",
        "severity": "warning"
      },
      "annotations": {
        "description": "Memory usage is above 90%",
        "summary": "High memory usage detected"
      },
      "startsAt": "2024-01-15T10:00:00.000Z",
      "fingerprint": "abc123def456"
    }
  ]
}
"""
    };

    /// <summary>
    /// Grafana Alerting template
    /// </summary>
    public static BuiltInTemplate Grafana() => new()
    {
        Name = "Grafana Alerting",
        Description = "Template for Grafana unified alerting webhooks",
        FieldMappings = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["title"] = "$.title",
            ["description"] = "$.message",
            ["severity"] = "$.alerts[0].labels.severity",
            ["externalId"] = "$.alerts[0].fingerprint"
        }),
        StateMapping = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["field"] = "$.status",
            ["openValue"] = "firing",
            ["resolvedValue"] = "resolved"
        }),
        SamplePayload = """
{
  "status": "firing",
  "title": "CPU Usage Alert",
  "message": "CPU usage on production server exceeded threshold",
  "alerts": [
    {
      "status": "firing",
      "labels": {
        "alertname": "HighCPU",
        "severity": "critical"
      },
      "fingerprint": "grafana123"
    }
  ]
}
"""
    };

    /// <summary>
    /// Generic JSON webhook template
    /// </summary>
    public static BuiltInTemplate Generic() => new()
    {
        Name = "Generic JSON",
        Description = "Generic template for simple JSON webhooks with title and description",
        FieldMappings = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["title"] = "$.title",
            ["description"] = "$.description",
            ["severity"] = "$.severity",
            ["externalId"] = "$.id"
        }),
        StateMapping = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["field"] = "$.status",
            ["openValue"] = "open",
            ["resolvedValue"] = "resolved"
        }),
        SamplePayload = """
{
  "id": "incident-001",
  "title": "Service Degradation",
  "description": "API response times have increased significantly",
  "severity": "high",
  "status": "open"
}
"""
    };
}

