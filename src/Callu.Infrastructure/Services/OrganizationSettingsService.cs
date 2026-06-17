using Mapster;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Shared.Models.Settings;

namespace Callu.Infrastructure.Services;

public sealed class OrganizationSettingsService(
    IOrganizationSettingsRepository repo,
    ITransactionManager transactionManager,
    IConfiguration configuration,
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

            entity.OrganizationName = request.OrganizationName;
            entity.DefaultTimezone = request.DefaultTimezone;
            entity.DefaultCulture = request.DefaultCulture;
            entity.BaseUrl = NormalizeBaseUrl(request.BaseUrl);
            entity.EmailNotificationsEnabled = request.EmailNotificationsEnabled;
            entity.UpdatedAt = DateTime.UtcNow;

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
        return fromConfig ?? DefaultBaseUrl;
    }

    private static string? NormalizeBaseUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Trim().TrimEnd('/');
    }
}
