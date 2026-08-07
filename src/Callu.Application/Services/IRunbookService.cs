using Callu.Shared.Models.Runbooks;

namespace Callu.Application.Services;

public interface IRunbookService
{
    Task<List<RunbookDto>> GetAllAsync(CancellationToken ct = default);
    Task<RunbookDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<RunbookDto>> GetByServiceAsync(Guid serviceId, CancellationToken ct = default);
    Task<RunbookDto> CreateAsync(CreateRunbookRequest request, string authorId, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, UpdateRunbookRequest request, CancellationToken ct = default);
    Task<bool> MarkUsedAsync(Guid id, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
