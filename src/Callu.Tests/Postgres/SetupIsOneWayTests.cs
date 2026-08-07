using Callu.Domain.Entities;
using Callu.Infrastructure.Identity;
using Callu.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Callu.Tests;

[Collection(PostgresCollection.Name)]
public class SetupIsOneWayTests(PostgresFixture pg)
{
    private const string AdminRole = "Admin";

    private static Task<bool> AdminExists_Filtered(ApplicationDbContext db) =>
        (from u in db.Users
         join ur in db.UserRoles on u.Id equals ur.UserId
         join r in db.Roles on ur.RoleId equals r.Id
         where r.Name == AdminRole
         select u.Id).AnyAsync();

    private static async Task<bool> SetupComplete(ApplicationDbContext db)
    {
        var adminExists = await (from u in db.Users.IgnoreQueryFilters()
                                 join ur in db.UserRoles on u.Id equals ur.UserId
                                 join r in db.Roles on ur.RoleId equals r.Id
                                 where r.Name == AdminRole
                                 select u.Id).AnyAsync();

        return adminExists
            || await db.OrganizationSettings.AnyAsync(s => s.Id == OrganizationSettings.SingletonId);
    }

    [PostgresFact]
    public async Task AFreshDatabase_StillReportsSetupAsIncomplete()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        await using var db = PostgresFixture.Context(cs);

        Assert.False(await SetupComplete(db));
    }

    [PostgresFact]
    public async Task SoftDeletingTheLastAdmin_DoesNotReopenSetup()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);
        await SeedAdminAsync(cs, softDeleted: false, withSettingsRow: true);

        await using (var db = PostgresFixture.Context(cs))
        {
            Assert.True(await SetupComplete(db));
        }

        await SoftDeleteEveryUserAsync(cs);

        await using (var db = PostgresFixture.Context(cs))
        {
            Assert.False(await AdminExists_Filtered(db),
                "the soft-delete filter is what hid the administrator — if this ever fails the test no longer covers the defect");

            Assert.True(await SetupComplete(db));
        }
    }

    [PostgresFact]
    public async Task DeletingEveryUserOutright_DoesNotReopenSetup()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);
        await SeedAdminAsync(cs, softDeleted: false, withSettingsRow: true);

        await using (var db = PostgresFixture.Context(cs))
        {
            db.UserRoles.RemoveRange(db.UserRoles);
            db.Users.RemoveRange(db.Users.IgnoreQueryFilters());
            await db.SaveChangesAsync();
        }

        await using (var verify = PostgresFixture.Context(cs))
        {
            Assert.False(await AdminExists_Filtered(verify));
            Assert.True(await SetupComplete(verify));
        }
    }

    [PostgresFact]
    public async Task AnAdminWithoutTheSettingsRow_CountsAsSetUp()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);
        await SeedAdminAsync(cs, softDeleted: false, withSettingsRow: false);

        await using var db = PostgresFixture.Context(cs);
        Assert.True(await SetupComplete(db));
    }

    private static async Task SeedAdminAsync(string cs, bool softDeleted, bool withSettingsRow)
    {
        await using var db = PostgresFixture.Context(cs);

        var role = new ApplicationRole { Id = Guid.NewGuid().ToString(), Name = AdminRole, NormalizedName = "ADMIN" };
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "admin@example.test",
            NormalizedUserName = "ADMIN@EXAMPLE.TEST",
            Email = "admin@example.test",
            NormalizedEmail = "ADMIN@EXAMPLE.TEST",
            EmailConfirmed = true,
            DisplayName = "Administrator",
            FirstName = "System",
            LastName = "Administrator",
            Timezone = "UTC",
            SecurityStamp = Guid.NewGuid().ToString(),
            IsDeleted = softDeleted,
            CreatedAt = DateTime.UtcNow,
        };

        db.Roles.Add(role);
        db.Users.Add(user);
        db.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = role.Id });

        if (withSettingsRow)
        {
            db.OrganizationSettings.Add(new OrganizationSettings
            {
                Id = OrganizationSettings.SingletonId,
                DefaultTimezone = "UTC",
                CreatedAt = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task SoftDeleteEveryUserAsync(string cs)
    {
        await using var db = PostgresFixture.Context(cs);

        var users = await db.Users.IgnoreQueryFilters().ToListAsync();
        foreach (var user in users)
            user.IsDeleted = true;

        await db.SaveChangesAsync();
    }
}
