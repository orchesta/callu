using Callu.Infrastructure.Health;

namespace Callu.Tests;

public class ReadinessSnapshotCacheTests
{
    [Fact]
    public async Task Collapses_concurrent_and_repeat_calls_to_one_probe_within_ttl()
    {
        var now = DateTimeOffset.UnixEpoch;
        var cache = new ReadinessSnapshotCache(TimeSpan.FromSeconds(5), () => now);
        var probes = 0;

        Task<ReadinessSnapshotCache.Snapshot> Probe() =>
            cache.GetAsync(() =>
            {
                probes++;
                return Task.FromResult(new ReadinessSnapshotCache.Snapshot(true, true, null));
            }, CancellationToken.None);

        await Probe();
        await Probe();
        await Probe();

        Assert.Equal(1, probes);
    }

    [Fact]
    public async Task Reprobes_after_ttl_expires()
    {
        var now = DateTimeOffset.UnixEpoch;
        var cache = new ReadinessSnapshotCache(TimeSpan.FromSeconds(5), () => now);
        var probes = 0;

        Task<ReadinessSnapshotCache.Snapshot> Probe() =>
            cache.GetAsync(() =>
            {
                probes++;
                return Task.FromResult(new ReadinessSnapshotCache.Snapshot(true, true, null));
            }, CancellationToken.None);

        await Probe();
        now = now.AddSeconds(6);
        await Probe();

        Assert.Equal(2, probes);
    }
}
