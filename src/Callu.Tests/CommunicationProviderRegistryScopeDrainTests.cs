using Callu.Infrastructure.Configuration;
using Callu.Infrastructure.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Callu.Tests;

/// <summary>A retired provider scope is disposed only after a grace window, so an in-flight call keeps its DbContext.</summary>
public class CommunicationProviderRegistryScopeDrainTests
{
    private sealed class Probe : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private static (CommunicationProviderRegistry Registry, IServiceProvider Sp) NewRegistry()
    {
        var sp = new ServiceCollection().AddScoped<Probe>().BuildServiceProvider();
        var registry = new CommunicationProviderRegistry(
            sp,
            Options.Create(new CommunicationSettingsOptions()),
            NullLogger<CommunicationProviderRegistry>.Instance);
        return (registry, sp);
    }

    private static (IServiceScope Scope, Probe Probe) NewTrackedScope(IServiceProvider sp)
    {
        var scope = sp.CreateScope();
        return (scope, scope.ServiceProvider.GetRequiredService<Probe>());
    }

    [Fact]
    public void RetiredScope_IsNotDisposed_WithinGrace()
    {
        var (registry, sp) = NewRegistry();
        var (scope, probe) = NewTrackedScope(sp);
        var t0 = DateTimeOffset.UnixEpoch;

        registry.RetireScopes(new[] { scope }, t0);

        Assert.Equal(0, registry.DrainRetiredScopes(t0));
        Assert.Equal(0, registry.DrainRetiredScopes(t0 + TimeSpan.FromSeconds(30)));
        Assert.False(probe.Disposed);
    }

    [Fact]
    public void RetiredScope_IsDisposed_PastGrace_AndDrainIsIdempotent()
    {
        var (registry, sp) = NewRegistry();
        var (scope, probe) = NewTrackedScope(sp);
        var t0 = DateTimeOffset.UnixEpoch;

        registry.RetireScopes(new[] { scope }, t0);

        var disposed = registry.DrainRetiredScopes(t0 + CommunicationProviderRegistry.ScopeDrainGrace + TimeSpan.FromSeconds(1));

        Assert.Equal(1, disposed);
        Assert.True(probe.Disposed);
        Assert.Equal(0, registry.DrainRetiredScopes(t0 + TimeSpan.FromHours(1)));
    }

    [Fact]
    public void Drain_IsFifo_DisposingOnlyScopesPastTheirOwnGrace()
    {
        var (registry, sp) = NewRegistry();
        var (scopeA, probeA) = NewTrackedScope(sp);
        var (scopeB, probeB) = NewTrackedScope(sp);
        var t0 = DateTimeOffset.UnixEpoch;

        registry.RetireScopes(new[] { scopeA }, t0);
        registry.RetireScopes(new[] { scopeB }, t0 + TimeSpan.FromSeconds(40));

        registry.DrainRetiredScopes(t0 + TimeSpan.FromSeconds(61));

        Assert.True(probeA.Disposed);
        Assert.False(probeB.Disposed);
    }
}
