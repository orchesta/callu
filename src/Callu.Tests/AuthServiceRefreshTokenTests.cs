using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Callu.Application.Common.Interfaces;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Domain.Entities;
using Callu.Infrastructure.Identity;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Callu.Application.Services;

namespace Callu.Tests;

/// <summary>Guards the refresh-token rotation and theft-detection contract, family by family.</summary>
public class AuthServiceRefreshTokenTests
{
    private sealed class Harness
    {
        public IRefreshTokenRepository Repo { get; } = Substitute.For<IRefreshTokenRepository>();
        public IAccessTokenRevocationStore Revocation { get; } = Substitute.For<IAccessTokenRevocationStore>();
        public UserManager<ApplicationUser> UserManager { get; }
        public IHttpContextAccessor HttpContextAccessor { get; } = Substitute.For<IHttpContextAccessor>();
        public AuthService Sut { get; }

        public Harness()
        {
            UserManager = Substitute.For<UserManager<ApplicationUser>>(
                Substitute.For<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null);
            var roleManager = Substitute.For<RoleManager<ApplicationRole>>(
                Substitute.For<IRoleStore<ApplicationRole>>(), null, null, null, null);
            var jwt = Substitute.For<IJwtTokenService>();
            jwt.GenerateAccessToken(Arg.Any<ApplicationUser>(), Arg.Any<IList<string>>(), Arg.Any<IList<Claim>>())
                .Returns("access-token");

            var settings = Options.Create(new JwtSettings
            {
                SecretKey = "test-secret-key-that-is-definitely-long-enough-0123456789abcdef",
                Issuer = "callu",
                Audience = "callu-clients",
                AccessTokenExpirationMinutes = 15,
                RefreshTokenExpirationDays = 7
            });

            Sut = new AuthService(UserManager, roleManager, jwt, settings, HttpContextAccessor,
                new ImmediateTransactionManager(), Repo, Revocation,
                Substitute.For<IAuditLogService>(), NullLogger<AuthService>.Instance);
        }
    }

    private static RefreshToken Token(Guid familyId, bool revoked = false, bool expired = false) => new()
    {
        Id = Guid.NewGuid(),
        UserId = "u1",
        TokenHash = "hash",
        FamilyId = familyId,
        ExpiresAt = expired ? DateTime.UtcNow.AddMinutes(-1) : DateTime.UtcNow.AddDays(7),
        RevokedAt = revoked ? DateTime.UtcNow.AddMinutes(-1) : null
    };

    [Fact]
    public async Task Refresh_WithRevokedToken_RevokesFamily_AndFails()
    {
        var h = new Harness();
        var family = Guid.NewGuid();
        h.Repo.GetByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Token(family, revoked: true));
        h.Repo.GetActiveByFamilyIdAsync(family, Arg.Any<CancellationToken>())
            .Returns(new List<RefreshToken> { Token(family) });

        var result = await h.Sut.RefreshTokenAsync("plaintext");

        Assert.False(result.Success);
        await h.Repo.Received(1).GetActiveByFamilyIdAsync(family, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refresh_WithExpiredToken_Fails_WithoutFamilyPurge()
    {
        var h = new Harness();
        var family = Guid.NewGuid();
        h.Repo.GetByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Token(family, expired: true));

        var result = await h.Sut.RefreshTokenAsync("plaintext");

        Assert.False(result.Success);
        await h.Repo.DidNotReceive().GetActiveByFamilyIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refresh_WithValidToken_RotatesWithinSameFamily()
    {
        var h = new Harness();
        var family = Guid.NewGuid();
        h.Repo.GetByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Token(family));
        h.UserManager.FindByIdAsync("u1").Returns(new ApplicationUser { Id = "u1", Email = "u@x.io", UserName = "u@x.io" });
        h.UserManager.GetRolesAsync(Arg.Any<ApplicationUser>()).Returns(new List<string> { "Member" });
        h.Repo.TryRevokeForRotationAsync(Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await h.Sut.RefreshTokenAsync("plaintext");

        Assert.True(result.Success);
        Assert.False(string.IsNullOrEmpty(result.RefreshToken));
        await h.Repo.Received(1).AddAsync(Arg.Is<RefreshToken>(t => t.FamilyId == family), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refresh_WhenRotationLosesRace_RevokesFamily_AndFails()
    {
        var h = new Harness();
        var family = Guid.NewGuid();
        h.Repo.GetByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Token(family));
        h.UserManager.FindByIdAsync("u1").Returns(new ApplicationUser { Id = "u1", Email = "u@x.io", UserName = "u@x.io" });
        h.Repo.TryRevokeForRotationAsync(Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);
        h.Repo.GetActiveByFamilyIdAsync(family, Arg.Any<CancellationToken>())
            .Returns(new List<RefreshToken> { Token(family) });

        var result = await h.Sut.RefreshTokenAsync("plaintext");

        Assert.False(result.Success);
        await h.Repo.Received(1).GetActiveByFamilyIdAsync(family, Arg.Any<CancellationToken>());
        await h.Repo.DidNotReceive().AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Logout_RevokesAllActiveSessions_AndBlacklistsAccessToken()
    {
        var h = new Harness();
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "u1"),
            new(JwtRegisteredClaimNames.Jti, "jti-123"),
            new(JwtRegisteredClaimNames.Exp, DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds().ToString())
        };
        h.HttpContextAccessor.HttpContext.Returns(new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
        });

        await h.Sut.LogoutAsync();

        await h.Repo.Received(1).RevokeAllActiveForUserAsync("u1", "logout", Arg.Any<CancellationToken>());
        await h.Revocation.Received(1).RevokeAsync("jti-123", Arg.Any<TimeSpan>());
    }

    [Fact]
    public async Task LogoutByRefreshToken_RevokesThatFamilyOnly()
    {
        var h = new Harness();
        var family = Guid.NewGuid();
        h.Repo.GetByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Token(family));

        await h.Sut.LogoutByRefreshTokenAsync("plaintext");

        await h.Repo.Received(1).RevokeActiveByFamilyIdAsync(family, "logout", Arg.Any<CancellationToken>());
        await h.Repo.DidNotReceive()
            .RevokeAllActiveForUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LogoutByRefreshToken_UnknownToken_IsIdempotent()
    {
        var h = new Harness();
        h.Repo.GetByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((RefreshToken?)null);

        await h.Sut.LogoutByRefreshTokenAsync("missing");

        await h.Repo.DidNotReceive()
            .RevokeActiveByFamilyIdAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await h.Repo.DidNotReceive()
            .RevokeAllActiveForUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
