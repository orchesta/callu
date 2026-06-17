namespace Callu.Infrastructure.Services.Models;

/// <summary>
/// Built-in template definition
/// </summary>
public class BuiltInTemplate
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string FieldMappings { get; set; } = "{}";
    public string? StateMapping { get; set; }
    public string? SamplePayload { get; set; }
}
