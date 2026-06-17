using System.Security.Cryptography;
using System.Text;
using Callu.Infrastructure.Services;

namespace Callu.Tests;

/// <summary>
/// Inbound-webhook HMAC verification is the primary untrusted-input gate. These confirm a
/// correct signature passes in every supported encoding (sha256=hex, raw hex, base64), and that
/// any wrong secret / tampered body / missing header is rejected.
/// </summary>
public class HmacWebhookSignatureVerifierTests
{
    private const string Secret = "super-secret-key";
    private const string Body = "{\"event\":\"alert\",\"id\":42}";
    private const string Header = "X-Signature";

    private readonly HmacWebhookSignatureVerifier _sut = new();

    private static string HexSig(string body, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
    }

    private static string Base64Sig(string body, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(body)));
    }

    private bool Verify(string signature) =>
        _sut.Verify(Body, Secret, new Dictionary<string, string> { [Header] = signature }, Header);

    [Fact]
    public void Verify_RawHexSignature_True() => Assert.True(Verify(HexSig(Body, Secret)));

    [Fact]
    public void Verify_Sha256PrefixedHex_True() => Assert.True(Verify("sha256=" + HexSig(Body, Secret)));

    [Fact]
    public void Verify_Base64Signature_True() => Assert.True(Verify(Base64Sig(Body, Secret)));

    [Fact]
    public void Verify_HexIsCaseInsensitive_True() => Assert.True(Verify(HexSig(Body, Secret).ToUpperInvariant()));

    [Fact]
    public void Verify_WrongSecret_False() => Assert.False(Verify(HexSig(Body, "the-wrong-secret")));

    [Fact]
    public void Verify_TamperedBody_False() => Assert.False(Verify(HexSig(Body + "tampered", Secret)));

    [Fact]
    public void Verify_GarbageSignature_False() => Assert.False(Verify("not-a-real-signature"));

    [Fact]
    public void Verify_MissingHeader_False() =>
        Assert.False(_sut.Verify(Body, Secret, new Dictionary<string, string>(), Header));

    [Fact]
    public void Verify_EmptyBody_False() =>
        Assert.False(_sut.Verify("", Secret, new Dictionary<string, string> { [Header] = HexSig(Body, Secret) }, Header));

    [Fact]
    public void Verify_EmptySecret_False() =>
        Assert.False(_sut.Verify(Body, "", new Dictionary<string, string> { [Header] = HexSig(Body, Secret) }, Header));
}
