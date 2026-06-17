namespace Callu.Domain.Base;

/// <summary>
/// Marker for entities whose audit fields (CreatedAt, UpdatedAt, CreatedBy, UpdatedBy) are
/// populated automatically by <see cref="Callu.Infrastructure.Persistence.Interceptors.AuditableEntityInterceptor"/>.
/// Lets us share the audit contract between <see cref="BaseEntity"/> (domain) and
/// <see cref="Callu.Infrastructure.Identity.ApplicationUser"/> (Identity) without forcing
/// multiple-inheritance gymnastics — Identity's required <c>IdentityUser</c> base stays.
/// </summary>
public interface IAuditableEntity
{
    DateTime CreatedAt { get; set; }
    DateTime? UpdatedAt { get; set; }
    string? CreatedBy { get; set; }
    string? UpdatedBy { get; set; }
}
