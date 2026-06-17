using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Callu.Tests;

/// <summary>
/// AUTHZ-1 (IDOR): marking a notification read must be scoped to the owning user, so a caller
/// cannot flip another user's bell state by guessing its id.
/// </summary>
public class NotificationMarkAsReadOwnershipTests
{
    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"markread-{Guid.NewGuid():N}")
            .Options);

    private static NotificationRepository Repo(ApplicationDbContext ctx) =>
        new(ctx, NullLogger<NotificationRepository>.Instance);

    private static Notification Notif(string userId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Type = NotificationType.Email,
        Title = "Database unreachable",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    [Fact]
    public async Task Owner_Marks_Read_Returns_True_And_Persists()
    {
        using var ctx = NewContext();
        var repo = Repo(ctx);
        var n = Notif("user-1");
        ctx.Notifications.Add(n);
        await ctx.SaveChangesAsync();

        var ok = await repo.MarkAsReadAsync(n.Id, "user-1");
        await ctx.SaveChangesAsync();

        Assert.True(ok);
        Assert.True((await ctx.Notifications.FindAsync(n.Id))!.IsRead);
    }

    [Fact]
    public async Task NonOwner_Cannot_Mark_Read()
    {
        using var ctx = NewContext();
        var repo = Repo(ctx);
        var n = Notif("victim");
        ctx.Notifications.Add(n);
        await ctx.SaveChangesAsync();

        var ok = await repo.MarkAsReadAsync(n.Id, "attacker");
        await ctx.SaveChangesAsync();

        Assert.False(ok);
        Assert.False((await ctx.Notifications.FindAsync(n.Id))!.IsRead);
    }

    [Fact]
    public async Task Unknown_Id_Returns_False()
    {
        using var ctx = NewContext();
        var repo = Repo(ctx);

        Assert.False(await repo.MarkAsReadAsync(Guid.NewGuid(), "user-1"));
    }
}
