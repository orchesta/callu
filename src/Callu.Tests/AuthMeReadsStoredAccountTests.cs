using System.Security.Claims;
using Callu.Api.Controllers;
using Callu.Application.Common.Interfaces;
using Callu.Application.Services;
using Callu.Infrastructure.Identity;
using Callu.Shared.Models.Auth;
using Callu.Shared.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Callu.Tests;

/// <summary>What /auth/me answers when the token predates an edit to the account.</summary>
public class AuthMeReadsStoredAccountTests
{
    private const string UserId = "user-1";

    private readonly IUserManagementService _users = Substitute.For<IUserManagementService>();

    private AuthController Controller()
    {
        var controller = new AuthController(
            Substitute.For<IAuthService>(),
            _users,
            Options.Create(new JwtSettings()),
            Substitute.For<IHostEnvironment>());

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, UserId),
            new Claim(ClaimTypes.Email, "old@example.com"),
            new Claim(ClaimTypes.Name, "Old Name"),
            new Claim(ClaimTypes.Role, "Member"),
        ], "test");

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
        };

        return controller;
    }

    private static UserInfo Body(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return Assert.IsType<ApiResponse<UserInfo>>(ok.Value).Data!;
    }

    [Fact]
    public async Task PrefersTheStoredAccountOverTheClaimsTheTokenCarries()
    {
        _users.GetUserByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(new UserDto
        {
            Id = UserId,
            Email = "new@example.com",
            DisplayName = "New Name",
            Role = "Admin",
        });

        var info = Body(await Controller().Me(CancellationToken.None));

        Assert.Equal("New Name", info.Name);
        Assert.Equal("new@example.com", info.Email);
        Assert.Equal("Admin", info.Role);
    }

    [Fact]
    public async Task FallsBackToTheClaimsWhenTheAccountCannotBeRead()
    {
        _users.GetUserByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((UserDto?)null);

        var info = Body(await Controller().Me(CancellationToken.None));

        Assert.Equal(UserId, info.Id);
        Assert.Equal("Old Name", info.Name);
        Assert.Equal("old@example.com", info.Email);
        Assert.Equal("Member", info.Role);
    }

    [Fact]
    public async Task KeepsTheClaimNameWhenTheStoredAccountHasNoDisplayName()
    {
        _users.GetUserByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(new UserDto
        {
            Id = UserId,
            Email = "new@example.com",
            DisplayName = null,
            Role = "Member",
        });

        var info = Body(await Controller().Me(CancellationToken.None));

        Assert.Equal("Old Name", info.Name);
        Assert.Equal("new@example.com", info.Email);
    }
}
