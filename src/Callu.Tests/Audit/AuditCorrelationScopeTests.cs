using Callu.Infrastructure.Audit;

namespace Callu.Tests.Audit;

public class AuditCorrelationScopeTests
{
    [Fact]
    public void BeginIfAbsent_DoesNotReplaceAnOuterOperation()
    {
        using (AuditCorrelationScope.Begin("EscalationProcessingJob:fire-1"))
        {
            using (AuditCorrelationScope.BeginIfAbsent(AuditCorrelationScope.ForIncident(Guid.Empty)))
                Assert.Equal("EscalationProcessingJob:fire-1", AuditCorrelationScope.CorrelationId);
        }
    }

    [Fact]
    public void BeginIfAbsent_NamesTheOperation_WhenNothingElseDoes_AndEndsWithIt()
    {
        var id = Guid.Parse("2f60ff6b-1c3a-4f2e-9c31-6b0a2d5e7f10");

        using (AuditCorrelationScope.BeginIfAbsent(AuditCorrelationScope.ForIncident(id)))
            Assert.Equal("incident-2f60ff6b1c3a4f2e9c316b0a2d5e7f10", AuditCorrelationScope.CorrelationId);

        Assert.Null(AuditCorrelationScope.CorrelationId);
    }
}
