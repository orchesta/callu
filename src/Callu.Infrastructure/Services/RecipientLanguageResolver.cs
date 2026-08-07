using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Shared.Localization;

namespace Callu.Infrastructure.Services;

/// <summary>Resolves a recipient's language: their own choice, else the organization's, else English.</summary>
public sealed class RecipientLanguageResolver(
    IUserContactRepository userContacts,
    IOrganizationSettingsService organizationSettings) : IRecipientLanguageResolver
{
    public async Task<string> ResolveAsync(string? userId, CancellationToken cancellationToken = default)
    {
        var chosen = string.IsNullOrWhiteSpace(userId)
            ? null
            : (await userContacts.GetContactByIdAsync(userId, cancellationToken))?.Culture;

        // Read regardless: a person who has not chosen is spoken to in the language the organization
        // runs in, not in whatever the product was built in.
        var organization = (await organizationSettings.GetSettingsAsync(cancellationToken)).DefaultCulture;

        return SupportedCultures.Resolve(chosen, organization);
    }
}
