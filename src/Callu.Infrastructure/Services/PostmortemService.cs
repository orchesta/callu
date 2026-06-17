using System.Text.Json;
using Callu.Application.Common.Interfaces;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Shared.Models.Postmortems;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Services;

public class PostmortemService(
    IRepository<Postmortem> repo,
    IIncidentRepository incidentRepo,
    IAuditLogService auditLog,
    ICurrentUserService currentUser,
    ITransactionManager transactionManager,
    ILogger<PostmortemService> logger) : IPostmortemService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private const string EntityName = "Postmortem";

    public async Task<List<PostmortemDto>> GetAllAsync(CancellationToken ct = default)
    {
        var items = await repo.GetQueryable()
            .Include(p => p.Incident)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);
        return items.Select(MapToDto).ToList();
    }

    public async Task<PostmortemDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var item = await repo.GetQueryable()
            .Include(p => p.Incident)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        return item == null ? null : MapToDto(item);
    }

    public async Task<List<PostmortemDto>> GetByIncidentAsync(Guid incidentId, CancellationToken ct = default)
    {
        var items = await repo.GetQueryable()
            .Include(p => p.Incident)
            .Where(p => p.IncidentId == incidentId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);
        return items.Select(MapToDto).ToList();
    }

    public async Task<PostmortemDto> CreateAsync(CreatePostmortemRequest request, string authorId, CancellationToken ct = default)
    {
        var incidentExists = await incidentRepo.GetQueryable()
            .AsNoTracking()
            .AnyAsync(i => i.Id == request.IncidentId && !i.IsDeleted, ct);
        if (!incidentExists)
            throw new Callu.Shared.Exceptions.NotFoundException("Incident", request.IncidentId);

        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var entity = new Postmortem
            {
                Id = Guid.NewGuid(),
                Title = request.Title,
                Content = request.Content,
                RootCause = request.RootCause,
                IncidentId = request.IncidentId,
                AuthorId = authorId,
                ActionItemsJson = JsonSerializer.Serialize(request.ActionItems, JsonOpts),
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

            logger.LogInformation("Postmortem created: {Title} for incident {IncidentId}", entity.Title, entity.IncidentId);
            return dto;
        }, ct);
    }

    public async Task<bool> UpdateAsync(Guid id, UpdatePostmortemRequest request, CancellationToken ct = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var entity = await repo.GetByIdAsync(id, ct);
            if (entity == null) return false;

            var oldValues = JsonSerializer.Serialize(MapToDto(entity), JsonOpts);

            if (entity.CanEditAllFields)
            {
                entity.Title = request.Title;
                entity.Content = request.Content;
                entity.RootCause = request.RootCause;
                entity.ActionItemsJson = JsonSerializer.Serialize(request.ActionItems, JsonOpts);
            }
            else if (entity.CanEditActionItems)
            {
                entity.ActionItemsJson = JsonSerializer.Serialize(request.ActionItems, JsonOpts);
            }
            else
            {
                throw new InvalidOperationException($"Postmortem is locked and cannot be modified. Status: '{entity.Status}'.");
            }
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

    public async Task<bool> SubmitForReviewAsync(Guid id, CancellationToken ct = default)
        => await ExecuteStateTransitionAsync(id, "Submitted", e => e.Submit(), ct);

    public async Task<bool> RejectReviewAsync(Guid id, CancellationToken ct = default)
        => await ExecuteStateTransitionAsync(id, "Rejected", e => e.Reject(), ct);

    public async Task<bool> PublishAsync(Guid id, CancellationToken ct = default)
        => await ExecuteStateTransitionAsync(id, "Published", e => e.Publish(), ct);

    public async Task<bool> LockAsync(Guid id, CancellationToken ct = default)
        => await ExecuteStateTransitionAsync(id, "Locked", e => e.Lock(), ct);

    private async Task<bool> ExecuteStateTransitionAsync(Guid id, string action, Action<Postmortem> transition, CancellationToken ct)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var entity = await repo.GetByIdAsync(id, ct);
            if (entity == null) return false;

            var fromStatus = entity.Status.ToString();
            transition(entity);
            repo.Update(entity);

            await auditLog.LogAsync(
                currentUser.UserId,
                action,
                EntityName,
                entity.Id.ToString(),
                oldValues: fromStatus,
                newValues: entity.Status.ToString(),
                ct);

            logger.LogInformation("Postmortem {Action}: {Title} ({From} → {To})",
                action, entity.Title, fromStatus, entity.Status);
            return true;
        }, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var entity = await repo.GetByIdAsync(id, ct);
            if (entity == null) return false;

            if (entity.Status != PostmortemStatus.Draft)
                throw new InvalidOperationException($"Only Draft postmortems can be deleted. Current: '{entity.Status}'.");

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

    private static PostmortemDto MapToDto(Postmortem p)
    {
        List<PostmortemActionItemDto> actions = [];
        try { actions = JsonSerializer.Deserialize<List<PostmortemActionItemDto>>(p.ActionItemsJson, JsonOpts) ?? []; }
        catch { }

        return new PostmortemDto
        {
            Id = p.Id,
            Title = p.Title,
            Content = p.Content,
            RootCause = p.RootCause,
            IncidentId = p.IncidentId,
            IncidentTitle = p.Incident?.Title,
            AuthorId = p.AuthorId,
            Status = p.Status.ToString(),
            PublishedAt = p.PublishedAt,
            ActionItems = actions,
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt,
        };
    }
}
