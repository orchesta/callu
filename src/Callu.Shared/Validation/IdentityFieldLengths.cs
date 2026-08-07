namespace Callu.Shared.Validation;

/// <summary>
/// The one place the Identity column widths live; <c>ApplicationUser</c>'s own
/// <c>[StringLength]</c> attributes read them too.
/// </summary>
public static class IdentityFieldLengths
{
    /// <summary>AspNetUsers.Email / .UserName / .NormalizedEmail — varchar(256), Identity's own default.</summary>
    public const int Email = 256;

    /// <summary>AspNetRoles.Name — varchar(256).</summary>
    public const int RoleName = 256;

    public const int FirstName = 100;

    public const int LastName = 100;

    public const int DisplayName = 100;

    /// <summary>ApplicationUser.Culture — varchar(20), holds a culture code like "tr-TR".</summary>
    public const int Culture = 20;

    /// <summary>IANA id. ApplicationUser.Timezone is varchar(100).</summary>
    public const int Timezone = 100;

    /// <summary>Not a column: an E.164 number is at most 15 digits plus a '+'.</summary>
    public const int PhoneNumber = 20;

    /// <summary>
    /// Not a column either: Identity's DataProtector tokens are ~180 characters today, and this only
    /// keeps an arbitrarily large body out of the token verifier.
    /// </summary>
    public const int Token = 512;
}
