using System.Text.Json;
using Callu.Application.Common.Interfaces;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Shared.Models.Runbooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Services;

public class RunbookService(
    IRepository<Runbook> repo,
    IAuditLogService auditLog,
    ICurrentUserService currentUser,
    ITransactionManager transactionManager,
    ILogger<RunbookService> logger) : IRunbookService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private const string EntityName = "Runbook";

    public async Task<List<RunbookDto>> GetAllAsync(CancellationToken ct = default)
    {
        var items = await repo.GetQueryable()
            .Where(r => !r.IsDeleted)
            .Include(r => r.Service)
            .OrderBy(r => r.Title)
            .ToListAsync(ct);
        return items.Select(MapToDto).ToList();
    }

    public async Task<RunbookDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var item = await repo.GetQueryable()
            .Include(r => r.Service)
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted, ct);
        return item == null ? null : MapToDto(item);
    }

    public async Task<List<RunbookDto>> GetByServiceAsync(Guid serviceId, CancellationToken ct = default)
    {
        var items = await repo.GetQueryable()
            .Where(r => !r.IsDeleted)
            .Include(r => r.Service)
            .Where(r => r.ServiceId == serviceId)
            .OrderBy(r => r.Title)
            .ToListAsync(ct);
        return items.Select(MapToDto).ToList();
    }

    public async Task<RunbookDto> CreateAsync(CreateRunbookRequest request, string authorId, CancellationToken ct = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var entity = new Runbook
            {
                Id = Guid.NewGuid(),
                Title = request.Title,
                Description = request.Description,
                Content = request.Content,
                ServiceId = request.ServiceId,
                AuthorId = authorId,
                TagsJson = JsonSerializer.Serialize(request.Tags, JsonOpts),
                CreatedAt = DateTime.UtcNow,
            };

            await repo.AddAsync(entity, ct);

            var dto = MapToDto(entity);
            await auditLog.LogAsync(
                authorId,
                "Created",
                EntityName,
                entity.Id.ToString(),
                oldValues: null,
                newValues: JsonSerializer.Serialize(dto, JsonOpts),
                ct);

            logger.LogInformation("Runbook created: {Title}", entity.Title);
            return dto;
        }, ct);
    }

    public async Task<bool> UpdateAsync(Guid id, UpdateRunbookRequest request, CancellationToken ct = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var entity = await repo.GetByIdAsync(id, ct);
            if (entity == null) return false;

            var oldValues = JsonSerializer.Serialize(MapToDto(entity), JsonOpts);

            entity.Title = request.Title;
            entity.Description = request.Description;
            entity.Content = request.Content;
            entity.ServiceId = request.ServiceId;
            entity.TagsJson = JsonSerializer.Serialize(request.Tags, JsonOpts);
            entity.UpdatedAt = DateTime.UtcNow;

            repo.Update(entity);

            await auditLog.LogAsync(
                currentUser.UserId,
                "Updated",
                EntityName,
                entity.Id.ToString(),
                oldValues,
                JsonSerializer.Serialize(MapToDto(entity), JsonOpts),
                ct);

            return true;
        }, ct);
    }

    public async Task<bool> MarkUsedAsync(Guid id, CancellationToken ct = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var entity = await repo.GetByIdAsync(id, ct);
            if (entity == null) return false;

            entity.UsageCount++;
            entity.LastUsedAt = DateTime.UtcNow;
            repo.Update(entity);

            await auditLog.LogAsync(
                currentUser.UserId,
                "MarkedUsed",
                EntityName,
                entity.Id.ToString(),
                oldValues: null,
                newValues: null,
                ct);

            return true;
        }, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var entity = await repo.GetByIdAsync(id, ct);
            if (entity == null) return false;

            entity.IsDeleted = true;
            entity.UpdatedAt = DateTime.UtcNow;
            repo.Update(entity);

            await auditLog.LogAsync(
                currentUser.UserId,
                "Deleted",
                EntityName,
                entity.Id.ToString(),
                oldValues: null,
                newValues: null,
                ct);

            return true;
        }, ct);
    }

    private static RunbookDto MapToDto(Runbook r)
    {
        List<string> tags = [];
        try { tags = JsonSerializer.Deserialize<List<string>>(r.TagsJson, JsonOpts) ?? []; }
        catch { }

        return new RunbookDto
        {
            Id = r.Id,
            Title = r.Title,
            Description = r.Description,
            Content = r.Content,
            ServiceId = r.ServiceId,
            ServiceName = r.Service?.Name,
            AuthorId = r.AuthorId,
            Tags = tags,
            LastUsedAt = r.LastUsedAt,
            UsageCount = r.UsageCount,
            CreatedAt = r.CreatedAt,
            UpdatedAt = r.UpdatedAt,
        };
    }
}
