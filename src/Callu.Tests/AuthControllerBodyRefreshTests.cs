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
using Microsoft.Extensions.Primitives;
using NSubstitute;

namespace Callu.Tests;

/// <summary>Native clients opt into body refresh via X-Callu-Client; SPA keeps the HttpOnly cookie.</summary>
public class AuthControllerBodyRefreshTests
{
    private readonly IAuthService _auth = Substitute.For<IAuthService>();

    private AuthController Controller(DefaultHttpContext? http = null)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns("Development");

        var controller = new AuthController(
            _auth,
            Substitute.For<IUserManagementService>(),
            Options.Create(new JwtSettings { RefreshTokenExpirationDays = 7 }),
            env);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = http ?? new DefaultHttpContext(),
        };
        return controller;
    }

    private static DefaultHttpContext NativeHttp()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers["X-Callu-Client"] = "native";
        return http;
    }

    private static AuthResponse OkAuth(string refresh = "refresh-plain") => new()
    {
        Success = true,
        Message = "ok",
        Token = "access",
        ExpiresAt = DateTime.UtcNow.AddMinutes(15),
        RefreshToken = refresh,
        User = new UserInfo { Id = "u1", Email = "a@b.c", Name = "Ada", Role = "Member" },
    };

    private static LoginResponse Body(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return Assert.IsType<ApiResponse<LoginResponse>>(ok.Value).Data!;
    }

    [Fact]
    public async Task Login_WithoutNativeHeader_OmitsRefreshTokenFromBody_AndSetsCookie()
    {
        _auth.LoginAsync(Arg.Any<LoginRequest>(), Arg.Any<CancellationToken>()).Returns(OkAuth("rt-login"));

        var http = new DefaultHttpContext();
        var result = await Controller(http).Login(new LoginRequest { Email = "a@b.c", Password = "x" }, CancellationToken.None);

        var payload = Body(result);
        Assert.Equal("access", payload.AccessToken);
        Assert.Null(payload.RefreshToken);
        Assert.Contains("calluapp_refresh", http.Response.Headers.SetCookie.ToString());
    }

    [Fact]
    public async Task Login_WithNativeHeader_IncludesRefreshTokenInBody()
    {
        _auth.LoginAsync(Arg.Any<LoginRequest>(), Arg.Any<CancellationToken>()).Returns(OkAuth("rt-login"));

        var payload = Body(await Controller(NativeHttp()).Login(
            new LoginRequest { Email = "a@b.c", Password = "x" }, CancellationToken.None));

        Assert.Equal("rt-login", payload.RefreshToken);
    }

    [Fact]
    public async Task Refresh_UsesBodyWhenCookieMissing_NativeGetsRefreshInBody()
    {
        _auth.RefreshTokenAsync("rt-body", Arg.Any<CancellationToken>()).Returns(OkAuth("rt-next"));

        var payload = Body(await Controller(NativeHttp()).Refresh(
            new RefreshRequest("rt-body"), CancellationToken.None));

        Assert.Equal("rt-next", payload.RefreshToken);
        await _auth.Received(1).RefreshTokenAsync("rt-body", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refresh_WithoutNativeHeader_OmitsRefreshTokenFromBody()
    {
        _auth.RefreshTokenAsync("rt-body", Arg.Any<CancellationToken>()).Returns(OkAuth("rt-next"));

        var payload = Body(await Controller().Refresh(new RefreshRequest("rt-body"), CancellationToken.None));

        Assert.Null(payload.RefreshToken);
    }

    [Fact]
    public async Task Refresh_PrefersCookieOverBody()
    {
        _auth.RefreshTokenAsync("rt-cookie", Arg.Any<CancellationToken>()).Returns(OkAuth("rt-next"));

        var http = new DefaultHttpContext();
        http.Request.Headers.Cookie = new StringValues("calluapp_refresh=rt-cookie");

        await Controller(http).Refresh(new RefreshRequest("rt-body-ignored"), CancellationToken.None);

        await _auth.Received(1).RefreshTokenAsync("rt-cookie", Arg.Any<CancellationToken>());
        await _auth.DidNotReceive().RefreshTokenAsync("rt-body-ignored", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refresh_WithoutCookieOrBody_IsUnauthorized()
    {
        var result = await Controller().Refresh(null, CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
        await _auth.DidNotReceive().RefreshTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Logout_WithoutBearer_UsesBodyRefresh()
    {
        await Controller().Logout(new RefreshRequest("rt-out"), CancellationToken.None);

        await _auth.Received(1).LogoutByRefreshTokenAsync("rt-out", Arg.Any<CancellationToken>());
        await _auth.DidNotReceive().LogoutAsync(Arg.Any<CancellationToken>());
    }
}
