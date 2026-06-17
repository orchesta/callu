using System.Text.Json;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Shared.Models.Communication;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Providers.Voximplant;

/// <summary>
/// DB-backed call token service for VoxEngine data fetch.
/// Tokens are one-time use, persisted to database, and expire after 5 minutes.
/// Survives app restarts — VoxEngine can always fetch call data.
/// </summary>
public class VoximplantCallDataService(
    ILogger<VoximplantCallDataService> logger,
    ICallTokenFactoryRepository callTokens,
    IVoximplantScenarioKeyValidator scenarioKeys,
    IVoximplantVoiceCallbackPersistence voiceCallback,
    VoxSipPasswordProtector sipPasswordProtector) : ICallDataService
{
    private static readonly TimeSpan TokenExpiry = TimeSpan.FromMinutes(10);

    private static DateTime _lastCleanup = DateTime.MinValue;
    private static readonly object CleanupLock = new();

    public async Task<string> CreateCallTokenAsync(VoxCallData callData, CancellationToken cancellationToken = default)
    {
        _ = CleanExpiredTokensAsync(cancellationToken);

        var token = Guid.NewGuid().ToString("N");

        var protectedPayload = ProtectSipPassword(callData);

        await callTokens.InsertAsync(new CallToken
        {
            Token = token,
            CallDataJson = JsonSerializer.Serialize(protectedPayload),
            ExpiresAt = DateTime.UtcNow.Add(TokenExpiry),
            IsConsumed = false,
            CreatedAt = DateTime.UtcNow
        }, cancellationToken);

        VoximplantCallDataServiceLog.CallTokenCreated(logger, callData.IncidentId, TokenExpiry.TotalMinutes);

        return token;
    }

    /// <summary>
    /// Returns a copy of <paramref name="source"/> with <c>SipPassword</c> replaced
    /// by its DataProtection-protected ciphertext (prefixed with the v1 sentinel).
    /// Mutating the caller's object is avoided so a single instance round-tripping
    /// the call stack doesn't leak ciphertext to the wire.
    /// </summary>
    private VoxCallData ProtectSipPassword(VoxCallData source)
    {
        if (string.IsNullOrEmpty(source.SipPassword)) return source;
        return new VoxCallData
        {
            IncidentId = source.IncidentId,
            Title = source.Title,
            Severity = source.Severity,
            ServiceName = source.ServiceName,
            Description = source.Description,
            Phone = source.Phone,
            CountryCode = source.CountryCode,
            Language = source.Language,
            TtsMessages = source.TtsMessages,
            SipServer = source.SipServer,
            SipUsername = source.SipUsername,
            SipPassword = sipPasswordProtector.Protect(source.SipPassword),
            CallerId = source.CallerId,
            ConferenceId = source.ConferenceId,
            MaxParticipants = source.MaxParticipants,
            Record = source.Record
        };
    }

    /// <summary>
    /// In-place decrypt of the SIP password on a freshly deserialized VoxCallData.
    /// Plaintext-prefix-missing values pass through unchanged for rollback safety.
    /// </summary>
    private void UnprotectSipPassword(VoxCallData? data)
    {
        if (data is null || string.IsNullOrEmpty(data.SipPassword)) return;
        data.SipPassword = sipPasswordProtector.Unprotect(data.SipPassword);
    }

    public async Task<VoxCallData?> ConsumeCallTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        var r = await callTokens.ConsumePlainAsync(token, cancellationToken);
        switch (r.Step)
        {
            case CallTokenPlainConsumeStep.NotFound:
                VoximplantCallDataServiceLog.CallTokenNotFound(logger, token[..Math.Min(8, token.Length)] + "...");
                return null;
            case CallTokenPlainConsumeStep.Expired:
                VoximplantCallDataServiceLog.CallTokenExpired(logger, r.CreatedAt ?? DateTime.UtcNow, r.ExpiresAt ?? DateTime.UtcNow);
                return null;
            case CallTokenPlainConsumeStep.Success:
            {
                var callData = JsonSerializer.Deserialize<VoxCallData>(r.CallDataJson!);
                UnprotectSipPassword(callData);
                VoximplantCallDataServiceLog.CallTokenConsumed(logger, callData?.IncidentId);
                return callData;
            }
            default:
                return null;
        }
    }

    public async Task<CallTokenConsumeOutcome> ConsumeCallTokenWithScenarioCheckAsync(
        string token,
        string scenarioApiKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(scenarioApiKey))
            return new CallTokenConsumeOutcome(CallTokenConsumeStatus.ScenarioKeyRejected, null);

        var result = await callTokens.ConsumeIfAllowedAsync(
            token,
            async _ => await scenarioKeys.ValidateAsync(scenarioApiKey, cancellationToken),
            cancellationToken);

        switch (result.Step)
        {
            case CallTokenScenarioStep.NotFound:
                VoximplantCallDataServiceLog.CallTokenNotFound(logger, token[..Math.Min(8, token.Length)] + "...");
                return new CallTokenConsumeOutcome(CallTokenConsumeStatus.NotFound, null);
            case CallTokenScenarioStep.Expired:
                VoximplantCallDataServiceLog.CallTokenExpired(
                    logger,
                    result.TokenCreatedAt ?? DateTime.UtcNow,
                    result.TokenExpiresAt ?? DateTime.UtcNow);
                return new CallTokenConsumeOutcome(CallTokenConsumeStatus.Expired, null);
            case CallTokenScenarioStep.AlreadyConsumed:
                logger.LogWarning("VoxEngine call-data: replay attempt for already-consumed token {TokenPrefix}",
                    token[..Math.Min(8, token.Length)] + "...");
                return new CallTokenConsumeOutcome(CallTokenConsumeStatus.AlreadyConsumed, null);
            case CallTokenScenarioStep.ValidationRejected:
                logger.LogWarning("VoxEngine call-data: scenario key rejected.");
                return new CallTokenConsumeOutcome(CallTokenConsumeStatus.ScenarioKeyRejected, null);
            case CallTokenScenarioStep.Success:
            {
                var callData = JsonSerializer.Deserialize<VoxCallData>(result.CallDataJson!);
                UnprotectSipPassword(callData);
                VoximplantCallDataServiceLog.CallTokenConsumed(logger, callData?.IncidentId);
                return new CallTokenConsumeOutcome(CallTokenConsumeStatus.Success, callData);
            }
            default:
                return new CallTokenConsumeOutcome(CallTokenConsumeStatus.NotFound, null);
        }
    }

    public Task<bool> ValidateScenarioApiKeyAsync(string apiKey, CancellationToken cancellationToken = default) =>
        scenarioKeys.ValidateAsync(apiKey, cancellationToken);

    public Task ProcessCallbackAsync(
        VoxCallbackRequest callback,
        string? scenarioApiKey = null,
        CancellationToken cancellationToken = default) =>
        voiceCallback.ProcessAsync(
            callback,
            scenarioApiKey,
            new VoximplantCallbackProcessingCallbacks(
                PeekCallTokenAsync,
                ValidateScenarioApiKeyAsync,
                NotifyActiveVoiceCallsChangedAsync),
            cancellationToken);

    /// <summary>Non-consuming read — lets the callback path recover incident_id without
    /// racing the scenario's own call-data fetch which legitimately consumes the token.</summary>
    private async Task<VoxCallData?> PeekCallTokenAsync(string token, CancellationToken cancellationToken)
    {
        var result = await callTokens.PeekAsync(token, cancellationToken);
        if (result.Step != CallTokenPeekStep.Success || string.IsNullOrEmpty(result.CallDataJson))
            return null;
        try
        {
            var data = JsonSerializer.Deserialize<VoxCallData>(result.CallDataJson);
            UnprotectSipPassword(data);
            return data;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private Task NotifyActiveVoiceCallsChangedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task CleanExpiredTokensAsync(CancellationToken cancellationToken)
    {
        lock (CleanupLock)
        {
            if (DateTime.UtcNow - _lastCleanup < TimeSpan.FromMinutes(1))
                return;
            _lastCleanup = DateTime.UtcNow;
        }

        try
        {
            var cutoff = DateTime.UtcNow;
            var expiredCount = await callTokens.DeleteConsumedOrExpiredAsync(cutoff, cancellationToken);
            if (expiredCount > 0)
                VoximplantCallDataServiceLog.CleanedExpiredTokens(logger, expiredCount);
        }
        catch (Exception ex)
        {
            VoximplantCallDataServiceLog.CleanupFailed(logger, ex);
        }
    }
}
