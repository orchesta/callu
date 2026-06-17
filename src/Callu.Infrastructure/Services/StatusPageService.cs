using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Mapster;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Messaging;
using Callu.Application.Services;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Domain.Entities;
using Callu.Shared.Models.StatusPages;
using System.Security.Cryptography;
using System.Text;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Status Page service implementation — page CRUD, incident management, stats, analytics/subscribers.
/// Component management is in StatusPageComponentService.
/// </summary>
public class StatusPageService(
    IStatusPageRepository statusPageRepo,
    IRepository<StatusPageComponent> componentRepo,
    IRepository<StatusPageIncident> incidentRepo,
    IRepository<StatusPageIncidentUpdate> incidentUpdateRepo,
    IRepository<StatusPageView> viewRepo,
    IRepository<StatusPageSubscriber> subscriberRepo,
    ITransactionManager transactionManager,
    IEmailService emailService,
    IOrganizationSettingsService organizationSettingsService,
    HybridCache cache,
    IStatusPageSubscriberNotifier subscriberNotifier,
    ILogger<StatusPageService> logger) : IStatusPageService
{
    private static readonly HybridCacheEntryOptions UptimeCacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(2),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    public async Task<IEnumerable<StatusPageDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var pages = await statusPageRepo.FindAsync(p => !p.IsDeleted, cancellationToken);
        return pages
            .OrderBy(p => p.Name)
            .Select(p => p.Adapt<StatusPageDto>());
    }

    public async Task<StatusPageDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var page = await statusPageRepo.GetDetailByIdAsync(id, cancellationToken);
        return page != null ? MapToDetailDto(page) : null;
    }

    public async Task<StatusPageDetailDto?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var page = await statusPageRepo.GetDetailBySlugAsync(slug, cancellationToken);
        return page != null ? MapToDetailDto(page) : null;
    }

    public async Task<PublicStatusPageDto?> GetBySlugPublicAsync(string slug, CancellationToken cancellationToken = default)
    {
        var page = await statusPageRepo.GetDetailBySlugPublicAsync(slug, cancellationToken);
        return page != null ? MapToPublicDetailDto(page) : null;
    }

    public async Task<StatusPageDto> CreateAsync(CreateStatusPageRequest request, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var existing = await statusPageRepo.GetBySlugAsync(request.Slug, cancellationToken);
            if (existing != null)
            {
                throw new InvalidOperationException($"Status page with slug '{request.Slug}' already exists.");
            }

            var page = new StatusPage
            {
                Name = request.Name,
                Slug = request.Slug,
                Description = request.Description,
                IsPublic = true,
                OverallStatus = "operational"
            };

            await statusPageRepo.AddAsync(page, cancellationToken);
            return page.Adapt<StatusPageDto>();
        }, cancellationToken);
    }

    public async Task<bool> UpdateAsync(Guid id, UpdateStatusPageRequest request, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var page = await statusPageRepo.FindSingleAsync(p => p.Id == id && !p.IsDeleted, cancellationToken);
            if (page == null) return false;

            if (request.Name != null) page.Name = request.Name;
            if (request.Description != null) page.Description = request.Description;
            if (request.IsPublic.HasValue) page.IsPublic = request.IsPublic.Value;
            if (request.SupportEmail != null) page.SupportEmail = request.SupportEmail == "" ? null : request.SupportEmail;
            if (request.AllowSubscriptions.HasValue) page.AllowSubscriptions = request.AllowSubscriptions.Value;

            if (request.Slug != null && request.Slug != page.Slug)
            {
                var existing = await statusPageRepo.GetBySlugAsync(request.Slug, cancellationToken);
                if (existing != null)
                {
                    throw new InvalidOperationException($"Status page with slug '{request.Slug}' already exists.");
                }
                page.Slug = request.Slug;
            }

            return true;
        }, cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var page = await statusPageRepo.GetQueryable()
                .Include(p => p.Components)
                .Include(p => p.Incidents)
                .Include(p => p.Subscribers)
                .Where(p => !p.IsDeleted)
                .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
            if (page == null) return false;

            var now = DateTime.UtcNow;
            page.IsDeleted = true;
            page.UpdatedAt = now;

            foreach (var component in page.Components)
            {
                component.IsDeleted = true;
                component.UpdatedAt = now;
            }
            foreach (var incident in page.Incidents)
            {
                incident.IsDeleted = true;
                incident.UpdatedAt = now;
            }
            foreach (var subscriber in page.Subscribers)
            {
                subscriber.IsDeleted = true;
                subscriber.UpdatedAt = now;
            }
            return true;
        }, cancellationToken);
    }

    public async Task<StatusPageIncidentDto?> CreateIncidentAsync(Guid pageId, CreateStatusIncidentRequest request, CancellationToken cancellationToken = default)
    {
        var page = await statusPageRepo.FindSingleAsync(p => p.Id == pageId && !p.IsDeleted, cancellationToken);
        if (page == null) return null;

        var dto = await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var incident = new StatusPageIncident
            {
                Title = request.Title,
                Status = request.Status,
                Impact = request.Impact ?? "minor",
                StatusPageId = pageId
            };

            await incidentRepo.AddAsync(incident, cancellationToken);
            return incident.Adapt<StatusPageIncidentDto>();
        }, cancellationToken);

        if (dto != null)
            await InvalidateUptimeCacheAsync(pageId, cancellationToken);

        return dto;
    }

    public async Task<bool> TriggerSubscriberNotificationAsync(Guid incidentId, CancellationToken cancellationToken = default)
    {
        var incident = await incidentRepo.FindSingleAsync(i => i.Id == incidentId && !i.IsDeleted, cancellationToken);
        if (incident == null) return false;

        var page = await statusPageRepo.FindSingleAsync(p => p.Id == incident.StatusPageId && !p.IsDeleted, cancellationToken);
        if (page == null || !page.AllowSubscriptions) return false;

        await subscriberNotifier.NotifyAsync(incidentId, cancellationToken);
        return true;
    }

    public async Task<bool> AddIncidentUpdateAsync(Guid incidentId, AddIncidentUpdateRequest request, CancellationToken cancellationToken = default)
    {
        Guid? pageId = null;

        var success = await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var incident = await incidentRepo.FindSingleAsync(i => i.Id == incidentId && !i.IsDeleted, cancellationToken);
            if (incident == null) return false;

            incident.Status = request.Status;

            var update = new StatusPageIncidentUpdate
            {
                Message = request.Message,
                Status = request.Status,
                StatusPageIncidentId = incidentId
            };

            await incidentUpdateRepo.AddAsync(update, cancellationToken);

            pageId = incident.StatusPageId;
            return true;
        }, cancellationToken);

        if (success && pageId is { } id)
            await InvalidateUptimeCacheAsync(id, cancellationToken);

        return success;
    }

    /// <summary>
    /// Wipe every cached uptime window for this page. HybridCache lacks wildcard
    /// removal in this project's target framework, so enumerate the known window
    /// sizes — they're cheap and the cache miss for an absent key is a no-op.
    /// Fix 08.P1-5.
    /// </summary>
    private static readonly int[] CachedUptimeDays = [7, 14, 30, 60, 90];

    private async Task InvalidateUptimeCacheAsync(Guid pageId, CancellationToken cancellationToken)
    {
        foreach (var days in CachedUptimeDays)
            await cache.RemoveAsync($"uptime:{pageId}:{days}", cancellationToken);
    }

    public async Task<StatusPageStatsDto> GetStatsAsync(Guid pageId, CancellationToken cancellationToken = default)
    {
        var pageExists = await statusPageRepo.ExistsAsync(p => p.Id == pageId && !p.IsDeleted, cancellationToken);
        if (!pageExists)
        {
            return new StatusPageStatsDto(0, 0, 0, 0);
        }

        var componentCount = await componentRepo.GetQueryable()
            .CountAsync(c => c.StatusPageId == pageId && !c.IsDeleted, cancellationToken);
        var activeIncidentCount = await incidentRepo.GetQueryable()
            .CountAsync(i => i.StatusPageId == pageId && !i.IsDeleted && i.Status != "resolved", cancellationToken);
        var pageViews = await viewRepo.GetQueryable()
            .LongCountAsync(v => v.StatusPageId == pageId, cancellationToken);
        var subscriberCount = await subscriberRepo.GetQueryable()
            .CountAsync(s => s.StatusPageId == pageId && !s.IsDeleted && s.IsConfirmed, cancellationToken);

        return new StatusPageStatsDto(
            ComponentCount: componentCount,
            ActiveIncidentCount: activeIncidentCount,
            PageViews: pageViews,
            SubscriberCount: subscriberCount
        );
    }

    public async Task<IEnumerable<ComponentUptimeDto>> GetUptimeAsync(Guid pageId, int days, CancellationToken cancellationToken = default)
    {
        days = Math.Clamp(days, 7, 90);
        return await cache.GetOrCreateAsync(
            $"uptime:{pageId}:{days}",
            async ct => await BuildUptimeAsync(pageId, days, ct),
            UptimeCacheOptions,
            cancellationToken: cancellationToken);
    }

    private async Task<List<ComponentUptimeDto>> BuildUptimeAsync(Guid pageId, int days, CancellationToken cancellationToken)
    {
        var isPublic = await statusPageRepo.ExistsAsync(p => p.Id == pageId && !p.IsDeleted && p.IsPublic, cancellationToken);
        if (!isPublic) return [];

        var cutoff = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(-(days - 1)), DateTimeKind.Utc);

        var components = (await componentRepo.FindAsync(c => c.StatusPageId == pageId && !c.IsDeleted, cancellationToken))
            .OrderBy(c => c.DisplayOrder)
            .ToList();

        if (!components.Any()) return [];

        var incidents = (await incidentRepo.GetQueryable()
            .Where(i => i.StatusPageId == pageId && !i.IsDeleted && i.CreatedAt >= cutoff)
            .Include(i => i.Updates)
            .ToListAsync(cancellationToken))
            .ToList();

        static string ImpactToStatus(string impact) => impact switch
        {
            "critical" => "major_outage",
            "major"    => "partial_outage",
            "minor"    => "degraded",
            _           => "degraded"
        };

        var statusPriority = new Dictionary<string, int>
        {
            ["operational"]    = 0,
            ["degraded"]       = 1,
            ["partial_outage"] = 2,
            ["major_outage"]   = 3,
            ["maintenance"]    = 1,
            ["no_data"]        = -1,
        };

        var incidentDayMap = new Dictionary<DateOnly, string>();
        foreach (var incident in incidents)
        {
            bool isResolved = incident.Status == "resolved";

            DateTime? resolvedAt = incident.Updates
                .Where(u => u.Status == "resolved")
                .OrderBy(u => u.CreatedAt)
                .Select(u => u.CreatedAt)
                .Cast<DateTime?>()
                .FirstOrDefault();

            var start = DateOnly.FromDateTime(incident.CreatedAt.Date);
            var end = (isResolved && resolvedAt.HasValue)
                ? DateOnly.FromDateTime(resolvedAt.Value.Date).AddDays(-1)
                : DateOnly.FromDateTime(DateTime.UtcNow.Date);

            if (end < start) end = start;

            var incStatus = ImpactToStatus(incident.Impact);

            for (var d = start; d <= end; d = d.AddDays(1))
            {
                if (!incidentDayMap.ContainsKey(d) ||
                    statusPriority.GetValueOrDefault(incidentDayMap[d], 0) < statusPriority.GetValueOrDefault(incStatus, 0))
                {
                    incidentDayMap[d] = incStatus;
                }
            }
        }

        static double StatusToUptimePercent(string s) => s switch
        {
            "operational"    => 100.0,
            "degraded"       => 90.0,
            "maintenance"    => 100.0,
            "partial_outage" => 50.0,
            "major_outage"   => 0.0,
            _                => 100.0
        };

        var result = new List<ComponentUptimeDto>();

        foreach (var component in components)
        {
            var componentCreatedDay = DateOnly.FromDateTime(component.CreatedAt.Date);
            var days30 = new List<UptimeDayDto>(days);

            for (int i = 0; i < days; i++)
            {
                var day = DateOnly.FromDateTime(cutoff.AddDays(i));
                string dayStatus;
                double? uptimePct;

                if (day < componentCreatedDay)
                {
                    dayStatus = "no_data";
                    uptimePct = null;
                }
                else if (incidentDayMap.TryGetValue(day, out var incStatus))
                {
                    dayStatus = incStatus;
                    uptimePct = StatusToUptimePercent(incStatus);
                }
                else
                {
                    dayStatus = "operational";
                    uptimePct = 100.0;
                }

                days30.Add(new UptimeDayDto
                {
                    Date = day,
                    Status = dayStatus,
                    UptimePercent = uptimePct
                });
            }

            var validDays = days30.Where(d => d.UptimePercent.HasValue).ToList();
            var avg = validDays.Any() ? validDays.Average(d => d.UptimePercent!.Value) : 100.0;

            result.Add(new ComponentUptimeDto
            {
                ComponentId = component.Id,
                ComponentName = component.Name,
                CurrentStatus = component.Status,
                AverageUptimePercent = Math.Round(avg, 2),
                Days = days30
            });
        }

        return result;
    }

    public async Task RecordPageViewAsync(Guid pageId, string? visitorHash, CancellationToken cancellationToken = default)
    {
        var pageExists = await statusPageRepo.ExistsAsync(p => p.Id == pageId && !p.IsDeleted && p.IsPublic, cancellationToken);
        if (!pageExists) return;

        await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var view = new StatusPageView
            {
                StatusPageId = pageId,
                VisitorHash = visitorHash,
                ViewedAt = DateTime.UtcNow,
            };
            await viewRepo.AddAsync(view, cancellationToken);
        }, cancellationToken);
    }

    public async Task<bool> SubscribeAsync(Guid pageId, string email, CancellationToken cancellationToken = default)
    {
        var page = await statusPageRepo.FindSingleAsync(p => p.Id == pageId && !p.IsDeleted && p.IsPublic, cancellationToken);
        if (page == null) return false;

        var confirmToken = GenerateUrlSafeToken();
        var unsubscribeToken = GenerateUrlSafeToken();

        var sendConfirmation = await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var existing = await subscriberRepo.FindSingleAsync(
                s => s.StatusPageId == pageId && s.Email == email && !s.IsDeleted, cancellationToken);
            if (existing != null)
            {
                if (existing.IsConfirmed)
                {
                    return false;
                }

                existing.ConfirmationTokenHash = HashToken(confirmToken);
                existing.ConfirmationTokenExpiresAt = DateTime.UtcNow.AddHours(24);
                if (string.IsNullOrEmpty(existing.UnsubscribeTokenHash))
                    existing.UnsubscribeTokenHash = HashToken(unsubscribeToken);
                existing.UpdatedAt = DateTime.UtcNow;
                return true;
            }

            var subscriber = new StatusPageSubscriber
            {
                Id = Guid.NewGuid(),
                StatusPageId = pageId,
                Email = email,
                IsConfirmed = false,
                ConfirmationTokenHash = HashToken(confirmToken),
                ConfirmationTokenExpiresAt = DateTime.UtcNow.AddHours(24),
                UnsubscribeTokenHash = HashToken(unsubscribeToken),
                SubscribedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };
            await subscriberRepo.AddAsync(subscriber, cancellationToken);
            logger.LogInformation("New (unconfirmed) subscriber {Email} for status page {PageId}", email, pageId);
            return true;
        }, cancellationToken);

        if (!sendConfirmation)
        {
            return true;
        }

        try
        {
            var baseUrl = await organizationSettingsService.GetPublicBaseUrlAsync(cancellationToken);
            var confirmLink = $"{baseUrl.TrimEnd('/')}/status/subscribe-confirm?token={Uri.EscapeDataString(confirmToken)}";
            await emailService.SendStatusPageSubscriptionConfirmationAsync(email, page.Name, confirmLink, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Confirmation email send failed for {Email} on status page {PageId}", email, pageId);
        }

        return true;
    }

    public async Task<bool> ConfirmSubscriptionAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        var hash = HashToken(token);

        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var subscriber = await subscriberRepo.FindSingleAsync(
                s => s.ConfirmationTokenHash == hash && !s.IsConfirmed && !s.IsDeleted,
                cancellationToken);

            if (subscriber == null) return false;
            if (subscriber.ConfirmationTokenExpiresAt == null || subscriber.ConfirmationTokenExpiresAt < DateTime.UtcNow)
                return false;

            subscriber.IsConfirmed = true;
            subscriber.ConfirmationTokenHash = null;
            subscriber.ConfirmationTokenExpiresAt = null;
            subscriber.UpdatedAt = DateTime.UtcNow;
            logger.LogInformation("Subscription confirmed: {Email} on page {PageId}", subscriber.Email, subscriber.StatusPageId);
            return true;
        }, cancellationToken);
    }

    public async Task<bool> UnsubscribeAsync(Guid pageId, string email, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var subscriber = await subscriberRepo.FindSingleAsync(
                s => s.StatusPageId == pageId && s.Email == email && !s.IsDeleted, cancellationToken);
            if (subscriber == null) return false;

            subscriber.IsDeleted = true;
            subscriber.UnsubscribedAt = DateTime.UtcNow;
            subscriber.UpdatedAt = DateTime.UtcNow;
            return true;
        }, cancellationToken);
    }

    public async Task<bool> UnsubscribeByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        var hash = HashToken(token);

        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var subscriber = await subscriberRepo.FindSingleAsync(
                s => s.UnsubscribeTokenHash == hash && !s.IsDeleted, cancellationToken);
            if (subscriber == null) return false;

            subscriber.IsDeleted = true;
            subscriber.UnsubscribedAt = DateTime.UtcNow;
            subscriber.UpdatedAt = DateTime.UtcNow;
            logger.LogInformation("Unsubscribed via token: {Email} on page {PageId}", subscriber.Email, subscriber.StatusPageId);
            return true;
        }, cancellationToken);
    }

    /// <summary>URL-safe 32-byte random token (Base64Url). 256 bits of entropy.</summary>
    private static string GenerateUrlSafeToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>SHA-256 hex; matches the <see cref="StatusPageSubscriber.ConfirmationTokenHash"/> column shape.</summary>
    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }

    public async Task<IEnumerable<StatusPageSubscriberDto>> GetSubscribersAsync(Guid pageId, CancellationToken cancellationToken = default)
    {
        var subscribers = await subscriberRepo.GetQueryable()
            .Where(s => s.StatusPageId == pageId && !s.IsDeleted)
            .OrderByDescending(s => s.SubscribedAt)
            .Select(s => new StatusPageSubscriberDto
            {
                Id = s.Id,
                Email = s.Email,
                IsConfirmed = s.IsConfirmed,
                SubscribedAt = s.SubscribedAt
            })
            .ToListAsync(cancellationToken);
        return subscribers;
    }

    /// <summary>
    /// Trimmed projection for the anonymous /slug/{slug} endpoint. Strips every
    /// health-check field that could leak credentials (HealthCheckUrl), internal
    /// topology (sample response, field mappings), or operational state (consecutive
    /// failures). Admin endpoints continue to use <see cref="MapToDetailDto"/>.
    /// </summary>
    private static PublicStatusPageDto MapToPublicDetailDto(StatusPage page)
    {
        return new PublicStatusPageDto
        {
            Id = page.Id,
            Name = page.Name,
            Slug = page.Slug,
            OverallStatus = page.OverallStatus,
            Description = page.Description,
            LogoUrl = page.LogoUrl,
            CustomDomain = page.CustomDomain,
            SupportEmail = page.SupportEmail,
            AllowSubscriptions = page.AllowSubscriptions,
            Components = page.Components
                .Select(c => new PublicStatusPageComponentDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    Description = c.Description,
                    Status = c.Status,
                    DisplayOrder = c.DisplayOrder
                })
                .ToList(),
            Incidents = page.Incidents
                .Select(i => new StatusPageIncidentDto
                {
                    Id = i.Id,
                    Title = i.Title,
                    Status = i.Status,
                    Impact = i.Impact,
                    CreatedAt = i.CreatedAt,
                    Updates = i.Updates
                        .Select(u => u.Adapt<StatusPageIncidentUpdateDto>())
                        .ToList()
                })
                .ToList()
        };
    }

    private static StatusPageDetailDto MapToDetailDto(StatusPage page)
    {
        return new StatusPageDetailDto
        {
            Id = page.Id,
            Name = page.Name,
            Slug = page.Slug,
            IsPublic = page.IsPublic,
            OverallStatus = page.OverallStatus,
            CreatedAt = page.CreatedAt,
            Description = page.Description,
            LogoUrl = page.LogoUrl,
            CustomDomain = page.CustomDomain,
            SupportEmail = page.SupportEmail,
            AllowSubscriptions = page.AllowSubscriptions,
            Components = page.Components
                .Select(c => c.Adapt<StatusPageComponentDto>())
                .ToList(),
            Incidents = page.Incidents
                .Select(i => new StatusPageIncidentDto
                {
                    Id = i.Id,
                    Title = i.Title,
                    Status = i.Status,
                    Impact = i.Impact,
                    CreatedAt = i.CreatedAt,
                    Updates = i.Updates
                        .Select(u => u.Adapt<StatusPageIncidentUpdateDto>())
                        .ToList()
                })
                .ToList()
        };
    }

}
