using Callu.Infrastructure.Audit;

namespace Callu.Tests.Audit;

public class Ed25519PemTests
{
    [Fact]
    public void TheRawKey_BecomesTheSpkiPemOpenAuditModelReads()
    {
        var raw = Convert.FromBase64String("kb7Bleqo+EYvJ2QYmWKh38+AmntzJMwdzMTFaf5hqyI=");
        var expected = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Audit", "Fixtures", "OpenAuditModel", "ed25519-test-public.pem")).ReplaceLineEndings("\n");

        Assert.Equal(expected, Ed25519Signer.ToPem(raw));
    }
}
