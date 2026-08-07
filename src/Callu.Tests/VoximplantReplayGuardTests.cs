using Callu.Application.Services;
using Callu.Infrastructure.Providers.Voximplant;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace Callu.Tests;

/// <summary>
/// VOX-1: replay protection is backed by IDistributedCache so nonce state is shared across
/// replicas. A (timestamp, nonce) pair must be accepted once and rejected on replay.
/// </summary>
public class VoximplantReplayGuardTests
{
    private sealed class FakeDistributedCache : IDistributedCache
    {
        private readonly Dictionary<string, byte[]> _store = new();
        public byte[]? Get(string key) => _store.TryGetValue(key, out var v) ? v : null;
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => _store[key] = value;
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        { Set(key, value, options); return Task.CompletedTask; }
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) => _store.Remove(key);
        public Task RemoveAsync(string key, CancellationToken token = default) { _store.Remove(key); return Task.CompletedTask; }
    }

    private static VoximplantReplayGuard NewGuard() =>
        new(new FakeDistributedCache(), Options.Create(new VoximplantReplayGuardOptions { WindowSeconds = 300 }));

    [Fact]
    public void Accepts_First_Use_Rejects_Replay()
    {
        var guard = NewGuard();

        Assert.True(guard.TryRegister(123456, "nonce-1"));
        Assert.False(guard.TryRegister(123456, "nonce-1"));
        Assert.True(guard.TryRegister(123456, "nonce-2"));
        Assert.True(guard.TryRegister(123457, "nonce-1"));
    }

    [Fact]
    public void Rejects_Empty_Nonce()
    {
        var guard = NewGuard();
        Assert.False(guard.TryRegister(123456, ""));
        Assert.False(guard.TryRegister(123456, "   "));
    }
}
