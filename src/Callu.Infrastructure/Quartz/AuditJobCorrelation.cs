using Callu.Infrastructure.Audit;
using Quartz;

namespace Callu.Infrastructure.Quartz;

/// <summary>The correlation id every audit row of one job run shares.</summary>
public static class AuditJobCorrelation
{
    private const int MaxLength = 256;

    public static IDisposable Begin(IJobExecutionContext context)
    {
        var id = $"{context.JobDetail.Key.Name}:{context.FireInstanceId}";
        return AuditCorrelationScope.Begin(id.Length <= MaxLength ? id : id[..MaxLength]);
    }
}
