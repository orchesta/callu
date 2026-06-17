using System.Diagnostics.Metrics;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Transactions;

namespace Callu.Tests;

/// <summary>
/// Minimal <see cref="IMeterFactory"/> so the (sealed) CalluMetrics can be constructed in
/// tests without a DI container. The metrics surface isn't exercised by the paths under test.
/// </summary>
internal sealed class FakeMeterFactory : IMeterFactory
{
    public Meter Create(MeterOptions options) => new(options);
    public void Dispose() { }
}

/// <summary>
/// Runs the operation inline with no real transaction — lets us unit-test services that wrap
/// work in <see cref="ITransactionManager"/> without a database.
/// </summary>
internal sealed class ImmediateTransactionManager : ITransactionManager
{
    public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> operation, CancellationToken cancellationToken = default)
        => operation();

    public Task ExecuteInTransactionAsync(Func<Task> operation, CancellationToken cancellationToken = default)
        => operation();

    public bool IsInTransaction() => false;

    public Task<IDisposable> BeginTransactionScopeAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IDisposable>(new NoOp());

    private sealed class NoOp : IDisposable { public void Dispose() { } }
}

/// <summary>
/// Like <see cref="ImmediateTransactionManager"/> but also flushes via SaveChanges — mirrors the
/// real TransactionManager's "run work → SaveChanges → commit" so orchestrator state changes get
/// persisted to the EF in-memory store (which does not support real transactions).
/// </summary>
internal sealed class SavingTransactionManager(ApplicationDbContext context) : ITransactionManager
{
    public async Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> operation, CancellationToken cancellationToken = default)
    {
        var result = await operation();
        await context.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task ExecuteInTransactionAsync(Func<Task> operation, CancellationToken cancellationToken = default)
    {
        await operation();
        await context.SaveChangesAsync(cancellationToken);
    }

    public bool IsInTransaction() => false;

    public Task<IDisposable> BeginTransactionScopeAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IDisposable>(new NoOp());

    private sealed class NoOp : IDisposable { public void Dispose() { } }
}
