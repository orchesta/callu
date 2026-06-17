using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Callu.Domain.Entities;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Infrastructure.Persistence.Repositories;

namespace Callu.Infrastructure.Persistence.UnitOfWork;

/// <summary>
/// Unit of Work implementation with ExecutionStrategy and concurrency support
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _context;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<UnitOfWork> _logger;
    private IDbContextTransaction? _transaction;
    private bool _disposed;

    private IRepository<Incident>? _incidents;
    private IRepository<Team>? _teams;
    private IRepository<TeamMember>? _teamMembers;
    private IRepository<Service>? _services;
    private IRepository<Schedule>? _schedules;
    private IRepository<ScheduleRotation>? _scheduleRotations;
    private IRepository<EscalationPolicy>? _escalationPolicies;
    private IRepository<EscalationStep>? _escalationSteps;

    public UnitOfWork(ApplicationDbContext context, ILoggerFactory loggerFactory)
    {
        _context = context;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<UnitOfWork>();
    }

    #region Repositories

    public IRepository<Incident> Incidents => 
        _incidents ??= new Repository<Incident>(_context, _loggerFactory.CreateLogger<Repository<Incident>>());

    public IRepository<Team> Teams => 
        _teams ??= new Repository<Team>(_context, _loggerFactory.CreateLogger<Repository<Team>>());

    public IRepository<TeamMember> TeamMembers => 
        _teamMembers ??= new Repository<TeamMember>(_context, _loggerFactory.CreateLogger<Repository<TeamMember>>());

    public IRepository<Service> Services => 
        _services ??= new Repository<Service>(_context, _loggerFactory.CreateLogger<Repository<Service>>());

    public IRepository<Schedule> Schedules => 
        _schedules ??= new Repository<Schedule>(_context, _loggerFactory.CreateLogger<Repository<Schedule>>());

    public IRepository<ScheduleRotation> ScheduleRotations => 
        _scheduleRotations ??= new Repository<ScheduleRotation>(_context, _loggerFactory.CreateLogger<Repository<ScheduleRotation>>());

    public IRepository<EscalationPolicy> EscalationPolicies => 
        _escalationPolicies ??= new Repository<EscalationPolicy>(_context, _loggerFactory.CreateLogger<Repository<EscalationPolicy>>());

    public IRepository<EscalationStep> EscalationSteps => 
        _escalationSteps ??= new Repository<EscalationStep>(_context, _loggerFactory.CreateLogger<Repository<EscalationStep>>());

    #endregion

    #region Basic Operations

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict detected while saving changes");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while saving changes");
            throw;
        }
    }

    #endregion

    #region Transaction Management

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction != null)
        {
            throw new InvalidOperationException("A transaction is already in progress");
        }

        _transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        _logger.LogDebug("Transaction started");
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction == null)
        {
            throw new InvalidOperationException("No transaction in progress");
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            await _transaction.CommitAsync(cancellationToken);
            _logger.LogDebug("Transaction committed");
        }
        finally
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction == null)
        {
            throw new InvalidOperationException("No transaction in progress");
        }

        try
        {
            await _transaction.RollbackAsync(cancellationToken);
            _logger.LogDebug("Transaction rolled back");
        }
        finally
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public bool IsInTransaction() => _transaction != null || _context.Database.CurrentTransaction != null;

    #endregion

    #region Dispose

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }


    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _transaction?.Dispose();
            _context.Dispose();
            _disposed = true;
        }
    }

    #endregion
}
