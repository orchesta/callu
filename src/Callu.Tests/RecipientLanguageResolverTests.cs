using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Common.Models.Persistence;
using Callu.Application.Services;
using Callu.Infrastructure.Services;
using Callu.Shared.Models.Settings;
using NSubstitute;

namespace Callu.Tests;

/// <summary>Which language a person is spoken to in when they are paged.</summary>
public class RecipientLanguageResolverTests
{
    private readonly IUserContactRepository _contacts = Substitute.For<IUserContactRepository>();
    private readonly IOrganizationSettingsService _organization = Substitute.For<IOrganizationSettingsService>();

    private RecipientLanguageResolver Sut(string? userCulture, string organizationCulture = "en-US")
    {
        _contacts.GetContactByIdAsync("u-1", Arg.Any<CancellationToken>())
            .Returns(new UserContactSnapshot("u-1", "Ada", "+900000000", "ada@x.io", userCulture));

        _organization.GetSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(new OrganizationSettingsDto { DefaultCulture = organizationCulture });

        return new RecipientLanguageResolver(_contacts, _organization);
    }

    [Fact]
    public async Task ThePersonsOwnChoiceWins()
    {
        Assert.Equal("tr-TR", await Sut(userCulture: "tr-TR", organizationCulture: "en-US").ResolveAsync("u-1"));
    }

    /// <summary>Nobody ever set this column before, so almost every existing account is unset — and
    /// answering English for all of them is what made every call English.</summary>
    [Fact]
    public async Task WithoutAChoice_TheOrganizationsLanguageIsUsed()
    {
        Assert.Equal("tr-TR", await Sut(userCulture: null, organizationCulture: "tr-TR").ResolveAsync("u-1"));
    }

    [Fact]
    public async Task WithNeitherSet_ThereIsStillAnAnswer()
    {
        Assert.Equal("en-US", await Sut(userCulture: null, organizationCulture: "").ResolveAsync("u-1"));
    }

    /// <summary>The UI stores a two-letter code; it has to mean the same language here.</summary>
    [Theory]
    [InlineData("tr", "tr-TR")]
    [InlineData("en", "en-US")]
    [InlineData("tr-CY", "tr-TR")]
    public async Task ATwoLetterOrRegionalCodeResolvesToTheLanguageWeSpeak(string stored, string expected)
    {
        Assert.Equal(expected, await Sut(userCulture: stored).ResolveAsync("u-1"));
    }

    [Fact]
    public async Task AnUnknownUserStillGetsTheOrganizationsLanguage()
    {
        _contacts.GetContactByIdAsync("ghost", Arg.Any<CancellationToken>()).Returns((UserContactSnapshot?)null);
        _organization.GetSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(new OrganizationSettingsDto { DefaultCulture = "tr-TR" });

        var sut = new RecipientLanguageResolver(_contacts, _organization);

        Assert.Equal("tr-TR", await sut.ResolveAsync("ghost"));
    }

    /// <summary>A test call from the settings screen has no incident and may have no user.</summary>
    [Fact]
    public async Task WithNoUserAtAll_TheOrganizationsLanguageIsUsed()
    {
        _organization.GetSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(new OrganizationSettingsDto { DefaultCulture = "tr-TR" });

        var sut = new RecipientLanguageResolver(_contacts, _organization);

        Assert.Equal("tr-TR", await sut.ResolveAsync(null));
    }
}
