using Callu.Infrastructure.Audit.OpenAuditModel;

namespace Callu.Tests.Audit;

/// <summary>The tree OpenAuditModel's proof format defines, checked against its published vectors.</summary>
public class MerkleTreeTests
{
    private static List<byte[]> ReferenceDigests() =>
        new[] { "001.json", "002.json", "003.json" }
            .Select(f => Convert.FromHexString(OamFixtures.Read(f)["integrity"]!["hash"]!.GetValue<string>()))
            .ToList();

    [Fact]
    public void TheRootOfTheReferenceChain_IsThePublishedRoot()
    {
        var root = MerkleTree.Root(ReferenceDigests());

        Assert.Equal("a43e376630ac6ebb7f3385a79e02a5b01826d925a5199d249511584c22f6822f",
            Convert.ToHexStringLower(root));
    }

    [Fact]
    public void ThePathForTheMiddleEvent_IsThePublishedPath()
    {
        var path = MerkleTree.AuditPath(ReferenceDigests(), 1);

        Assert.Equal(2, path.Count);
        Assert.Equal("left", path[0].Side);
        Assert.Equal("a6eda3d87ca03442e74a2d866ae5cb15c0dbf1b98e9fcbe727ec265f4c71db74", Convert.ToHexStringLower(path[0].Hash));
        Assert.Equal("right", path[1].Side);
        Assert.Equal("1d06e9f239f757bdad0398feaa49e091fcb5c8de89802025c821eaa93379ad4a", Convert.ToHexStringLower(path[1].Hash));
    }

    /// <summary>The streamed root and path, fed one leaf at a time, are the recursive ones for every size and index.</summary>
    // Sizes up to 70 cross several powers of two, so every shape of unbalanced right edge is reached.
    [Fact]
    public void TheStreamedRootAndPath_EqualTheRecursiveOnes_ForEverySizeAndIndex()
    {
        for (var n = 1; n <= 70; n++)
        {
            var digests = Enumerable.Range(0, n)
                .Select(i => System.Security.Cryptography.SHA256.HashData(BitConverter.GetBytes(i)))
                .ToList();
            var root = MerkleTree.Root(digests);

            for (var index = 0; index < n; index++)
            {
                var streamed = new StreamingMerkleTree();
                for (var i = 0; i < n; i++) streamed.Append(digests[i], isTarget: i == index);

                Assert.Equal(n, streamed.Count);
                Assert.Equal(root, streamed.Root());

                var expected = MerkleTree.AuditPath(digests, index);
                var actual = streamed.AuditPath();
                Assert.Equal(expected.Select(p => p.Side), actual.Select(p => p.Side));
                Assert.Equal(expected.Select(p => Convert.ToHexString(p.Hash)), actual.Select(p => Convert.ToHexString(p.Hash)));
            }
        }
    }

    [Fact]
    public void AStreamedTree_WithNoTarget_HasNoPath_AndAnEmptyOneHasNoRoot()
    {
        var streamed = new StreamingMerkleTree();
        Assert.Throws<InvalidOperationException>(() => { streamed.Root(); });

        streamed.Append(ReferenceDigests()[0]);
        Assert.Throws<InvalidOperationException>(() => { streamed.AuditPath(); });
    }

    [Fact]
    public void AOneLeafTree_IsItsLeafHash_AndHasAnEmptyPath()
    {
        var digest = ReferenceDigests()[0];

        Assert.Equal(MerkleTree.LeafHash(digest), MerkleTree.Root([digest]));
        Assert.Empty(MerkleTree.AuditPath([digest], 0));
    }

    [Fact]
    public void AnOddNodeIsPromoted_NeverDuplicated()
    {
        var d = Enumerable.Range(0, 5).Select(i => new[] { (byte)i }).ToList();
        var l = d.Select(MerkleTree.LeafHash).ToList();

        var expected = MerkleTree.NodeHash(
            MerkleTree.NodeHash(MerkleTree.NodeHash(l[0], l[1]), MerkleTree.NodeHash(l[2], l[3])),
            l[4]);

        Assert.Equal(expected, MerkleTree.Root(d));
    }

    [Fact]
    public void EveryPath_LeadsBackToTheRoot_ForEverySizeUpTo33()
    {
        for (var n = 1; n <= 33; n++)
        {
            var d = Enumerable.Range(0, n).Select(i => BitConverter.GetBytes(i)).ToList();
            var root = MerkleTree.Root(d);

            for (var i = 0; i < n; i++)
            {
                var hash = MerkleTree.LeafHash(d[i]);
                foreach (var step in MerkleTree.AuditPath(d, i))
                    hash = step.Side == "left" ? MerkleTree.NodeHash(step.Hash, hash) : MerkleTree.NodeHash(hash, step.Hash);

                Assert.True(root.AsSpan().SequenceEqual(hash), $"n={n}, index={i}");
            }
        }
    }

    [Fact]
    public void AnIndexOutsideTheTree_IsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MerkleTree.AuditPath(ReferenceDigests(), 3));
        Assert.Throws<ArgumentException>(() => MerkleTree.Root([]));
    }
}
