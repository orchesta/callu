using System.Text.Json;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Infrastructure.Push;
using Callu.Shared.Localization;
using Callu.Shared.Models.Settings;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Services;

public class FirebaseSettingsService(
    IFirebaseSettingsRepository settingsRepo,
    ITransactionManager transactionManager,
    HybridCache cache,
    FirebaseCredentialProtector credentialProtector,
    FcmAccessTokenProvider accessTokens,
    ILogger<FirebaseSettingsService> logger) : IFirebaseSettingsService
{
    private const string CacheKey = "firebase-settings";

    public async Task<FirebaseSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        return await cache.GetOrCreateAsync(CacheKey, async ct =>
        {
            var settings = await settingsRepo.GetSettingsAsync(ct);
            return settings is null ? new FirebaseSettingsDto() : ToDto(settings);
        }, cancellationToken: cancellationToken);
    }

    public async Task<bool> SaveSettingsAsync(
        UpdateFirebaseSettingsRequest request, CancellationToken cancellationToken = default)
    {
        await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var settings = await settingsRepo.GetSettingsAsync(cancellationToken);
            if (settings is null)
            {
                settings = new FirebaseSettings
                {
                    Id = FirebaseSettings.SingletonId,
                    CreatedAt = DateTime.UtcNow
                };
                await settingsRepo.AddAsync(settings, cancellationToken);
            }

            if (request.ClearCredential)
            {
                settings.ServiceAccountJson = null;
                settings.ProjectId = null;
                settings.IsConfigured = false;
            }
            else if (!string.IsNullOrWhiteSpace(request.ServiceAccountJson))
            {
                var json = request.ServiceAccountJson.Trim();
                if (!TryParseServiceAccount(json, out var projectId, out var error))
                {
                    logger.LogWarning("Rejected Firebase service account JSON: {Error}", error);
                    throw new ArgumentException(error);
                }

                settings.ServiceAccountJson = credentialProtector.Protect(json);
                settings.ProjectId = string.IsNullOrWhiteSpace(request.ProjectId)
                    ? projectId
                    : request.ProjectId.Trim();
            }
            else if (!string.IsNullOrWhiteSpace(request.ProjectId))
            {
                settings.ProjectId = request.ProjectId.Trim();
            }

            settings.IsConfigured = !string.IsNullOrEmpty(settings.ServiceAccountJson)
                                    && !string.IsNullOrWhiteSpace(settings.ProjectId);
            settings.UpdatedAt = DateTime.UtcNow;
            return true;
        }, cancellationToken);

        // Both caches are dropped after the commit: a read landing between an earlier eviction and
        // the commit would re-populate with the superseded row and stay stale until the next write.
        // Invalidate() only reaches this process, so the other host carries its access token until it
        // expires — a rotation is visible there within the hour, not instantly.
        await cache.RemoveAsync(CacheKey, cancellationToken);
        accessTokens.Invalidate();
        return true;
    }

    public async Task<FirebaseTestResult> TestAsync(CancellationToken cancellationToken = default)
    {
        var result = await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var settings = await settingsRepo.GetSettingsAsync(cancellationToken);
            if (settings is null || string.IsNullOrEmpty(settings.ServiceAccountJson))
            {
                return new FirebaseTestResult
                {
                    Success = false,
                    Message = Messages.Get("firebase.notConfigured")
                };
            }

            var plaintext = credentialProtector.Unprotect(settings.ServiceAccountJson);
            if (plaintext is null)
            {
                settings.IsConfigured = false;
                settings.LastTestedAt = DateTime.UtcNow;
                settings.LastTestResult = "Stored credential could not be decrypted — re-enter service account JSON";
                return new FirebaseTestResult
                {
                    Success = false,
                    Message = Messages.Get("firebase.credentialDecryptFailed")
                };
            }

            if (!TryParseServiceAccount(plaintext, out _, out var error))
            {
                settings.IsConfigured = false;
                settings.LastTestedAt = DateTime.UtcNow;
                settings.LastTestResult = error;
                return new FirebaseTestResult { Success = false, Message = error };
            }

            try
            {
                accessTokens.Invalidate();
                await accessTokens.ProbeAsync(plaintext, cancellationToken);
                settings.IsConfigured = true;
                settings.LastTestedAt = DateTime.UtcNow;
                settings.LastTestResult = "OAuth token acquired";
                return new FirebaseTestResult
                {
                    Success = true,
                    Message = Messages.Get("firebase.testPassed")
                };
            }
            catch (Exception ex)
            {
                settings.IsConfigured = false;
                settings.LastTestedAt = DateTime.UtcNow;
                settings.LastTestResult = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
                logger.LogWarning(ex, "Firebase credential probe failed");
                return new FirebaseTestResult
                {
                    Success = false,
                    Message = settings.LastTestResult ?? ex.Message
                };
            }
        }, cancellationToken);

        await cache.RemoveAsync(CacheKey, cancellationToken);
        return result;
    }

    public static bool TryParseServiceAccount(string json, out string projectId, out string error)
    {
        projectId = "";
        error = "";
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("project_id", out var pid) || pid.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(pid.GetString()))
            {
                error = "Service account JSON must include project_id";
                return false;
            }
            if (!root.TryGetProperty("client_email", out var email) || email.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(email.GetString()))
            {
                error = "Service account JSON must include client_email";
                return false;
            }
            if (!root.TryGetProperty("private_key", out var key) || key.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(key.GetString()))
            {
                error = "Service account JSON must include private_key";
                return false;
            }

            projectId = pid.GetString()!;
            if (!FirebaseSettings.IsValidProjectId(projectId))
            {
                error = "project_id may contain only lowercase letters, digits, and hyphens";
                projectId = "";
                return false;
            }

            return true;
        }
        catch (JsonException)
        {
            error = "Service account must be valid JSON";
            return false;
        }
    }

    private static FirebaseSettingsDto ToDto(FirebaseSettings s) => new()
    {
        Id = s.Id,
        ProjectId = s.ProjectId,
        HasCredential = !string.IsNullOrEmpty(s.ServiceAccountJson),
        IsConfigured = s.IsConfigured,
        LastTestedAt = s.LastTestedAt,
        LastTestResult = s.LastTestResult
    };
}
