using System.Security.Cryptography;
using Callu.Application.Common.Interfaces;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Shared.Exceptions;
using Callu.Shared.Models.Integrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Services;

public class IntegrationService(
    IIntegrationRepository repo,
    IServiceRepository serviceRepo,
    IWebhookTemplateRepository templateRepo,
    IWebhookCaptureRepository captureRepo,
    IAuditLogService auditLog,
    ICurrentUserService currentUser,
    ITransactionManager transactionManager,
    ILogger<IntegrationService> logger) : IIntegrationService
{
    private const string EntityName = "Integration";

    public async Task<IReadOnlyList<IntegrationDto>> GetAllAsync(Guid? serviceId = null, CancellationToken cancellationToken = default)
    {
        var query = repo.GetQueryable()
            .AsNoTracking()
            .Include(i => i.Service)
            .Include(i => i.Team)
            .Include(i => i.WebhookTemplate)
            .Where(i => !i.IsDeleted);

        if (serviceId is { } sid)
            query = query.Where(i => i.ServiceId == sid);

        var items = await query
            .OrderBy(i => i.Name)
            .ThenBy(i => i.Id)
            .ToListAsync(cancellationToken);

        var captureCounts = await captureRepo.GetCountsByIntegrationAsync(
            items.Select(i => i.Id).ToList(), cancellationToken);
        return items.Select(i => MapToDto(i, captureCounts.GetValueOrDefault(i.Id))).ToList();
    }

    public async Task<IntegrationDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await repo.GetQueryable()
            .AsNoTracking()
            .Include(i => i.Service)
            .Include(i => i.Team)
            .Include(i => i.WebhookTemplate)
            .FirstOrDefaultAsync(i => i.Id == id && !i.IsDeleted, cancellationToken);

        if (item is null) return null;
        return MapToDto(item, await captureRepo.GetCountByIntegrationAsync(item.Id, cancellationToken));
    }

    public async Task<IntegrationSecretsDto> CreateAsync(CreateIntegrationRequest request, CancellationToken cancellationToken = default)
    {
        if (request.ServiceId is { } requestedServiceId)
            await EnsureServiceExistsAsync(requestedServiceId, cancellationToken);
        await EnsureTemplateExistsAsync(request.WebhookTemplateId, cancellationToken);

        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var entity = new Integration
            {
                Id = Guid.NewGuid(),
                Name = request.Name,
                Type = Enum.Parse<IntegrationType>(request.Type, ignoreCase: true),
                Description = request.Description,
                ServiceId = request.ServiceId,
                TeamId = request.TeamId,
                WebhookTemplateId = request.WebhookTemplateId,
                // An endpoint born without a service defaults to listening, so its first alarm is kept.
                ListeningMode = request.ListeningMode ?? !request.ServiceId.HasValue,
                Direction = IntegrationDirection.Inbound,
                Mode = IntegrationMode.WebhookOnly,
                IsActive = true,
                WebhookEnabled = true,
                WebhookToken = GenerateSecureToken(),
                ApiKey = GenerateSecureToken(32),
                WebhookSecret = string.IsNullOrEmpty(request.WebhookSecret) ? null : request.WebhookSecret,
                WebhookSignatureHeader = string.IsNullOrEmpty(request.WebhookSecret) ? null : request.WebhookSignatureHeader,
            };
            entity.InboundWebhookUrl = WebhookUrl(entity.WebhookToken);

            await repo.AddAsync(entity, cancellationToken);

            await auditLog.LogAsync(
                currentUser.UserId, AuditAction.Created, EntityName, entity.Id.ToString(),
                oldValues: null,
                newValues: $"name={entity.Name}; type={entity.Type}; service={entity.ServiceId}; template={entity.WebhookTemplateId}",
                description: $"Inbound integration '{entity.Name}' created",
                cancellationToken: cancellationToken);

            logger.LogInformation("Integration {IntegrationId} created for service {ServiceId}", entity.Id, entity.ServiceId);

            return new IntegrationSecretsDto
            {
                Id = entity.Id,
                WebhookUrl = entity.InboundWebhookUrl!,
                WebhookToken = entity.WebhookToken!,
                ApiKey = entity.ApiKey!,
            };
        }, cancellationToken);
    }

    public async Task<IntegrationDto> UpdateAsync(Guid id, UpdateIntegrationRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureTemplateExistsAsync(request.WebhookTemplateId, cancellationToken);

        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var entity = await repo.GetQueryable()
                .Include(i => i.Service)
                .Include(i => i.Team)
                .Include(i => i.WebhookTemplate)
                .FirstOrDefaultAsync(i => i.Id == id && !i.IsDeleted, cancellationToken)
                ?? throw new NotFoundException(EntityName, id);

            var before = $"name={entity.Name}; template={entity.WebhookTemplateId}; active={entity.IsActive}; webhook={entity.WebhookEnabled}; listening={entity.ListeningMode}";

            entity.Name = request.Name;
            entity.Description = request.Description;
            entity.TeamId = request.TeamId;
            entity.WebhookTemplateId = request.WebhookTemplateId;
            entity.IsActive = request.IsActive;
            entity.WebhookEnabled = request.WebhookEnabled;

            // Null means "leave listening alone", so a client built before this field cannot reset it.
            if (request.ListeningMode is { } listening)
                entity.ListeningMode = listening;

            // Null means "leave the secret alone"; an empty string is how the admin clears it.
            if (request.WebhookSecret is not null)
            {
                if (request.WebhookSecret.Length == 0)
                {
                    entity.WebhookSecret = null;
                    entity.WebhookSignatureHeader = null;
                }
                else
                {
                    entity.WebhookSecret = request.WebhookSecret;
                    entity.WebhookSignatureHeader = request.WebhookSignatureHeader;
                }
            }

            entity.UpdatedAt = DateTime.UtcNow;
            repo.Update(entity);

            await auditLog.LogAsync(
                currentUser.UserId, AuditAction.Updated, EntityName, entity.Id.ToString(),
                oldValues: before,
                newValues: $"name={entity.Name}; template={entity.WebhookTemplateId}; active={entity.IsActive}; webhook={entity.WebhookEnabled}; listening={entity.ListeningMode}",
                description: $"Inbound integration '{entity.Name}' updated",
                cancellationToken: cancellationToken);

            return MapToDto(entity);
        }, cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var entity = await repo.GetQueryable()
                .FirstOrDefaultAsync(i => i.Id == id && !i.IsDeleted, cancellationToken);
            if (entity is null) return false;

            // The token is what makes the endpoint reachable, and a soft-deleted row keeps its
            // columns — clear it so a deleted integration cannot keep ingesting.
            var name = entity.Name;
            entity.WebhookToken = null;
            entity.ApiKey = null;
            entity.WebhookEnabled = false;
            entity.IsDeleted = true;
            entity.UpdatedAt = DateTime.UtcNow;
            repo.Update(entity);

            await auditLog.LogAsync(
                currentUser.UserId, AuditAction.Deleted, EntityName, id.ToString(),
                oldValues: $"name={name}; service={entity.ServiceId}",
                newValues: null,
                description: $"Inbound integration '{name}' deleted",
                cancellationToken: cancellationToken);

            return true;
        }, cancellationToken);
    }

    public async Task<IntegrationSecretsDto> RotateCredentialsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var entity = await repo.GetQueryable()
                .FirstOrDefaultAsync(i => i.Id == id && !i.IsDeleted, cancellationToken)
                ?? throw new NotFoundException(EntityName, id);

            entity.WebhookToken = GenerateSecureToken();
            entity.ApiKey = GenerateSecureToken(32);
            entity.InboundWebhookUrl = WebhookUrl(entity.WebhookToken);
            entity.UpdatedAt = DateTime.UtcNow;
            repo.Update(entity);

            await auditLog.LogAsync(
                currentUser.UserId, AuditAction.Updated, EntityName, entity.Id.ToString(),
                oldValues: null,
                newValues: "credentials rotated",
                description: $"Inbound integration '{entity.Name}' token and API key rotated",
                cancellationToken: cancellationToken);

            logger.LogInformation("Integration {IntegrationId} credentials rotated", entity.Id);

            return new IntegrationSecretsDto
            {
                Id = entity.Id,
                WebhookUrl = entity.InboundWebhookUrl!,
                WebhookToken = entity.WebhookToken!,
                ApiKey = entity.ApiKey!,
            };
        }, cancellationToken);
    }

    public async Task<IntegrationDto> BindServiceAsync(Guid id, Guid? serviceId, CancellationToken cancellationToken = default)
    {
        if (serviceId is { } targetServiceId)
            await EnsureServiceExistsAsync(targetServiceId, cancellationToken);

        var bound = await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var entity = await repo.GetQueryable()
                .Include(i => i.Service)
                .Include(i => i.Team)
                .Include(i => i.WebhookTemplate)
                .FirstOrDefaultAsync(i => i.Id == id && !i.IsDeleted, cancellationToken)
                ?? throw new NotFoundException(EntityName, id);

            var before = $"service={entity.ServiceId}; listening={entity.ListeningMode}";
            entity.ServiceId = serviceId;
            entity.Service = null;
            // Binding means "start opening incidents", so listening ends with it; unbinding restarts it.
            entity.ListeningMode = serviceId is null;
            entity.UpdatedAt = DateTime.UtcNow;
            repo.Update(entity);

            await auditLog.LogAsync(
                currentUser.UserId, AuditAction.Updated, EntityName, entity.Id.ToString(),
                oldValues: before,
                newValues: $"service={serviceId}; listening={entity.ListeningMode}",
                description: serviceId is null
                    ? $"Inbound integration '{entity.Name}' unbound; it now captures instead of creating incidents"
                    : $"Inbound integration '{entity.Name}' bound to service {serviceId}",
                cancellationToken: cancellationToken);

            logger.LogInformation(
                "Integration {IntegrationId} service binding changed to {ServiceId}", entity.Id, serviceId);

            return entity.Id;
        }, cancellationToken);

        return await GetByIdAsync(bound, cancellationToken)
            ?? throw new NotFoundException(EntityName, bound);
    }

    private async Task EnsureServiceExistsAsync(Guid serviceId, CancellationToken cancellationToken)
    {
        var exists = await serviceRepo.GetQueryable()
            .AsNoTracking()
            .AnyAsync(s => s.Id == serviceId && !s.IsDeleted, cancellationToken);
        if (!exists)
            throw new NotFoundException("Service", serviceId);
    }

    private async Task EnsureTemplateExistsAsync(Guid? templateId, CancellationToken cancellationToken)
    {
        if (templateId is not { } id) return;

        var exists = await templateRepo.GetQueryable()
            .AsNoTracking()
            .AnyAsync(t => t.Id == id && !t.IsDeleted, cancellationToken);
        if (!exists)
            throw new NotFoundException("WebhookTemplate", id);
    }

    private static string WebhookUrl(string? token) => $"/api/v1/webhooks/{token}";

    private static IntegrationDto MapToDto(Integration i, int capturedCount = 0) => new()
    {
        Id = i.Id,
        Name = i.Name,
        Type = i.Type.ToString(),
        Description = i.Description,
        ServiceId = i.ServiceId,
        ServiceName = i.Service?.Name,
        TeamId = i.TeamId,
        TeamName = i.Team?.Name,
        WebhookTemplateId = i.WebhookTemplateId,
        WebhookTemplateName = i.WebhookTemplate?.Name,
        IsActive = i.IsActive,
        WebhookEnabled = i.WebhookEnabled,
        ListeningMode = i.ListeningMode,
        CapturedCount = capturedCount,
        HasToken = !string.IsNullOrEmpty(i.WebhookToken),
        WebhookUrl = string.IsNullOrEmpty(i.WebhookToken) ? null : WebhookUrl(i.WebhookToken),
        HasApiKey = !string.IsNullOrEmpty(i.ApiKey),
        MaskedApiKey = Mask(i.ApiKey),
        HasSignatureSecret = !string.IsNullOrEmpty(i.WebhookSecret),
        SignatureHeaderName = i.WebhookSignatureHeader,
        LastWebhookReceivedAt = i.LastWebhookReceivedAt,
        WebhooksReceivedCount = i.WebhooksReceivedCount,
        CreatedAt = i.CreatedAt,
        UpdatedAt = i.UpdatedAt,
    };

    private static string? Mask(string? key) =>
        string.IsNullOrEmpty(key) ? null : "****" + key[^Math.Min(4, key.Length)..];

    private static string GenerateSecureToken(int length = 24)
    {
        var bytes = new byte[length];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "")[..length];
    }
}
