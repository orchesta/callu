using Callu.Infrastructure.Audit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace Callu.Infrastructure.Quartz;

/// <summary>Writes the audit trail's closed days out to the archive directory.</summary>
[DisallowConcurrentExecution]
public sealed class AuditArchiveQuartzJob(
    IServiceScopeFactory scopeFactory,
    IOptions<AuditArchiveOptions> options,
    ILogger<AuditArchiveQuartzJob> logger)
    : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        if (!options.Value.Enabled) return;

        using var scope = scopeFactory.CreateScope();
        var archive = scope.ServiceProvider.GetRequiredService<AuditArchiveService>();

        var result = await archive.ArchiveAsync(DateTime.UtcNow, context.CancellationToken);

        if (result.DaysArchived >= options.Value.MaxDaysPerRun)
            logger.LogWarning(
                "Audit archive stopped at its per-run ceiling of {Days} day(s); the rest waits for the next run. "
                + "Retention cannot prune past {Through} until it catches up",
                options.Value.MaxDaysPerRun, result.ArchivedThrough);
    }
}
