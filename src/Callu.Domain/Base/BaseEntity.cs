using System.ComponentModel.DataAnnotations;

namespace Callu.Domain.Base;

/// <summary>
/// Base entity class with audit fields and soft delete support
/// </summary>
public abstract class BaseEntity : IAuditableEntity, ISoftDeletable
{
    [Key]
    public Guid Id { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    [StringLength(128)]
    public string? CreatedBy { get; set; }

    [StringLength(128)]
    public string? UpdatedBy { get; set; }

    public bool IsDeleted { get; set; } = false;

    [Timestamp]
    public uint RowVersion { get; set; }
}
