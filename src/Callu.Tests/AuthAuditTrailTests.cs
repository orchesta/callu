using Callu.Application.Common.Interfaces;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Domain.Enums;
using Callu.Infrastructure.Identity;
using Callu.Infrastructure.Services;
using Callu.Shared.Models.Auth;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Callu.Tests;

/// <summary>Who signed in, who failed to, and who was locked out — the first things a review asks for.</summary>
// None of it was recorded: the enum had Login/Logout members that no code ever wrote.
public class AuthAuditTrailTests
{
    private const string Email = "operator@example.io";
    private const string Password = "Correct!Horse123";

    private readonly IAuditLogService _audit = Substitute.For<IAuditLogService>();
    private readonly UserManager<ApplicationUser> _users =
        Substitute.For<UserManager<ApplicationUser>>(
            Substitute.For<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null);

    private readonly ApplicationUser _user = new()
    {
        Id = Guid.NewGuid().ToString(),
        Email = Email,
        UserName = Email,
        FirstName = "Ops",
        LastName = "Person",
    };

    public AuthAuditTrailTests()
    {
        // The unknown-account path hashes a dummy password to keep the timing even.
        _users.PasswordHasher = new PasswordHasher<ApplicationUser>();
    }

    private AuthService Sut()
    {
        var settings = Options.Create(new JwtSettings
        {
            SecretKey = new string('k', 64),
            Issuer = "callu",
            Audience = "callu",
            AccessTokenExpirationMinutes = 15,
            RefreshTokenExpirationDays = 7,
        });

        var jwt = Substitute.For<IJwtTokenService>();
        jwt.GenerateAccessToken(Arg.Any<ApplicationUser>(), Arg.Any<IList<string>>(), Arg.Any<IList<Claim>>())
            .Returns("token");

        var http = Substitute.For<IHttpContextAccessor>();
        http.HttpContext.Returns(new DefaultHttpContext());

        return new AuthService(
            _users,
            Substitute.For<RoleManager<ApplicationRole>>(
                Substitute.For<IRoleStore<ApplicationRole>>(), null, null, null, null),
            jwt,
            settings,
            http,
            new ImmediateTransactionManager(),
            Substitute.For<IRefreshTokenRepository>(),
            Substitute.For<IAccessTokenRevocationStore>(),
            _audit,
            NullLogger<AuthService>.Instance);
    }

    /// <summary>The audit calls this test's run produced, as (action, userId) pairs.</summary>
    private IReadOnlyList<(AuditAction Action, string? UserId)> Written() =>
        _audit.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IAuditLogService.LogAsync))
            .Select(c => c.GetArguments())
            .Select(a => ((AuditAction)a[1]!, (string?)a[0]))
            .ToList();

    private void KnownUser(bool passwordValid = true, bool lockedOut = false, bool emailConfirmed = true)
    {
        _users.FindByEmailAsync(Email).Returns(_user);
        _users.IsLockedOutAsync(_user).Returns(lockedOut);
        _users.CheckPasswordAsync(_user, Arg.Any<string>()).Returns(passwordValid);
        _users.IsEmailConfirmedAsync(_user).Returns(emailConfirmed);
        _users.GetRolesAsync(_user).Returns(new List<string> { "Member" });
        _users.UpdateAsync(_user).Returns(IdentityResult.Success);
    }

    [Fact]
    public async Task ASuccessfulSignIn_IsRecorded()
    {
        KnownUser();

        var result = await Sut().LoginAsync(new LoginRequest { Email = Email, Password = Password });

        Assert.True(result.Success);
        Assert.Contains((AuditAction.Login, _user.Id), Written());
    }

    [Fact]
    public async Task AWrongPassword_IsRecordedAgainstTheAccountItTargeted()
    {
        KnownUser(passwordValid: false);

        var result = await Sut().LoginAsync(new LoginRequest { Email = Email, Password = "wrong" });

        Assert.False(result.Success);
        Assert.Contains((AuditAction.LoginFailed, _user.Id), Written());
    }

    /// <summary>An attempt against an account that does not exist is still an attempt.</summary>
    [Fact]
    public async Task AnAttemptOnAnUnknownAccount_IsRecorded()
    {
        _users.FindByEmailAsync(Email).Returns((ApplicationUser?)null);

        var result = await Sut().LoginAsync(new LoginRequest { Email = Email, Password = Password });

        Assert.False(result.Success);
        Assert.Contains(AuditAction.LoginFailed, Written().Select(w => w.Action));
    }

    [Fact]
    public async Task ALockedOutAccount_IsRecorded()
    {
        KnownUser(lockedOut: true);

        await Sut().LoginAsync(new LoginRequest { Email = Email, Password = Password });

        Assert.Contains((AuditAction.LoginFailed, _user.Id), Written());
    }

    [Fact]
    public async Task AFailedSignIn_IsNeverRecordedAsASuccess()
    {
        KnownUser(passwordValid: false);

        await Sut().LoginAsync(new LoginRequest { Email = Email, Password = "wrong" });

        Assert.DoesNotContain(AuditAction.Login, Written().Select(w => w.Action));
    }

    /// <summary>A broken audit table must not lock everybody out of the product.</summary>
    [Fact]
    public async Task SignInStillSucceeds_WhenTheAuditWriteThrows()
    {
        KnownUser();
        _audit.LogAsync(Arg.Any<string?>(), Arg.Any<AuditAction>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("audit table is gone"));

        var result = await Sut().LoginAsync(new LoginRequest { Email = Email, Password = Password });

        Assert.True(result.Success);
    }
}
