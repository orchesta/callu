using Callu.Domain.Entities;

namespace Callu.Application.Common.Interfaces.Persistence;

/// <summary>
/// Call token persistence using an isolated <see cref="Microsoft.EntityFrameworkCore.IDbContextFactory{TContext}"/> per operation (VoxEngine / callback paths).
/// </summary>
public interface ICallTokenFactoryRepository
{
    Task InsertAsync(CallToken token, CancellationToken cancellationToken = default);

    /// <summary>Marks consumed when found and valid; expired rows are marked consumed.</summary>
    Task<CallTokenPlainConsumeResult> ConsumePlainAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>Reads a token's call data without consuming it. Used by callback handlers
    /// that only need to correlate the incident — consuming would race with the
    /// scenario's own <see cref="ConsumePlainAsync"/> call-data fetch.</summary>
    Task<CallTokenPeekResult> PeekAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>Consumes only if <paramref name="isAllowedAsync"/> returns true; otherwise leaves token usable.</summary>
    Task<CallTokenScenarioConsumeResult> ConsumeIfAllowedAsync(
        string token,
        Func<string, Task<bool>> isAllowedAsync,
        CancellationToken cancellationToken = default);

    Task<int> DeleteConsumedOrExpiredAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default);
}

public enum CallTokenPlainConsumeStep { NotFound, Expired, AlreadyConsumed, Success }

public readonly record struct CallTokenPlainConsumeResult(
    CallTokenPlainConsumeStep Step,
    string? CallDataJson,
    DateTime? CreatedAt,
    DateTime? ExpiresAt);

public enum CallTokenScenarioStep { NotFound, Expired, AlreadyConsumed, ValidationRejected, Success }

public readonly record struct CallTokenScenarioConsumeResult(
    CallTokenScenarioStep Step,
    string? CallDataJson,
    DateTime? TokenCreatedAt = null,
    DateTime? TokenExpiresAt = null);

public enum CallTokenPeekStep { NotFound, Success }

public readonly record struct CallTokenPeekResult(
    CallTokenPeekStep Step,
    string? CallDataJson);
