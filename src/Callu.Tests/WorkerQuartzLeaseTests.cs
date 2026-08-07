using Callu.Worker.Hosting;
using NSubstitute;
using StackExchange.Redis;

namespace Callu.Tests;

public class WorkerQuartzLeaseTests
{
    [Fact]
    public async Task EmptyKey_IsAcquiredByTheFirstCaller()
    {
        var db = Substitute.For<IDatabase>();
        db.StringGetAsync(WorkerQuartzReplicaGuard.LeaseKey).Returns(RedisValue.Null);
        db.StringSetAsync(WorkerQuartzReplicaGuard.LeaseKey, "a", Arg.Any<TimeSpan?>(), When.NotExists)
            .Returns(true);

        var result = await WorkerQuartzLease.RenewAsync(db, "a", TimeSpan.FromSeconds(30));

        Assert.True(result.HeldByUs);
        Assert.Null(result.OtherHolder);
    }

    [Fact]
    public async Task OtherHolder_IsReportedWithoutStealingTheLease()
    {
        var db = Substitute.For<IDatabase>();
        db.StringGetAsync(WorkerQuartzReplicaGuard.LeaseKey).Returns((RedisValue)"other");

        var result = await WorkerQuartzLease.RenewAsync(db, "me", TimeSpan.FromSeconds(30));

        Assert.False(result.HeldByUs);
        Assert.Equal("other", result.OtherHolder);
        await db.DidNotReceive().StringSetAsync(
            Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(), Arg.Any<When>());
    }

    [Fact]
    public async Task OwnLease_IsRefreshed()
    {
        var db = Substitute.For<IDatabase>();
        db.StringGetAsync(WorkerQuartzReplicaGuard.LeaseKey).Returns((RedisValue)"me");
        db.KeyExpireAsync(WorkerQuartzReplicaGuard.LeaseKey, Arg.Any<TimeSpan?>()).Returns(true);

        var result = await WorkerQuartzLease.RenewAsync(db, "me", TimeSpan.FromSeconds(30));

        Assert.True(result.HeldByUs);
        await db.Received(1).KeyExpireAsync(WorkerQuartzReplicaGuard.LeaseKey, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Release_DeletesOnlyWhenOwned()
    {
        var db = Substitute.For<IDatabase>();
        db.StringGetAsync(WorkerQuartzReplicaGuard.LeaseKey).Returns((RedisValue)"me");

        await WorkerQuartzLease.ReleaseIfOwnedAsync(db, "me");

        await db.Received(1).KeyDeleteAsync(WorkerQuartzReplicaGuard.LeaseKey);
    }

    [Fact]
    public async Task Release_LeavesSomeoneElsesLeaseAlone()
    {
        var db = Substitute.For<IDatabase>();
        db.StringGetAsync(WorkerQuartzReplicaGuard.LeaseKey).Returns((RedisValue)"other");

        await WorkerQuartzLease.ReleaseIfOwnedAsync(db, "me");

        await db.DidNotReceive().KeyDeleteAsync(Arg.Any<RedisKey>());
    }
}
