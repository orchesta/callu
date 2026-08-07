using Callu.Domain.Entities;

namespace Callu.Infrastructure.Audit;

/// <summary>Somewhere an audit entry is copied to, alongside the table that holds it.</summary>
public interface IAuditSink
{
    /// <summary>Must not throw and must not block the caller: the entry is already written.</summary>
    void Emit(AuditLog entry);
}
