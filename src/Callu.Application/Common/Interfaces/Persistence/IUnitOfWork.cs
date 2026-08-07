using Callu.Domain.Entities;

namespace Callu.Application.Common.Interfaces.Persistence;

/// <summary>
/// Unit of Work interface
/// </summary>
public interface IUnitOfWork : IDisposable
{
    IRepository<Incident> Incidents { get; }
    IRepository<Team> Teams { get; }
    IRepository<TeamMember> TeamMembers { get; }
    IRepository<Service> Services { get; }
    IRepository<Schedule> Schedules { get; }
    IRepository<ScheduleRotation> ScheduleRotations { get; }
    IRepository<EscalationPolicy> EscalationPolicies { get; }
    IRepository<EscalationStep> EscalationSteps { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    Task BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);
    bool IsInTransaction();
}

