using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Infrastructure.Persistence;
using Callu.Shared.Models.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Push;

public sealed class FcmMobilePushSender(
    IFirebaseSettingsRepository settingsRepo,
    IUserPushDeviceRepository devices,
    IServiceScopeFactory scopeFactory,
    FirebaseCredentialProtector credentialProtector,
    FcmAccessTokenProvider accessTokens,
    IHttpClientFactory httpClientFactory,
    ILogger<FcmMobilePushSender> logger) : IMobilePushSender
{
    public async Task SendToUserAsync(
        string userId, NotificationItemDto notification, CancellationToken cancellationToken = default)
    {
        var settings = await settingsRepo.GetSettingsAsync(cancellationToken);
        if (settings is null || !settings.IsConfigured || string.IsNullOrEmpty(settings.ServiceAccountJson)
            || string.IsNullOrWhiteSpace(settings.ProjectId))
            return;

        if (!FirebaseSettings.IsValidProjectId(settings.ProjectId))
        {
            logger.LogWarning("Skipping FCM send — stored Firebase project id is not a valid project id");
            return;
        }

        var plaintext = credentialProtector.Unprotect(settings.ServiceAccountJson);
        if (plaintext is null)
        {
            logger.LogWarning("Skipping FCM send — Firebase credential could not be decrypted");
            return;
        }

        var tokens = await devices.GetActiveByUserIdAsync(userId, cancellationToken);
        if (tokens.Count == 0) return;

        string accessToken;
        try
        {
            accessToken = await accessTokens.GetAccessTokenAsync(plaintext, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "FCM access token unavailable — mobile push skipped");
            return;
        }

        var client = httpClientFactory.CreateClient("fcm");
        var url = $"https://fcm.googleapis.com/v1/projects/{Uri.EscapeDataString(settings.ProjectId)}/messages:send";
        var stale = new List<Guid>();

        var credentialRefreshed = false;

        async Task<HttpResponseMessage> PostAsync(string payload, string bearer)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            return await client.SendAsync(req, cancellationToken);
        }

        async Task RecordFailureAsync(HttpResponseMessage response, Guid deviceId)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (IsTokenRejected(response.StatusCode, body))
            {
                stale.Add(deviceId);
                return;
            }

            logger.LogWarning(
                "FCM send failed for user {UserId}: {Status} {Body}",
                userId, (int)response.StatusCode, Truncate(body));
        }

        foreach (var device in tokens)
        {
            try
            {
                var payload = BuildPayload(device.PushToken, notification);

                using (var response = await PostAsync(payload, accessToken))
                {
                    if (response.IsSuccessStatusCode) continue;

                    // A cached credential that has stopped being accepted would fail every remaining
                    // device the same way, silently, until it expired on its own.
                    if (response.StatusCode != HttpStatusCode.Unauthorized || credentialRefreshed)
                    {
                        await RecordFailureAsync(response, device.Id);
                        continue;
                    }

                    credentialRefreshed = true;
                    accessTokens.Invalidate();
                    accessToken = await accessTokens.GetAccessTokenAsync(plaintext, cancellationToken);
                }

                using var retried = await PostAsync(payload, accessToken);
                if (retried.IsSuccessStatusCode) continue;

                await RecordFailureAsync(retried, device.Id);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "FCM send failed for user {UserId}", userId);
            }
        }

        if (stale.Count > 0)
            await PruneAsync(userId, stale, cancellationToken);
    }

    /// <summary>True only when FCM named this specific registration token as no longer valid.</summary>
    // A wrong or deleted project answers 404 too, and treating that as a dead token would delete
    // every registration in the instance; the FcmError detail is what separates the two.
    private static bool IsTokenRejected(HttpStatusCode status, string body)
    {
        if (status != HttpStatusCode.NotFound && status != HttpStatusCode.BadRequest)
            return false;

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("error", out var error)
                || !error.TryGetProperty("details", out var details)
                || details.ValueKind != JsonValueKind.Array)
                return false;

            foreach (var detail in details.EnumerateArray())
            {
                if (detail.ValueKind != JsonValueKind.Object) continue;

                if (detail.TryGetProperty("errorCode", out var code)
                    && code.ValueKind == JsonValueKind.String
                    && string.Equals(code.GetString(), "UNREGISTERED", StringComparison.Ordinal))
                    return true;

                // INVALID_ARGUMENT on its own is request-level, and every device of every user gets
                // the same payload shape — matching on it would delete the whole fleet on one bad
                // message. Only a violation that names the token field is about this registration.
                if (status == HttpStatusCode.BadRequest && NamesTheTokenField(detail))
                    return true;
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    /// <summary>Whether a google.rpc.BadRequest detail blames the registration token itself.</summary>
    private static bool NamesTheTokenField(JsonElement detail)
    {
        if (!detail.TryGetProperty("fieldViolations", out var violations)
            || violations.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var violation in violations.EnumerateArray())
        {
            if (violation.ValueKind == JsonValueKind.Object
                && violation.TryGetProperty("field", out var field)
                && field.ValueKind == JsonValueKind.String
                && string.Equals(field.GetString(), "message.token", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>Soft-deletes dead registrations on their own context.</summary>
    // The caller may be mid-dispatch on a shared context whose outcome is committed separately;
    // saving here would flush that unit of work at a point it did not choose.
    private async Task PruneAsync(string userId, List<Guid> deviceIds, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var pruned = await db.UserPushDevices
                .Where(d => deviceIds.Contains(d.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.IsDeleted, true), cancellationToken);

            logger.LogInformation(
                "Pruned {Count} unregistered FCM token(s) for user {UserId}", pruned, userId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not prune unregistered FCM tokens for user {UserId}", userId);
        }
    }

    private static string BuildPayload(string token, NotificationItemDto notification)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteStartObject("message");
            writer.WriteString("token", token);
            writer.WriteStartObject("notification");
            writer.WriteString("title", Clip(notification.Title, 200));
            writer.WriteString("body", Clip(notification.Message, 500));
            writer.WriteEndObject();
            if (!string.IsNullOrWhiteSpace(notification.ActionUrl))
            {
                writer.WriteStartObject("data");
                writer.WriteString("actionUrl", notification.ActionUrl);
                writer.WriteString("notificationId", notification.Id.ToString());
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string Clip(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Length <= max ? value : value[..max];
    }

    private static string Truncate(string s) => s.Length <= 300 ? s : s[..300];
}
