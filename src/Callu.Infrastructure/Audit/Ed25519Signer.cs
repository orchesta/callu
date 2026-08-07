using Callu.Shared.Models.Audit;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace Callu.Infrastructure.Audit;

/// <summary>Signs canonical audit-event bytes with one Ed25519 key.</summary>
public sealed class Ed25519Signer(string keyId, byte[] privateKey) : IOpenAuditSigner
{
    public const string AlgorithmName = "Ed25519";

    public string Algorithm => AlgorithmName;

    public string? KeyId => keyId;

    public string Sign(byte[] canonicalBytes)
    {
        var signer = new Org.BouncyCastle.Crypto.Signers.Ed25519Signer();
        signer.Init(forSigning: true, new Ed25519PrivateKeyParameters(privateKey));
        signer.BlockUpdate(canonicalBytes, 0, canonicalBytes.Length);

        // Base64 rather than hex: it is the one encoding a v0.1 verifier checks.
        return Convert.ToBase64String(signer.GenerateSignature());
    }

    public static bool Verify(byte[] publicKey, byte[] canonicalBytes, string base64Signature)
    {
        var verifier = new Org.BouncyCastle.Crypto.Signers.Ed25519Signer();
        verifier.Init(forSigning: false, new Ed25519PublicKeyParameters(publicKey));
        verifier.BlockUpdate(canonicalBytes, 0, canonicalBytes.Length);

        return verifier.VerifySignature(Convert.FromBase64String(base64Signature));
    }
}
