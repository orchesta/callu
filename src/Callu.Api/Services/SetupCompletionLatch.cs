namespace Callu.Api.Services;

/// <summary>
/// One-way latch recording that initial setup has completed; "not yet configured" is never cached.
/// </summary>
public sealed class SetupCompletionLatch
{
    private volatile bool _complete;

    public bool IsComplete => _complete;

    public void MarkComplete() => _complete = true;
}
