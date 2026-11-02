using Callu.Infrastructure.Audit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Callu.Infrastructure.Quartz;

/// <summary>Chains the audit entries written since the last run.</summary>
[DisallowConcurrentExecution]
public sealed class AuditChainSealQuartzJob(
    IServiceScopeFactory scopeFactory,
    ILogger<AuditChainSealQuartzJob> logger)
    : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        using var correlation = AuditJobCorrelation.Begin(context);
        using var scope = scopeFactory.CreateScope();
        var chain = scope.ServiceProvider.GetRequiredService<AuditChainService>();

        var result = await chain.SealAsync(context.CancellationToken);

        // A FailureReason is either the key ring (nothing sealed, ProtectedKey left alone: do not mint)
        // or a public-chain stop (the keyed chain still sealed). SealAsync has logged it as an Error.
        // IntegrityBroken is written by the daily verify job (and by POST /audit-logs/verify) so a
        // 5-minute seal cadence does not flood the trail with identical rows.
        if (result.SealedCount > 0)
            logger.LogInformation(
                "Audit chain sealed {Count} entry(ies), now through sequence {Sequence}",
                result.SealedCount, result.LastSequence);

        if (result.PublicSealedCount > 0)
            logger.LogInformation(
                "Audit public chain extended over {Count} entry(ies), now through sequence {Sequence}",
                result.PublicSealedCount, result.PublicHead);
    }
}
