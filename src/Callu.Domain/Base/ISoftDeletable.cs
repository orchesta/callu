namespace Callu.Domain.Base;

/// <summary>
/// Marker for entities subject to soft-delete. The global query filter
/// configured in ApplicationDbContext excludes rows where IsDeleted = true.
/// </summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
}
