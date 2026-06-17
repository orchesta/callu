using Callu.Application.Common.Interfaces.Persistence;
using Callu.Domain.Entities;
using Callu.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Callu.Infrastructure.Persistence.Voximplant;

/// <summary>
/// Repository for single-use call tokens.
/// Consumption is atomic: a single SQL <c>UPDATE ... WHERE NOT consumed AND expires &gt; now()</c>
/// acts as the race-winner. Two concurrent calls for the same token will each see exactly one
/// affected row and the other sees zero; the loser then re-reads to produce an "already consumed"
/// or "expired" diagnostic without a second chance to observe the call payload.
/// </summary>
public class CallTokenFactoryRepository(IDbContextFactory<ApplicationDbContext> contextFactory)
    : ICallTokenFactoryRepository
{
    public async Task InsertAsync(CallToken token, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.CallTokens.Add(token);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<CallTokenPlainConsumeResult> ConsumePlainAsync(string token, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTime.UtcNow;

        var affected = await context.CallTokens
            .Where(t => t.Token == token && !t.IsConsumed && t.ConsumedAt == null && t.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.IsConsumed, true)
                .SetProperty(t => t.ConsumedAt, now), cancellationToken);

        if (affected == 1)
        {
            var winning = await context.CallTokens
                .AsNoTracking()
                .Where(t => t.Token == token)
                .Select(t => new { t.CallDataJson, t.CreatedAt, t.ExpiresAt })
                .FirstAsync(cancellationToken);

            return new CallTokenPlainConsumeResult(
                CallTokenPlainConsumeStep.Success,
                winning.CallDataJson,
                winning.CreatedAt,
                winning.ExpiresAt);
        }

        return await DiagnoseAsync(context, token, cancellationToken);
    }

    public async Task<CallTokenScenarioConsumeResult> ConsumeIfAllowedAsync(
        string token,
        Func<string, Task<bool>> isAllowedAsync,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var peek = await context.CallTokens
            .AsNoTracking()
            .Where(t => t.Token == token)
            .Select(t => new { t.CallDataJson, t.IsConsumed, t.ConsumedAt, t.ExpiresAt, t.CreatedAt })
            .FirstOrDefaultAsync(cancellationToken);

        if (peek is null)
            return new CallTokenScenarioConsumeResult(CallTokenScenarioStep.NotFound, null);

        if (peek.IsConsumed || peek.ConsumedAt.HasValue)
            return new CallTokenScenarioConsumeResult(
                CallTokenScenarioStep.AlreadyConsumed, null, peek.CreatedAt, peek.ExpiresAt);

        if (DateTime.UtcNow > peek.ExpiresAt)
            return new CallTokenScenarioConsumeResult(
                CallTokenScenarioStep.Expired, null, peek.CreatedAt, peek.ExpiresAt);

        if (!await isAllowedAsync(peek.CallDataJson))
            return new CallTokenScenarioConsumeResult(CallTokenScenarioStep.ValidationRejected, null);

        var now = DateTime.UtcNow;
        var affected = await context.CallTokens
            .Where(t => t.Token == token && !t.IsConsumed && t.ConsumedAt == null && t.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.IsConsumed, true)
                .SetProperty(t => t.ConsumedAt, now), cancellationToken);

        if (affected == 1)
            return new CallTokenScenarioConsumeResult(
                CallTokenScenarioStep.Success,
                peek.CallDataJson,
                peek.CreatedAt,
                peek.ExpiresAt);

        return new CallTokenScenarioConsumeResult(
            CallTokenScenarioStep.AlreadyConsumed, null, peek.CreatedAt, peek.ExpiresAt);
    }

    public async Task<CallTokenPeekResult> PeekAsync(string token, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var callDataJson = await context.CallTokens
            .AsNoTracking()
            .Where(t => t.Token == token)
            .Select(t => t.CallDataJson)
            .FirstOrDefaultAsync(cancellationToken);

        return callDataJson is null
            ? new CallTokenPeekResult(CallTokenPeekStep.NotFound, null)
            : new CallTokenPeekResult(CallTokenPeekStep.Success, callDataJson);
    }

    public async Task<int> DeleteConsumedOrExpiredAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.CallTokens
            .Where(t => t.IsConsumed || t.ConsumedAt != null || t.ExpiresAt < cutoffUtc)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static async Task<CallTokenPlainConsumeResult> DiagnoseAsync(
        ApplicationDbContext context, string token, CancellationToken cancellationToken)
    {
        var entry = await context.CallTokens
            .AsNoTracking()
            .Where(t => t.Token == token)
            .Select(t => new { t.IsConsumed, t.ConsumedAt, t.CreatedAt, t.ExpiresAt })
            .FirstOrDefaultAsync(cancellationToken);

        if (entry is null)
            return new CallTokenPlainConsumeResult(CallTokenPlainConsumeStep.NotFound, null, null, null);

        if (entry.IsConsumed || entry.ConsumedAt.HasValue)
            return new CallTokenPlainConsumeResult(
                CallTokenPlainConsumeStep.AlreadyConsumed, null, entry.CreatedAt, entry.ExpiresAt);

        return new CallTokenPlainConsumeResult(
            CallTokenPlainConsumeStep.Expired, null, entry.CreatedAt, entry.ExpiresAt);
    }
}
