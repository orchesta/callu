using Callu.Application.Validators;
using Callu.Infrastructure.Identity;
using Callu.Infrastructure.Services;
using Callu.Shared.Extensions;
using Callu.Shared.Models.Auth;
using Callu.Shared.Validation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Callu.Tests;

/// <summary>
/// FirstName and LastName are each bounded at their own varchar(100), and DisplayName is derived
/// from the pair into another varchar(100) that no request DTO names.
/// </summary>
public class ProfileDisplayNameLengthTests
{
    private static UserManager<ApplicationUser> UserManagerFor(ApplicationUser user)
    {
        var manager = Substitute.For<UserManager<ApplicationUser>>(
            Substitute.For<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null);

        manager.FindByIdAsync(user.Id).Returns(user);
        manager.UpdateAsync(user).Returns(IdentityResult.Success);

        return manager;
    }

    private static ProfileService Service(UserManager<ApplicationUser> manager) =>
        new(manager,
            Substitute.For<Callu.Application.Common.Interfaces.Persistence.INotificationPreferenceRepository>(),
            Substitute.For<Callu.Application.Common.Interfaces.Persistence.IRefreshTokenRepository>(),
            new ImmediateTransactionManager(),
            Substitute.For<Callu.Application.Services.IAuditLogService>(),
            NullLogger<ProfileService>.Instance);

    [Fact]
    public async Task TwoMaximumLengthNames_DoNotOverflowDisplayName()
    {
        var user = new ApplicationUser { Id = "u1", Email = "u1@example.io" };
        var manager = UserManagerFor(user);

        var first = new string('a', IdentityFieldLengths.FirstName);
        var last = new string('b', IdentityFieldLengths.LastName);

        // Both halves pass validation: this is the accepted request, not a rejected one.
        Assert.True(new UpdateProfileRequestValidator()
            .Validate(new UpdateProfileRequest { FirstName = first, LastName = last }).IsValid);

        var updated = await Service(manager).UpdateProfileAsync(
            "u1", new UpdateProfileRequest { FirstName = first, LastName = last });

        Assert.True(updated);
        Assert.Equal(IdentityFieldLengths.DisplayName, user.DisplayName!.Length);
    }

    [Fact]
    public async Task AnOrdinaryName_IsNotTouched()
    {
        var user = new ApplicationUser { Id = "u2", Email = "u2@example.io" };

        await Service(UserManagerFor(user)).UpdateProfileAsync(
            "u2", new UpdateProfileRequest { FirstName = "Ada", LastName = "Lovelace" });

        Assert.Equal("Ada Lovelace", user.DisplayName);
        Assert.Equal("AL", user.Initials);
    }

    [Fact]
    public void ClampTo_DoesNotSplitASurrogatePair()
    {
        // "ab" + an astral character: cutting at 3 would leave a lone high surrogate, which is not
        // valid UTF-8 and trades the length error for an encoding error.
        var value = "ab\U0001F600";

        Assert.Equal("ab", value.ClampTo(3));
        Assert.Equal("ab", value.ClampTo(2));
        Assert.Equal(value, value.ClampTo(4));
        Assert.Null(((string?)null).ClampTo(4));
    }
}
