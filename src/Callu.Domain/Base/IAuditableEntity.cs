namespace Callu.Domain.Base;

/// <summary>Marker for entities whose audit fields are populated automatically by the auditing interceptor.</summary>
public interface IAuditableEntity
{
    DateTime CreatedAt { get; set; }
    DateTime? UpdatedAt { get; set; }
    string? CreatedBy { get; set; }
    string? UpdatedBy { get; set; }
}
