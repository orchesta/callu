using Callu.Application.Services;
using Callu.Domain.Enums;
using Callu.Infrastructure.Audit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Callu.Infrastructure.Quartz;

/// <summary>Replays the audit chain daily and reports a break where an operator will see it.</summary>
[DisallowConcurrentExecution]
public sealed class AuditChainVerifyQuartzJob(
    IServiceScopeFactory scopeFactory,
    ILogger<AuditChainVerifyQuartzJob> logger)
    : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        using var scope = scopeFactory.CreateScope();
        var chain = scope.ServiceProvider.GetRequiredService<AuditChainService>();

        var verdict = await chain.VerifyAsync(context.CancellationToken);

        if (verdict.Intact)
        {
            logger.LogInformation(
                "Audit chain verified: {Count} entry(ies) intact", verdict.CheckedCount);
            return;
        }

        if (verdict.FirstBrokenSequence is null)
        {
            logger.LogError("Audit chain cannot be verified: {Reason}", verdict.Reason);
        }
        else
        {
            logger.LogError(
                "Audit chain is broken at sequence {Sequence}: {Reason} ({Count} entry(ies) verified before it)",
                verdict.FirstBrokenSequence, verdict.Reason, verdict.CheckedCount);
        }

        // Also written to the audit trail, because that is the screen an auditor opens. The copy
        // that survives someone with database access is the stream sink, not this row.
        try
        {
            var audit = scope.ServiceProvider.GetRequiredService<IAuditLogService>();
            await audit.LogAsync(
                null,
                AuditAction.IntegrityBroken,
                nameof(AuditAction.IntegrityBroken),
                (verdict.FirstBrokenSequence ?? 0).ToString(),
                description: verdict.Reason,
                cancellationToken: context.CancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not record the audit chain break in the audit log");
        }
    }
}
