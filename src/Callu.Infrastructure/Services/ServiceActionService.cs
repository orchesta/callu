using Callu.Application.Common.Interfaces;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Shared.Exceptions;
using Callu.Shared.Models.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Callu.Infrastructure.Services;

public class ServiceActionService(
    IRepository<ServiceAction> actions,
    IServiceRepository services,
    IUnitOfWork unitOfWork,
    ITransactionManager transactionManager,
    IAuditLogService auditLogService,
    ICurrentUserService currentUser,
    IOptions<Configuration.CommunicationSettingsOptions> communicationSettings,
    ILogger<ServiceActionService> logger) : IServiceActionService
{
    public async Task<List<ServiceActionDto>> GetForServiceAsync(Guid serviceId, bool includeSensitive, CancellationToken cancellationToken = default)
    {
        await EnsureServiceExistsAsync(serviceId, cancellationToken);

        var rows = await actions.GetQueryable()
            .AsNoTracking()
            .Where(a => a.ServiceId == serviceId && !a.IsDeleted)
            .OrderBy(a => a.DisplayOrder).ThenBy(a => a.Name)
            .Take(ServiceAction.MaxPerService)
            .ToListAsync(cancellationToken);

        // Headers can carry credentials; read-only roles get the action without them.
        return rows
            .Select(a => includeSensitive ? ToDto(a) : ToDto(a) with { HeadersJson = null })
            .ToList();
    }

    public async Task<ServiceActionDto> CreateAsync(Guid serviceId, CreateServiceActionRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureServiceExistsAsync(serviceId, cancellationToken);

        ServiceAckConfigurationGuard.EnsureValid(
            request.Url, request.PayloadTemplate, AllowPrivate, "Url", "PayloadTemplate");

        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var existing = await actions.GetQueryable()
                .Where(a => a.ServiceId == serviceId && !a.IsDeleted)
                .Select(a => a.Name)
                .ToListAsync(cancellationToken);

            if (existing.Count >= ServiceAction.MaxPerService)
                throw new BusinessRuleException($"A service can have at most {ServiceAction.MaxPerService} actions.");

            if (existing.Contains(request.Name, StringComparer.Ordinal))
                throw new ConflictException($"An action named '{request.Name}' already exists on this service.");

            var action = new ServiceAction
            {
                Id = Guid.NewGuid(),
                ServiceId = serviceId,
                Name = request.Name,
                Description = NullIfEmpty(request.Description),
                Url = request.Url,
                HttpMethod = request.HttpMethod.ToUpperInvariant(),
                ContentType = request.ContentType,
                HeadersJson = NullIfEmpty(request.HeadersJson),
                PayloadTemplate = NullIfEmpty(request.PayloadTemplate),
                Secret = NullIfEmpty(request.Secret),
                SignatureHeader = NullIfEmpty(request.SignatureHeader),
                IsEnabled = request.IsEnabled,
                DisplayOrder = request.DisplayOrder,
                CreatedAt = DateTime.UtcNow,
            };

            await actions.AddAsync(action, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            // Re-count inside the transaction, so two saves crossing the cap roll back.
            var total = await actions.GetQueryable()
                .CountAsync(a => a.ServiceId == serviceId && !a.IsDeleted, cancellationToken);
            if (total > ServiceAction.MaxPerService)
                throw new BusinessRuleException($"A service can have at most {ServiceAction.MaxPerService} actions.");

            await auditLogService.LogAsync(
                currentUser.UserId, AuditAction.Created, "ServiceAction", action.Id.ToString(),
                null, $"'{action.Name}' → {action.HttpMethod} {action.Url}",
                description: $"Action '{action.Name}' created on service {serviceId}",
                cancellationToken: cancellationToken);

            return ToDto(action);
        }, cancellationToken);
    }

    public async Task<ServiceActionDto> UpdateAsync(Guid serviceId, Guid actionId, UpdateServiceActionRequest request, CancellationToken cancellationToken = default)
    {
        var action = await FindAsync(serviceId, actionId, cancellationToken);

        ServiceAckConfigurationGuard.EnsureValid(
            request.Url, request.PayloadTemplate, AllowPrivate, "Url", "PayloadTemplate");

        if (request.Name is not null && !request.Name.Equals(action.Name, StringComparison.Ordinal))
        {
            var taken = await actions.GetQueryable()
                .AnyAsync(a => a.ServiceId == serviceId && a.Id != actionId && !a.IsDeleted && a.Name == request.Name, cancellationToken);
            if (taken)
                throw new ConflictException($"An action named '{request.Name}' already exists on this service.");
            action.Name = request.Name;
        }

        if (request.Description is not null) action.Description = NullIfEmpty(request.Description);
        if (request.Url is not null) action.Url = request.Url;
        if (request.HttpMethod is not null) action.HttpMethod = request.HttpMethod.ToUpperInvariant();
        if (request.ContentType is not null) action.ContentType = request.ContentType;
        if (request.HeadersJson is not null) action.HeadersJson = NullIfEmpty(request.HeadersJson);
        if (request.PayloadTemplate is not null) action.PayloadTemplate = NullIfEmpty(request.PayloadTemplate);
        if (request.Secret is not null) action.Secret = NullIfEmpty(request.Secret);
        if (request.SignatureHeader is not null) action.SignatureHeader = NullIfEmpty(request.SignatureHeader);
        if (request.IsEnabled.HasValue) action.IsEnabled = request.IsEnabled.Value;
        if (request.DisplayOrder.HasValue) action.DisplayOrder = request.DisplayOrder.Value;

        action.UpdatedAt = DateTime.UtcNow;
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditLogService.LogAsync(
            currentUser.UserId, AuditAction.Updated, "ServiceAction", action.Id.ToString(),
            null, $"'{action.Name}' → {action.HttpMethod} {action.Url}",
            description: $"Action '{action.Name}' updated",
            cancellationToken: cancellationToken);

        return ToDto(action);
    }

    public async Task DeleteAsync(Guid serviceId, Guid actionId, CancellationToken cancellationToken = default)
    {
        var action = await FindAsync(serviceId, actionId, cancellationToken);

        action.IsDeleted = true;
        action.UpdatedAt = DateTime.UtcNow;
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditLogService.LogAsync(
            currentUser.UserId, AuditAction.Deleted, "ServiceAction", action.Id.ToString(),
            $"'{action.Name}'", null,
            description: $"Action '{action.Name}' deleted",
            cancellationToken: cancellationToken);

        logger.LogInformation("Service action {ActionId} ('{Name}') deleted from service {ServiceId}", actionId, action.Name, serviceId);
    }

    private bool AllowPrivate => communicationSettings.Value.AllowPrivateWebhookEndpoint;

    private async Task EnsureServiceExistsAsync(Guid serviceId, CancellationToken cancellationToken)
    {
        var exists = await services.GetQueryable().AnyAsync(s => s.Id == serviceId && !s.IsDeleted, cancellationToken);
        if (!exists) throw new NotFoundException("Service", serviceId);
    }

    private async Task<ServiceAction> FindAsync(Guid serviceId, Guid actionId, CancellationToken cancellationToken)
    {
        var action = await actions.GetQueryable()
            .FirstOrDefaultAsync(a => a.Id == actionId && a.ServiceId == serviceId && !a.IsDeleted, cancellationToken);
        return action ?? throw new NotFoundException("ServiceAction", actionId);
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static ServiceActionDto ToDto(ServiceAction a) => new()
    {
        Id = a.Id,
        ServiceId = a.ServiceId,
        Name = a.Name,
        Description = a.Description,
        Url = a.Url,
        HttpMethod = a.HttpMethod,
        ContentType = a.ContentType,
        HeadersJson = a.HeadersJson,
        PayloadTemplate = a.PayloadTemplate,
        HasSecret = !string.IsNullOrEmpty(a.Secret),
        SignatureHeader = a.SignatureHeader,
        IsEnabled = a.IsEnabled,
        DisplayOrder = a.DisplayOrder,
        CreatedAt = a.CreatedAt,
    };
}
