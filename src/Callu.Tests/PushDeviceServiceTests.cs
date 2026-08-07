using Callu.Application.Common.Interfaces.Persistence;
using Callu.Domain.Entities;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Infrastructure.Services;
using Callu.Shared.Models.Devices;
using NSubstitute;

namespace Callu.Tests;

public class PushDeviceServiceTests
{
    private readonly IUserPushDeviceRepository _repo = Substitute.For<IUserPushDeviceRepository>();
    private readonly ITransactionManager _tx = Substitute.For<ITransactionManager>();

    public PushDeviceServiceTests()
    {
        _tx.ExecuteInTransactionAsync(Arg.Any<Func<Task<PushDeviceDto>>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Func<Task<PushDeviceDto>>>()());
        _tx.ExecuteInTransactionAsync(Arg.Any<Func<Task<bool>>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Func<Task<bool>>>()());
    }

    private PushDeviceService Sut() => new(_repo, _tx);

    [Fact]
    public async Task Register_CreatesNewDevice()
    {
        _repo.GetByTokenAsync("tok-1", Arg.Any<CancellationToken>()).Returns((UserPushDevice?)null);
        _repo.GetActiveByUserIdAsync("u1", Arg.Any<CancellationToken>()).Returns([]);

        var dto = await Sut().RegisterAsync("u1", new RegisterPushDeviceRequest
        {
            Platform = "iOS",
            PushToken = "tok-1"
        });

        Assert.Equal("ios", dto.Platform);
        await _repo.Received(1).AddAsync(Arg.Is<UserPushDevice>(d =>
            d.UserId == "u1" && d.PushToken == "tok-1" && d.Platform == "ios"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Register_ReassignsExistingTokenToCurrentUser()
    {
        var existing = new UserPushDevice
        {
            Id = Guid.NewGuid(),
            UserId = "other",
            Platform = "android",
            PushToken = "tok-1",
            LastSeenAt = DateTime.UtcNow.AddDays(-1)
        };
        _repo.GetByTokenAsync("tok-1", Arg.Any<CancellationToken>()).Returns(existing);

        var dto = await Sut().RegisterAsync("u1", new RegisterPushDeviceRequest
        {
            Platform = "android",
            PushToken = "tok-1"
        });

        Assert.Equal("u1", existing.UserId);
        Assert.Equal(existing.Id, dto.Id);
        await _repo.DidNotReceive().AddAsync(Arg.Any<UserPushDevice>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Register_EvictsOldestWhenAtCap()
    {
        var owned = AtCap("u1");

        await Sut().RegisterAsync("u1", new RegisterPushDeviceRequest
        {
            Platform = "web",
            PushToken = "new-tok"
        });

        _repo.Received(1).Delete(owned[^1]);
        foreach (var kept in owned[..^1])
            _repo.DidNotReceive().Delete(kept);
        await _repo.Received(1).AddAsync(Arg.Any<UserPushDevice>(), Arg.Any<CancellationToken>());
    }

    // A soft delete is not flushed until the transaction commits, so a count re-read after each
    // delete never falls below the cap: the eviction has to come from one ordered read.
    [Fact]
    public async Task Register_ReadsTheDeviceListOnceWhenEvicting()
    {
        AtCap("u1");

        await Sut().RegisterAsync("u1", new RegisterPushDeviceRequest
        {
            Platform = "web",
            PushToken = "new-tok"
        });

        await _repo.Received(1).GetActiveByUserIdAsync("u1", Arg.Any<CancellationToken>());
    }

    /// <summary>A user holding the maximum number of devices, most recently seen first.</summary>
    private List<UserPushDevice> AtCap(string userId)
    {
        var owned = Enumerable.Range(0, UserPushDevice.MaxDevicesPerUser)
            .Select(i => new UserPushDevice
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                PushToken = $"tok-{i}",
                LastSeenAt = DateTime.UtcNow.AddMinutes(-i)
            })
            .ToList();

        _repo.GetByTokenAsync("new-tok", Arg.Any<CancellationToken>()).Returns((UserPushDevice?)null);
        _repo.GetActiveByUserIdAsync(userId, Arg.Any<CancellationToken>()).Returns(owned);
        return owned;
    }

    [Fact]
    public async Task Unregister_WithToken_DeletesOnlyMatchingOwnedDevice()
    {
        var owned = new UserPushDevice { UserId = "u1", PushToken = "tok" };
        _repo.GetByTokenAsync("tok", Arg.Any<CancellationToken>()).Returns(owned);

        await Sut().UnregisterAsync("u1", new UnregisterPushDeviceRequest { PushToken = "tok" });

        _repo.Received(1).Delete(owned);
    }

    [Fact]
    public async Task Unregister_WithoutToken_DeletesAllForUser()
    {
        var devices = new List<UserPushDevice>
        {
            new() { UserId = "u1", PushToken = "a" },
            new() { UserId = "u1", PushToken = "b" }
        };
        _repo.GetActiveByUserIdAsync("u1", Arg.Any<CancellationToken>()).Returns(devices);

        await Sut().UnregisterAsync("u1", null);

        _repo.Received(1).Delete(devices[0]);
        _repo.Received(1).Delete(devices[1]);
    }
}
