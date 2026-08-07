using Callu.Infrastructure.Audit;

namespace Callu.Tests;

/// <summary>One sweep run is one logical operation, however many transactions it takes.</summary>
public class AuditCorrelationScopeTests
{
    [Fact]
    public void OutsideAnyScope_ThereIsNoCorrelationId()
    {
        Assert.Null(AuditCorrelationScope.CorrelationId);
    }

    [Fact]
    public void EverythingInsideOneScopeSharesItsId()
    {
        using (AuditCorrelationScope.Begin("nightly-sweep:42"))
        {
            Assert.Equal("nightly-sweep:42", AuditCorrelationScope.CorrelationId);
        }

        Assert.Null(AuditCorrelationScope.CorrelationId);
    }

    /// <summary>A nested scope restores the one it interrupted, rather than clearing it.</summary>
    [Fact]
    public void ANestedScopeRestoresTheOuterOne()
    {
        using (AuditCorrelationScope.Begin("outer"))
        {
            using (AuditCorrelationScope.Begin("inner"))
                Assert.Equal("inner", AuditCorrelationScope.CorrelationId);

            Assert.Equal("outer", AuditCorrelationScope.CorrelationId);
        }
    }

    /// <summary>The scope has to follow the work, which is asynchronous and moves between threads.</summary>
    [Fact]
    public async Task TheScopeSurvivesAnAwait()
    {
        using (AuditCorrelationScope.Begin("sweep-1"))
        {
            await Task.Yield();
            await Task.Delay(1);

            Assert.Equal("sweep-1", AuditCorrelationScope.CorrelationId);
        }
    }

    /// <summary>Two runs at once must not read each other's id.</summary>
    [Fact]
    public async Task ConcurrentRunsDoNotSeeEachOther()
    {
        async Task<string?> Run(string id)
        {
            using (AuditCorrelationScope.Begin(id))
            {
                await Task.Delay(5);
                return AuditCorrelationScope.CorrelationId;
            }
        }

        var results = await Task.WhenAll(Run("a"), Run("b"), Run("c"));

        Assert.Equal(new[] { "a", "b", "c" }, results);
    }

    [Fact]
    public void DisposingTwiceDoesNotUndoALaterScope()
    {
        var scope = AuditCorrelationScope.Begin("first");
        scope.Dispose();

        using (AuditCorrelationScope.Begin("second"))
        {
            scope.Dispose();
            Assert.Equal("second", AuditCorrelationScope.CorrelationId);
        }
    }
}
