using Mapster;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Shared.Models.Settings;
using Callu.Domain.Enums;

namespace Callu.Infrastructure.Services;

public sealed class OrganizationSettingsService(
    IOrganizationSettingsRepository repo,
    ITransactionManager transactionManager,
    IConfiguration configuration,
    IAuditLogService auditLogService,
    ILogger<OrganizationSettingsService> logger) : IOrganizationSettingsService
{
    private const string DefaultBaseUrl = "http://localhost:3000";

    public async Task<OrganizationSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var entity = await repo.GetSettingsAsync(cancellationToken);
        return entity is null
            ? new OrganizationSettingsDto()
            : entity.Adapt<OrganizationSettingsDto>();
    }

    public async Task<OrganizationSettingsDto> SaveSettingsAsync(UpdateOrganizationSettingsRequest request, CancellationToken cancellationToken = default)
    {
        var saved = await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var entity = await repo.GetSettingsAsync(cancellationToken);

            if (entity is null)
            {
                entity = new OrganizationSettings
                {
                    Id = OrganizationSettings.SingletonId,
                    CreatedAt = DateTime.UtcNow
                };
                await repo.AddAsync(entity, cancellationToken);
            }

            var before = $"name={entity.OrganizationName}; tz={entity.DefaultTimezone}; "
                + $"culture={entity.DefaultCulture}; baseUrl={entity.BaseUrl}; email={entity.EmailNotificationsEnabled}";

            entity.OrganizationName = request.OrganizationName;
            entity.DefaultTimezone = request.DefaultTimezone;
            entity.DefaultCulture = request.DefaultCulture;
            entity.BaseUrl = NormalizeBaseUrl(request.BaseUrl);
            entity.EmailNotificationsEnabled = request.EmailNotificationsEnabled;
            entity.UpdatedAt = DateTime.UtcNow;

            await auditLogService.LogAsync(
                null, AuditAction.SettingsChanged, "OrganizationSettings", entity.Id.ToString(),
                oldValues: before,
                newValues: $"name={entity.OrganizationName}; tz={entity.DefaultTimezone}; "
                    + $"culture={entity.DefaultCulture}; baseUrl={entity.BaseUrl}; email={entity.EmailNotificationsEnabled}",
                cancellationToken: cancellationToken);

            logger.LogInformation("Organization settings saved (baseUrl={BaseUrl})", entity.BaseUrl ?? "<unset>");
            return entity;
        }, cancellationToken);

        return saved.Adapt<OrganizationSettingsDto>();
    }

    public async Task<string> GetPublicBaseUrlAsync(CancellationToken cancellationToken = default)
    {
        var settings = await GetSettingsAsync(cancellationToken);
        var fromDb = NormalizeBaseUrl(settings.BaseUrl);
        if (!string.IsNullOrWhiteSpace(fromDb))
            return fromDb!;

        var fromConfig =
            NormalizeBaseUrl(configuration["CalluSettings:ApiUrl"]) ??
            NormalizeBaseUrl(configuration["CalluSettings:FrontendUrl"]);
        if (fromConfig is not null) return fromConfig;

        // Falling back here means every link we send points at the operator's own machine. Said once
        // per process so it is findable without drowning the log on a busy escalation.
        if (!Interlocked.Exchange(ref _warnedAboutDefaultBaseUrl, 1).Equals(1))
        {
            logger.LogWarning(
                "No public base URL is configured, so notification links will point at {DefaultBaseUrl} "
                + "and will not open for anyone reaching this install remotely. Set it under Settings.",
                DefaultBaseUrl);
        }

        return DefaultBaseUrl;
    }

    private static int _warnedAboutDefaultBaseUrl;

    private static string? NormalizeBaseUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Trim().TrimEnd('/');
    }
}
