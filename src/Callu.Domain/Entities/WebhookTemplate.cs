using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;

namespace Callu.Domain.Entities;

/// <summary>
/// Stores webhook field mapping configuration for parsing external payloads
/// </summary>
public class WebhookTemplate : BaseEntity
{
    /// <summary>
    /// Template name (e.g., "Prometheus AlertManager", "Grafana", "Custom")
    /// </summary>
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Template description
    /// </summary>
    [StringLength(500)]
    public string? Description { get; set; }
    
    /// <summary>
    /// Field mappings configuration as JSON
    /// Maps JSON paths to Callu fields (title, description, severity, etc.)
    /// </summary>
    public string FieldMappings { get; set; } = "{}";
    
    /// <summary>
    /// State mapping configuration as JSON
    /// Defines how to determine incident status (open/resolved)
    /// </summary>
    public string? StateMapping { get; set; }
    
    /// <summary>
    /// Sample payload for reference/testing
    /// </summary>
    public string? SamplePayload { get; set; }
    
    /// <summary>
    /// Is this a built-in system template
    /// </summary>
    public bool IsBuiltIn { get; set; } = false;
    
    /// <summary>
    /// Language code for TTS pronunciation of dynamic incident data (e.g. en-US)
    /// </summary>
    [StringLength(10)]
    public string DataLanguage { get; set; } = "en-US";
    
    /// <summary>
    /// Is this template currently active
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// Services using this template
    /// </summary>
    public virtual ICollection<Service> Services { get; set; } = new List<Service>();
}
