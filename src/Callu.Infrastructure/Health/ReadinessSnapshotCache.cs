namespace Callu.Infrastructure.Health;

public sealed class ReadinessSnapshotCache
{
    public readonly record struct Snapshot(bool Database, bool Cache, bool? Broker);

    private readonly TimeSpan _ttl;
    private readonly Func<DateTimeOffset> _now;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private Snapshot _cached;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public ReadinessSnapshotCache() : this(TimeSpan.FromSeconds(5), () => DateTimeOffset.UtcNow)
    {
    }

    internal ReadinessSnapshotCache(TimeSpan ttl, Func<DateTimeOffset> now)
    {
        _ttl = ttl;
        _now = now;
    }

    public async Task<Snapshot> GetAsync(Func<Task<Snapshot>> probe, CancellationToken ct)
    {
        if (_now() < _expiresAt)
            return _cached;

        await _gate.WaitAsync(ct);
        try
        {
            if (_now() < _expiresAt)
                return _cached;

            _cached = await probe();
            _expiresAt = _now() + _ttl;
            return _cached;
        }
        finally
        {
            _gate.Release();
        }
    }
}
