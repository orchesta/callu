using Callu.Application.Common.Interfaces.Persistence;
using Callu.Domain.Entities;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Infrastructure.Services;
using Callu.Shared.Exceptions;
using Callu.Shared.Models.Maintenance;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Callu.Tests;

public class MaintenanceWindowUpdateTests
{
    private sealed class PassthroughTx : ITransactionManager
    {
        public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> operation, CancellationToken cancellationToken = default)
            => operation();

        public Task ExecuteInTransactionAsync(Func<Task> operation, CancellationToken cancellationToken = default)
            => operation();

        public bool IsInTransaction() => false;
    }

    private static MaintenanceWindowService Build(
        IRepository<MaintenanceWindow> repo,
        IValidator<UpdateMaintenanceWindowRequest>? updateValidator = null)
    {
        var createValidator = Substitute.For<IValidator<CreateMaintenanceWindowRequest>>();
        createValidator.ValidateAsync(Arg.Any<CreateMaintenanceWindowRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());

        var upd = updateValidator ?? Substitute.For<IValidator<UpdateMaintenanceWindowRequest>>();
        if (updateValidator is null)
        {
            upd.ValidateAsync(Arg.Any<UpdateMaintenanceWindowRequest>(), Arg.Any<CancellationToken>())
                .Returns(new ValidationResult());
        }

        return new MaintenanceWindowService(
            repo,
            new PassthroughTx(),
            createValidator,
            upd,
            Substitute.For<Callu.Application.Services.IAuditLogService>(),
            Substitute.For<Callu.Application.Common.Interfaces.ICurrentUserService>(),
            NullLogger<MaintenanceWindowService>.Instance);
    }

    [Fact]
    public async Task Update_ExtendsEndsAt_WithoutCancelling()
    {
        var id = Guid.NewGuid();
        var entity = new MaintenanceWindow
        {
            Id = id,
            Title = "DB patch",
            StartsAt = DateTime.UtcNow.AddMinutes(-30),
            EndsAt = DateTime.UtcNow.AddMinutes(30),
            AppliesToAllServices = true,
            AffectedServiceIdsJson = "[]",
            Mode = MaintenanceWindowMode.SuppressAlerts,
        };
        var repo = Substitute.For<IRepository<MaintenanceWindow>>();
        repo.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(entity);

        var sut = Build(repo);
        var newEnd = DateTime.UtcNow.AddHours(3);
        var result = await sut.UpdateAsync(id, new UpdateMaintenanceWindowRequest
        {
            Title = "DB patch",
            StartsAt = entity.StartsAt,
            EndsAt = newEnd,
            AppliesToAllServices = true,
            Mode = "SuppressAlerts",
        });

        Assert.NotNull(result);
        Assert.Equal(newEnd, entity.EndsAt);
        Assert.False(entity.IsCancelled);
        Assert.Equal(entity.StartsAt, result!.StartsAt);
    }

    [Fact]
    public async Task Update_CancelledWindow_ThrowsConflict()
    {
        var id = Guid.NewGuid();
        var entity = new MaintenanceWindow
        {
            Id = id,
            Title = "Done",
            StartsAt = DateTime.UtcNow.AddHours(-2),
            EndsAt = DateTime.UtcNow.AddHours(1),
            IsCancelled = true,
            AppliesToAllServices = true,
            AffectedServiceIdsJson = "[]",
        };
        var repo = Substitute.For<IRepository<MaintenanceWindow>>();
        repo.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(entity);

        var sut = Build(repo);
        await Assert.ThrowsAsync<ConflictException>(() => sut.UpdateAsync(id, new UpdateMaintenanceWindowRequest
        {
            Title = "Done",
            StartsAt = entity.StartsAt,
            EndsAt = DateTime.UtcNow.AddHours(2),
            AppliesToAllServices = true,
            Mode = "SuppressAlerts",
        }));
    }

    [Fact]
    public async Task Update_AfterStart_KeepsOriginalStartsAt()
    {
        var id = Guid.NewGuid();
        var originalStart = DateTime.UtcNow.AddHours(-1);
        var entity = new MaintenanceWindow
        {
            Id = id,
            Title = "Live",
            StartsAt = originalStart,
            EndsAt = DateTime.UtcNow.AddHours(1),
            AppliesToAllServices = true,
            AffectedServiceIdsJson = "[]",
        };
        var repo = Substitute.For<IRepository<MaintenanceWindow>>();
        repo.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(entity);

        var sut = Build(repo);
        await sut.UpdateAsync(id, new UpdateMaintenanceWindowRequest
        {
            Title = "Live",
            StartsAt = DateTime.UtcNow.AddHours(1),
            EndsAt = DateTime.UtcNow.AddHours(4),
            AppliesToAllServices = true,
            Mode = "SuppressAlerts",
        });

        Assert.Equal(originalStart, entity.StartsAt);
    }
}
