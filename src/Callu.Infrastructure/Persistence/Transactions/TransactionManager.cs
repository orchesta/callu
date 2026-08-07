using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Persistence.Transactions;

/// <summary>Centrally manages transactions; the operation runs exactly once, and transient failures
/// surface to the caller rather than being replayed on the shared context.</summary>
public class TransactionManager : ITransactionManager
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<TransactionManager> _logger;

    /// <summary>
    /// Creates a new TransactionManager instance
    /// </summary>
    public TransactionManager(ApplicationDbContext context, ILogger<TransactionManager> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes the specified operation within a transaction and returns the result.
    /// If a transaction already exists, does not start a new transaction.
    /// </summary>
    public async Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        if (_context.Database.CurrentTransaction != null)
        {
            _logger.LogDebug("Using existing transaction");
            return await operation();
        }

        _logger.LogDebug("Starting new transaction");

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await operation();
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            _logger.LogDebug("Transaction completed successfully");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Rolling back transaction");
            await RollbackAsync(transaction);
            throw;
        }
    }

    /// <summary>
    /// Executes the specified operation within a transaction.
    /// If a transaction already exists, does not start a new transaction.
    /// </summary>
    public async Task ExecuteInTransactionAsync(Func<Task> operation, CancellationToken cancellationToken = default)
    {
        await ExecuteInTransactionAsync(async () =>
        {
            await operation();
            return true;
        }, cancellationToken);
    }

    /// <summary>
    /// Checks if there is an active transaction
    /// </summary>
    public bool IsInTransaction()
    {
        return _context.Database.CurrentTransaction != null;
    }

    /// <summary>
    /// Rolls back, then hands the scope back in a state the next transaction on it can trust.
    /// The rollback is not bound to the caller's token — a cancelled request must still unwind.
    /// </summary>
    private async Task RollbackAsync(IDbContextTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transaction rollback failed");
        }

        ResetTrackerAfterRollback();
    }

    /// <summary>Drops the failed attempt's tracked entities, so the next transaction on the scope
    /// does not flush rows the database already rolled back.</summary>
    private void ResetTrackerAfterRollback() => _context.AfterRollback();
}
