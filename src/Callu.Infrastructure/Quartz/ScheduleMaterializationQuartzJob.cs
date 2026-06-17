using Callu.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Callu.Infrastructure.Quartz;

/// <summary>
/// Daily job: extends the materialization horizon for every active schedule. Without this
/// a long-running deployment would eventually run out of pre-generated occurrences and
/// on-call queries would return null for dates beyond the horizon.
/// </summary>
[DisallowConcurrentExecution]
public sealed class ScheduleMaterializationQuartzJob(
    IServiceScopeFactory scopeFactory,
    ILogger<ScheduleMaterializationQuartzJob> logger)
    : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var materializer = scope.ServiceProvider.GetRequiredService<IScheduleMaterializer>();
            await materializer.RematerializeAllAsync(IScheduleMaterializer.DefaultHorizon, context.CancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Quartz schedule-materialization job failed");
            throw;
        }
    }
}
