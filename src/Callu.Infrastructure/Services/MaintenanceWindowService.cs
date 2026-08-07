using System.Text.Json;
using FluentValidation;
using Callu.Application.Common.Interfaces;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Shared.Exceptions;
using Callu.Shared.Models.Maintenance;
using Callu.Infrastructure.Persistence.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Maintenance window service — uses ITransactionManager for consistency with other services.
/// </summary>
public class MaintenanceWindowService(
    IRepository<MaintenanceWindow> repo,
    ITransactionManager transactionManager,
    IValidator<CreateMaintenanceWindowRequest> createValidator,
    IValidator<UpdateMaintenanceWindowRequest> updateValidator,
    IAuditLogService auditLogService,
    ICurrentUserService currentUser,
    ILogger<MaintenanceWindowService> logger) : IMaintenanceWindowService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<List<MaintenanceWindowDto>> GetAllAsync(CancellationToken ct = default)
    {
        var items = await repo.GetQueryable()
            .Where(m => !m.IsDeleted)
            .OrderByDescending(m => m.StartsAt)
            .ToListAsync(ct);
        return items.Select(MapToDto).ToList();
    }

    public async Task<List<MaintenanceWindowDto>> GetActiveAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var items = await repo.GetQueryable()
            .Where(m => !m.IsDeleted && !m.IsCancelled && m.StartsAt <= now && m.EndsAt >= now)
            .ToListAsync(ct);
        return items.Select(MapToDto).ToList();
    }

    public async Task<MaintenanceWindowDto> CreateAsync(CreateMaintenanceWindowRequest request, string userId, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var entity = new MaintenanceWindow
            {
                Id = Guid.NewGuid(),
                Title = request.Title,
                Description = request.Description,
                StartsAt = ToUtc(request.StartsAt),
                EndsAt = ToUtc(request.EndsAt),
                AffectedServiceIdsJson = JsonSerializer.Serialize(request.AffectedServiceIds, JsonOpts),
                AppliesToAllServices = request.AppliesToAllServices,
                Mode = Enum.TryParse<MaintenanceWindowMode>(request.Mode, out var mode) ? mode : MaintenanceWindowMode.SuppressAlerts,
                CreatedById = userId,
                CreatedAt = DateTime.UtcNow,
            };

            await repo.AddAsync(entity, ct);

            await auditLogService.LogAsync(
                userId, AuditAction.Created, "MaintenanceWindow", entity.Id.ToString(),
                newValues: $"title={entity.Title}; mode={entity.Mode}; {entity.StartsAt:O}..{entity.EndsAt:O}",
                description: "Maintenance window created",
                cancellationToken: ct);

            logger.LogInformation("Maintenance window created: {Title} ({Start} - {End})", entity.Title, entity.StartsAt, entity.EndsAt);
            return MapToDto(entity);
        }, ct);
    }

    public async Task<MaintenanceWindowDto?> UpdateAsync(Guid id, UpdateMaintenanceWindowRequest request, CancellationToken ct = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var entity = await repo.GetByIdAsync(id, ct);
            if (entity == null || entity.IsDeleted) return null;

            if (entity.IsCancelled)
                throw new ConflictException("Cancelled maintenance windows cannot be edited.");

            var now = DateTime.UtcNow;
            var requestedStart = ToUtc(request.StartsAt);

            entity.Title = request.Title;
            entity.Description = request.Description;
            entity.StartsAt = entity.StartsAt <= now ? entity.StartsAt : requestedStart;
            entity.EndsAt = ToUtc(request.EndsAt);
            entity.AppliesToAllServices = request.AppliesToAllServices;
            entity.AffectedServiceIdsJson = JsonSerializer.Serialize(
                request.AppliesToAllServices ? [] : request.AffectedServiceIds, JsonOpts);
            entity.Mode = Enum.TryParse<MaintenanceWindowMode>(request.Mode, out var mode)
                ? mode
                : MaintenanceWindowMode.SuppressAlerts;
            entity.UpdatedAt = now;
            repo.Update(entity);

            await auditLogService.LogAsync(
                currentUser.UserId, AuditAction.Updated, "MaintenanceWindow", entity.Id.ToString(),
                newValues: $"title={entity.Title}; mode={entity.Mode}; {entity.StartsAt:O}..{entity.EndsAt:O}",
                description: "Maintenance window updated",
                cancellationToken: ct);

            logger.LogInformation("Maintenance window updated: {Title} ({Start} - {End})", entity.Title, entity.StartsAt, entity.EndsAt);
            return MapToDto(entity);
        }, ct);
    }

    public async Task<bool> CancelAsync(Guid id, CancellationToken ct = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var entity = await repo.GetByIdAsync(id, ct);
            if (entity == null || entity.IsDeleted) return false;

            entity.IsCancelled = true;
            entity.UpdatedAt = DateTime.UtcNow;
            repo.Update(entity);

            await auditLogService.LogAsync(
                currentUser.UserId, AuditAction.Updated, "MaintenanceWindow", entity.Id.ToString(),
                oldValues: $"title={entity.Title}",
                description: "Maintenance window cancelled",
                cancellationToken: ct);

            logger.LogInformation("Maintenance window cancelled: {Title}", entity.Title);
            return true;
        }, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var entity = await repo.GetByIdAsync(id, ct);
            if (entity == null || entity.IsDeleted) return false;

            entity.IsDeleted = true;
            entity.UpdatedAt = DateTime.UtcNow;
            repo.Update(entity);

            await auditLogService.LogAsync(
                currentUser.UserId, AuditAction.Deleted, "MaintenanceWindow", entity.Id.ToString(),
                oldValues: $"title={entity.Title}",
                description: "Maintenance window deleted",
                cancellationToken: ct);

            return true;
        }, ct);
    }

    public async Task<bool> IsServiceInMaintenanceAsync(Guid serviceId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var activeWindows = await repo.GetQueryable()
            .Where(m => !m.IsDeleted && !m.IsCancelled && m.StartsAt <= now && m.EndsAt >= now)
            .ToListAsync(ct);

        return activeWindows.Any(m =>
        {
            if (m.AppliesToAllServices) return true;
            return ParseAffectedServiceIds(m).Contains(serviceId);
        });
    }

    public async Task<string?> GetMaintenanceModeForServiceAsync(Guid serviceId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var activeWindows = await repo.GetQueryable()
            .Where(m => !m.IsDeleted && !m.IsCancelled && m.StartsAt <= now && m.EndsAt >= now)
            .ToListAsync(ct);

        var matchingWindow = activeWindows.FirstOrDefault(m =>
        {
            if (m.AppliesToAllServices) return true;
            return ParseAffectedServiceIds(m).Contains(serviceId);
        });

        return matchingWindow?.Mode.ToString();
    }

    private static DateTime ToUtc(DateTime dt) => dt.Kind switch
    {
        DateTimeKind.Utc => dt,
        DateTimeKind.Local => dt.ToUniversalTime(),
        _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc),
    };

    private static List<Guid> ParseAffectedServiceIds(MaintenanceWindow m)
    {
        try { return JsonSerializer.Deserialize<List<Guid>>(m.AffectedServiceIdsJson, JsonOpts) ?? []; }
        catch { return []; }
    }

    private static MaintenanceWindowDto MapToDto(MaintenanceWindow m)
    {
        var now = DateTime.UtcNow;
        var serviceIds = ParseAffectedServiceIds(m);

        return new MaintenanceWindowDto
        {
            Id = m.Id,
            Title = m.Title,
            Description = m.Description,
            StartsAt = m.StartsAt,
            EndsAt = m.EndsAt,
            AffectedServiceIds = serviceIds,
            AppliesToAllServices = m.AppliesToAllServices,
            Mode = m.Mode.ToString(),
            CreatedById = m.CreatedById,
            IsCancelled = m.IsCancelled,
            IsActive = !m.IsCancelled && m.StartsAt <= now && m.EndsAt >= now,
            CreatedAt = m.CreatedAt,
        };
    }
}
