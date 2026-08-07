using Callu.Shared.Models.Postmortems;

namespace Callu.Application.Services;

public interface IPostmortemService
{
    Task<List<PostmortemDto>> GetAllAsync(CancellationToken ct = default);
    Task<PostmortemDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<PostmortemDto>> GetByIncidentAsync(Guid incidentId, CancellationToken ct = default);
    Task<PostmortemDto> CreateAsync(CreatePostmortemRequest request, string authorId, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, UpdatePostmortemRequest request, CancellationToken ct = default);

    /// <summary>Draft → InReview transition.</summary>
    Task<bool> SubmitForReviewAsync(Guid id, CancellationToken ct = default);

    /// <summary>InReview → Draft transition (reviewer sends it back).</summary>
    Task<bool> RejectReviewAsync(Guid id, CancellationToken ct = default);

    /// <summary>Draft|InReview → Published. Stamps PublishedAt.</summary>
    Task<bool> PublishAsync(Guid id, CancellationToken ct = default);

    /// <summary>Published → Locked (final immutability).</summary>
    Task<bool> LockAsync(Guid id, CancellationToken ct = default);

    /// <summary>Soft-deletes only Draft postmortems; post-review rows must be retracted by an admin.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
