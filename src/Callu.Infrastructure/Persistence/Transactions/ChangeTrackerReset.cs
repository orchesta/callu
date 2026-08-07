using Microsoft.EntityFrameworkCore;

namespace Callu.Infrastructure.Persistence.Transactions;

/// <summary>What has to happen to the scoped DbContext's change tracker after a transaction on it rolls
/// back; every component that opens a transaction on that shared context must call it.</summary>
internal static class ChangeTrackerReset
{
    /// <summary>Drops the failed attempt's tracked entities, including any message it staged.</summary>
    public static void AfterRollback(this DbContext context) => context.ChangeTracker.Clear();
}
