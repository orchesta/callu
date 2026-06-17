using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Shared.Models.Webhooks;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Manages webhook configuration — provider setup, token/key management, listening mode, templates.
/// Split from the original monolithic WebhookService for SRP.
/// </summary>
public class WebhookConfigService(
    IServiceRepository serviceRepo,
    IWebhookCaptureRepository captureRepo,
    IWebhookTemplateRepository templateRepo,
    ITransactionManager transactionManager) : IWebhookConfigService
{
    public async Task<ServiceWebhookSettingsDto> SetProviderAsync(Guid serviceId, string providerId, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var service = await serviceRepo.FindSingleAsync(s => s.Id == serviceId && !s.IsDeleted, cancellationToken);
            
            if (service == null)
                throw new ArgumentException("Service not found", nameof(serviceId));

            service.ProviderId = providerId;
            
            if (string.IsNullOrEmpty(service.WebhookToken))
            {
                service.WebhookToken = GenerateSecureToken();
            }

            return await GetWebhookSettingsInternalAsync(service, cancellationToken);
        }, cancellationToken);
    }

    public async Task<bool> DisableWebhookAsync(Guid serviceId, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var service = await serviceRepo.FindSingleAsync(s => s.Id == serviceId && !s.IsDeleted, cancellationToken);
            if (service == null) return false;

            service.ProviderId = null;
            service.WebhookListeningMode = false;
            
            return true;
        }, cancellationToken);
    }

    public async Task<string> RegenerateTokenAsync(Guid serviceId, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var service = await serviceRepo.FindSingleAsync(s => s.Id == serviceId && !s.IsDeleted, cancellationToken);
            
            if (service == null)
                throw new ArgumentException("Service not found", nameof(serviceId));

            service.WebhookToken = GenerateSecureToken();
            return service.WebhookToken;
        }, cancellationToken);
    }

    public async Task<string> RegenerateApiKeyAsync(Guid serviceId, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var service = await serviceRepo.FindSingleAsync(s => s.Id == serviceId && !s.IsDeleted, cancellationToken);
            
            if (service == null)
                throw new ArgumentException("Service not found", nameof(serviceId));

            service.WebhookApiKey = GenerateSecureToken(32);
            return service.WebhookApiKey;
        }, cancellationToken);
    }

    public async Task<bool> EnableListeningModeAsync(Guid serviceId, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var service = await serviceRepo.FindSingleAsync(s => s.Id == serviceId && !s.IsDeleted, cancellationToken);
            if (service == null) return false;

            service.WebhookListeningMode = true;
            
            if (!service.WebhookEnabled)
            {
                service.ProviderId = "callu";
                if (string.IsNullOrEmpty(service.WebhookToken))
                {
                    service.WebhookToken = GenerateSecureToken();
                }
            }
            
            return true;
        }, cancellationToken);
    }

    public async Task<bool> DisableListeningModeAsync(Guid serviceId, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var service = await serviceRepo.FindSingleAsync(s => s.Id == serviceId && !s.IsDeleted, cancellationToken);
            if (service == null) return false;

            service.WebhookListeningMode = false;
            return true;
        }, cancellationToken);
    }

    public async Task<bool> SetSignatureAsync(Guid serviceId, string secret, string? headerName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
            return false;

        var resolvedHeader = string.IsNullOrWhiteSpace(headerName)
            ? "X-Callu-Signature"
            : headerName.Trim();

        if (!System.Text.RegularExpressions.Regex.IsMatch(resolvedHeader, "^[A-Za-z][A-Za-z0-9-]*$"))
            return false;

        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Authorization", "X-Callu-Api-Key", "Cookie", "Set-Cookie"
        };
        if (reserved.Contains(resolvedHeader))
            return false;

        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var service = await serviceRepo.FindSingleAsync(s => s.Id == serviceId && !s.IsDeleted, cancellationToken);
            if (service == null) return false;

            service.WebhookSecret = secret;
            service.WebhookSignatureHeader = resolvedHeader;
            return true;
        }, cancellationToken);
    }

    public async Task<bool> ClearSignatureAsync(Guid serviceId, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var service = await serviceRepo.FindSingleAsync(s => s.Id == serviceId && !s.IsDeleted, cancellationToken);
            if (service == null) return false;

            service.WebhookSecret = null;
            service.WebhookSignatureHeader = null;
            return true;
        }, cancellationToken);
    }

    public async Task<bool> SetTemplateAsync(Guid serviceId, Guid? templateId, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var service = await serviceRepo.FindSingleAsync(s => s.Id == serviceId && !s.IsDeleted, cancellationToken);
            if (service == null) return false;

            if (templateId.HasValue)
            {
                var templateExists = await templateRepo.GetQueryable()
                    .AnyAsync(t => t.Id == templateId.Value && !t.IsDeleted, cancellationToken);
                
                if (!templateExists) return false;
            }

            service.WebhookTemplateId = templateId;
            return true;
        }, cancellationToken);
    }

    public async Task<ServiceWebhookSettingsDto?> GetWebhookSettingsAsync(Guid serviceId, CancellationToken cancellationToken = default)
    {
        var service = await serviceRepo.GetQueryable()
            .AsNoTracking()
            .Include(s => s.WebhookTemplate)
            .FirstOrDefaultAsync(s => s.Id == serviceId && !s.IsDeleted, cancellationToken);
        
        if (service == null) return null;

        return await GetWebhookSettingsInternalAsync(service, cancellationToken);
    }

    private async Task<ServiceWebhookSettingsDto> GetWebhookSettingsInternalAsync(
        Service service, 
        CancellationToken cancellationToken)
    {
        var captureCount = await captureRepo.GetQueryable()
            .CountAsync(c => c.ServiceId == service.Id && !c.IsDeleted, cancellationToken);

        var webhookUrl = !string.IsNullOrEmpty(service.WebhookToken)
            ? $"/api/v1/webhooks/{service.WebhookToken}"
            : null;

        return new ServiceWebhookSettingsDto
        {
            ServiceId = service.Id,
            ProviderId = service.ProviderId,
            WebhookEnabled = service.WebhookEnabled,
            WebhookToken = service.WebhookToken,
            WebhookUrl = webhookUrl,
            ApiKey = MaskApiKey(service.WebhookApiKey),
            HasApiKey = !string.IsNullOrEmpty(service.WebhookApiKey),
            ListeningMode = service.WebhookListeningMode,
            HasSignatureSecret = !string.IsNullOrEmpty(service.WebhookSecret),
            SignatureHeaderName = service.WebhookSignatureHeader,
            TemplateId = service.WebhookTemplateId,
            TemplateName = service.WebhookTemplate?.Name,
            LastWebhookReceivedAt = service.LastWebhookReceivedAt,
            WebhooksReceivedCount = service.WebhooksReceivedCount,
            CapturedCount = captureCount
        };
    }

    public async Task<IReadOnlyList<WebhookApiKeyOverviewDto>> ListWebhookApiKeysAsync(CancellationToken cancellationToken = default)
    {
        var rows = await serviceRepo.GetQueryable()
            .AsNoTracking()
            .Where(s => !s.IsDeleted && s.WebhookApiKey != null && s.WebhookApiKey != "")
            .OrderByDescending(s => s.UpdatedAt ?? s.CreatedAt)
            .Select(s => new
            {
                s.Id,
                s.Name,
                s.WebhookApiKey,
                s.WebhookSecret,
                s.ProviderId,
                s.UpdatedAt,
                s.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new WebhookApiKeyOverviewDto
        {
            ServiceId = r.Id,
            ServiceName = r.Name,
            HasApiKey = true,
            MaskedApiKey = MaskApiKey(r.WebhookApiKey),
            HasSignatureSecret = !string.IsNullOrEmpty(r.WebhookSecret),
            UpdatedAt = r.UpdatedAt ?? r.CreatedAt,
            WebhookEnabled = !string.IsNullOrEmpty(r.ProviderId)
        }).ToList();
    }

    private static string? MaskApiKey(string? key) =>
        string.IsNullOrEmpty(key) ? null : "****" + key[^Math.Min(4, key.Length)..];

    private static string GenerateSecureToken(int length = 24)
    {
        var bytes = new byte[length];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "")
            .Substring(0, length);
    }
}
