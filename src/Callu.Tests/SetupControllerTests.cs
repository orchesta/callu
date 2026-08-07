using System.Reflection;
using Callu.Api.Controllers;
using Callu.Api.Services;
using Callu.Api.Validators;
using Microsoft.AspNetCore.RateLimiting;
using Callu.Infrastructure.Identity;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Seeding;
using Callu.Shared.Localization;
using Callu.Shared.Models.Settings;
using Callu.Shared.Results;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Callu.Tests;

/// <summary>The anonymous first-admin endpoint, in the parts that do not need PostgreSQL to drive.</summary>
public class SetupControllerTests : IDisposable
{
    private readonly ApplicationDbContext _ctx;

    public SetupControllerTests()
        => _ctx = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"setup-{Guid.NewGuid():N}").Options);

    public void Dispose() => _ctx.Dispose();

    private readonly SetupCompletionLatch _latch = new();

    private SetupController Controller(UserManager<ApplicationUser>? userManager = null) =>
        new(userManager ?? MockUserManager(),
            _ctx,
            Substitute.For<IDbSeeder>(),
            _latch,
            NullLogger<SetupController>.Instance);

    private static UserManager<ApplicationUser> MockUserManager(params ApplicationUser[] admins)
    {
        var mgr = Substitute.For<UserManager<ApplicationUser>>(
            Substitute.For<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null);
        mgr.GetUsersInRoleAsync("Admin").Returns(admins.ToList());
        return mgr;
    }

    private static string? FailMessage(IActionResult result)
    {
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        return Assert.IsType<ApiResponse<object>>(bad.Value).Message;
    }

    // ---- self-disable signal ----------------------------------------------

    [Fact]
    public async Task Status_ReportsSetupRequired_WhenNoAdminExists()
    {
        var result = await Controller().GetSetupStatus(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.True((bool)ok.Value!.GetType().GetProperty("setupRequired")!.GetValue(ok.Value)!);
    }

    [Fact]
    public async Task Status_ReportsConfigured_OnceAnAdminExists()
    {
        await SeedAdminAsync();

        var result = await Controller().GetSetupStatus(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.False((bool)ok.Value!.GetType().GetProperty("setupRequired")!.GetValue(ok.Value)!);
    }

    [Fact]
    public async Task Status_StaysConfigured_AfterTheLastAdminIsRemoved()
    {
        await SeedAdminAsync(softDeleted: true);

        var result = await Controller().GetSetupStatus(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.False((bool)ok.Value!.GetType().GetProperty("setupRequired")!.GetValue(ok.Value)!);
    }

    [Fact]
    public async Task Status_AnswersFromTheLatch_OnceSetupHasBeenSeenComplete()
    {
        await SeedAdminAsync();
        Assert.IsType<OkObjectResult>(await Controller().GetSetupStatus(CancellationToken.None));

        _ctx.UserRoles.RemoveRange(_ctx.UserRoles);
        _ctx.Users.RemoveRange(_ctx.Users);
        _ctx.Roles.RemoveRange(_ctx.Roles);
        await _ctx.SaveChangesAsync();

        var result = await Controller().GetSetupStatus(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.False((bool)ok.Value!.GetType().GetProperty("setupRequired")!.GetValue(ok.Value)!);
    }

    [Fact]
    public async Task Status_DoesNotLatch_WhileSetupIsStillRequired()
    {
        await Controller().GetSetupStatus(CancellationToken.None);

        Assert.False(_latch.IsComplete);
    }

    [Fact]
    public void Status_IsRateLimited()
    {
        var action = typeof(SetupController).GetMethod(nameof(SetupController.GetSetupStatus))!;

        Assert.Null(action.GetCustomAttribute<DisableRateLimitingAttribute>());
        Assert.NotNull(action.GetCustomAttribute<EnableRateLimitingAttribute>());
    }

    private async Task SeedAdminAsync(bool softDeleted = false)
    {
        var role = new ApplicationRole { Id = "role-admin", Name = "Admin", NormalizedName = "ADMIN" };
        var admin = new ApplicationUser
        {
            Id = "admin",
            Email = "admin@example.io",
            UserName = "admin@example.io",
            IsDeleted = softDeleted,
        };

        _ctx.Roles.Add(role);
        _ctx.Users.Add(admin);
        _ctx.UserRoles.Add(new IdentityUserRole<string> { UserId = admin.Id, RoleId = role.Id });
        await _ctx.SaveChangesAsync();
    }

    // ---- required-field guard ---------------------------------------------

    [Theory]
    [InlineData("", "Str0ng-Passw0rd!")]
    [InlineData("   ", "Str0ng-Passw0rd!")]
    [InlineData("admin@example.io", "")]
    [InlineData("admin@example.io", "   ")]
    public async Task InitialSetup_RejectsBlankCredentials_BeforeTouchingTheDatabase(string email, string password)
    {
        var result = await Controller().InitialSetup(
            new InitialSetupRequest(email, password), CancellationToken.None);

        Assert.Equal(Messages.Get("setup.fieldsRequired"), FailMessage(result));
        Assert.Empty(await _ctx.Users.ToListAsync());
    }

    // ---- request validator -------------------------------------------------

    [Theory]
    [InlineData("short1!A")]                 // under 12 characters
    [InlineData("nouppercase-123!")]         // no uppercase
    [InlineData("NOLOWERCASE-123!")]         // no lowercase
    [InlineData("NoDigitsHere!!!!")]         // no digit
    [InlineData("NoSpecialChars123")]        // no special character
    public void Validator_RejectsWeakPasswords(string password)
    {
        var result = new InitialSetupRequestValidator()
            .Validate(new InitialSetupRequest("admin@example.io", password));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validator_RejectsMalformedEmail()
    {
        var result = new InitialSetupRequestValidator()
            .Validate(new InitialSetupRequest("not-an-email", "Str0ng-Passw0rd!"));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validator_AcceptsAWellFormedRequest()
    {
        var result = new InitialSetupRequestValidator()
            .Validate(new InitialSetupRequest("admin@example.io", "Str0ng-Passw0rd!", "Ada Lovelace", "Europe/Istanbul"));

        Assert.True(result.IsValid);
    }
}
