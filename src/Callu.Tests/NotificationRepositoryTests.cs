using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Callu.Tests;

/// <summary>AddAsync is idempotent on DedupeKey, so a duplicate page cannot roll back the whole batch.</summary>
public class NotificationRepositoryTests
{
    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"h5-{Guid.NewGuid():N}")
            .Options);

    private static NotificationRepository Repo(ApplicationDbContext ctx) =>
        new(ctx, NullLogger<NotificationRepository>.Instance);

    private static Notification Notif(string? dedupeKey, string userId = "user-1") => new()
    {
        Id = Guid.NewGuid(),
        IncidentId = Guid.NewGuid(),
        UserId = userId,
        Type = NotificationType.Email,
        Title = "Database unreachable",
        Message = "Escalation",
        DedupeKey = dedupeKey,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task AddAsync_SkipsDuplicateKey_PendingInSameUnitOfWork()
    {
        using var ctx = NewContext();
        var repo = Repo(ctx);

        await repo.AddAsync(Notif("KEY-A"));
        await repo.AddAsync(Notif("KEY-A"));
        await ctx.SaveChangesAsync();

        Assert.Equal(1, await ctx.Notifications.CountAsync(n => n.DedupeKey == "KEY-A"));
    }

    [Fact]
    public async Task AddAsync_SkipsDuplicateKey_AlreadyPersisted()
    {
        using var ctx = NewContext();
        var repo = Repo(ctx);

        await repo.AddAsync(Notif("KEY-B"));
        await ctx.SaveChangesAsync();

        await repo.AddAsync(Notif("KEY-B"));
        await ctx.SaveChangesAsync();

        Assert.Equal(1, await ctx.Notifications.CountAsync(n => n.DedupeKey == "KEY-B"));
    }

    [Fact]
    public async Task AddAsync_KeepsDistinctKeys()
    {
        using var ctx = NewContext();
        var repo = Repo(ctx);

        await repo.AddAsync(Notif("KEY-C1"));
        await repo.AddAsync(Notif("KEY-C2"));
        await ctx.SaveChangesAsync();

        Assert.Equal(2, await ctx.Notifications.CountAsync());
    }

    [Fact]
    public async Task AddAsync_NeverSkips_WhenDedupeKeyNullOrEmpty()
    {
        using var ctx = NewContext();
        var repo = Repo(ctx);

        await repo.AddAsync(Notif(null));
        await repo.AddAsync(Notif(null));
        await repo.AddAsync(Notif(string.Empty));
        await ctx.SaveChangesAsync();

        Assert.Equal(3, await ctx.Notifications.CountAsync());
    }

    [Fact]
    public async Task AddAsync_DuplicateKey_DoesNotRollBackTheRestOfTheBatch()
    {
        using var ctx = NewContext();
        var repo = Repo(ctx);

        await repo.AddAsync(Notif("DUP", userId: "a"));
        await repo.AddAsync(Notif("DUP", userId: "b"));
        await repo.AddAsync(Notif("UNIQUE", userId: "c"));
        await ctx.SaveChangesAsync();

        Assert.Equal(1, await ctx.Notifications.CountAsync(n => n.DedupeKey == "DUP"));
        Assert.Equal(1, await ctx.Notifications.CountAsync(n => n.DedupeKey == "UNIQUE"));
    }
}
