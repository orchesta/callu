using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Infrastructure.Email;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Bridges the DB-backed email-template editor to the live send pipeline, returning null when
/// there is no active row so the caller falls back to the file templates.
/// </summary>
public class DbEmailTemplateResolver(
    IEmailTemplateRepository templateRepo,
    HybridCache cache,
    ILogger<DbEmailTemplateResolver> logger) : IDbEmailTemplateResolver
{
    /// <summary>Symmetric with the file loader's 5-minute cache (EmailTemplates.CacheRefreshInterval).</summary>
    private static readonly HybridCacheEntryOptions CacheOptions = new() { Expiration = TimeSpan.FromMinutes(5) };

    internal static string CacheKey(string key) => $"email-template:{key}";

    public async Task<RenderedEmailTemplate?> TryRenderAsync(
        string key, IReadOnlyDictionary<string, string> variables, CancellationToken cancellationToken = default)
    {
        try
        {
            var cached = await cache.GetOrCreateAsync<CachedTemplate?>(
                CacheKey(key),
                async ct =>
                {
                    var row = await templateRepo.GetByKeyAsync(key, ct);
                    if (row is null || row.IsDeleted || !row.IsActive) return null;
                    return new CachedTemplate(row.Subject, row.HtmlBody);
                },
                CacheOptions,
                cancellationToken: cancellationToken);

            if (cached is null) return null;

            return new RenderedEmailTemplate(
                // Subjects are plain text in mail clients — no HTML encoding there.
                TemplateVariableRenderer.ReplaceVariablesPlain(cached.Subject, variables),
                TemplateVariableRenderer.ReplaceVariables(cached.HtmlBody, variables));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fail open to the file templates — a cache/DB hiccup must never block an email.
            logger.LogWarning(ex, "DB email-template resolve failed for key '{Key}'; falling back to file template", key);
            return null;
        }
    }

    public async Task InvalidateAsync(string key, CancellationToken cancellationToken = default)
    {
        await cache.RemoveAsync(CacheKey(key), cancellationToken);
    }

    private sealed record CachedTemplate(string Subject, string HtmlBody);
}
