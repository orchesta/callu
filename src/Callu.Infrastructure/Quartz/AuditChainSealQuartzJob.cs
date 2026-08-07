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
        using var scope = scopeFactory.CreateScope();
        var chain = scope.ServiceProvider.GetRequiredService<AuditChainService>();

        var result = await chain.SealAsync(context.CancellationToken);

        // Key-ring failure: SealAsync already logged Error and left ProtectedKey alone. Do not mint.
        // IntegrityBroken is written by the daily verify job (and by POST /audit-logs/verify) so a
        // 5-minute seal cadence does not flood the trail with identical rows.
        if (result.FailureReason is not null)
            return;

        if (result.SealedCount > 0)
            logger.LogInformation(
                "Audit chain sealed {Count} entry(ies), now through sequence {Sequence}",
                result.SealedCount, result.LastSequence);
    }
}
