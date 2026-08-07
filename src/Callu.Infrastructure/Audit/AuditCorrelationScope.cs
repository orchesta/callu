namespace Callu.Infrastructure.Audit;

/// <summary>The logical operation the audit rows written inside this scope belong to.</summary>
// Ambient rather than a parameter: every write inside the scope should carry it, and threading it
// through each call site is how one gets forgotten.
public static class AuditCorrelationScope
{
    private static readonly AsyncLocal<string?> Current = new();

    /// <summary>What to record as the logical operation, or null outside any scope.</summary>
    public static string? CorrelationId => Current.Value;

    /// <summary>Names the operation until the returned handle is disposed.</summary>
    public static IDisposable Begin(string correlationId)
    {
        var previous = Current.Value;
        Current.Value = correlationId;
        return new Scope(previous);
    }

    private sealed class Scope(string? previous) : IDisposable
    {
        private bool _restored;

        public void Dispose()
        {
            if (_restored) return;
            _restored = true;
            Current.Value = previous;
        }
    }
}
