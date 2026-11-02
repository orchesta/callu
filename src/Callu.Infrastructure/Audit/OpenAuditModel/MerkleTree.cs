using System.Security.Cryptography;

namespace Callu.Infrastructure.Audit.OpenAuditModel;

/// <summary>One sibling on the way from a leaf to the root, and the side it sits on.</summary>
public sealed record MerklePathStep(string Side, byte[] Hash);

/// <summary>The RFC 6962 §2.1 tree OpenAuditModel's proof format is defined over.</summary>
// The 0x00/0x01 prefixes keep a leaf from ever colliding with a node; the split at the largest power
// of two below n, with an odd node promoted rather than duplicated, is what makes two implementations
// build the same tree.
public static class MerkleTree
{
    public static byte[] LeafHash(byte[] digest)
    {
        var input = new byte[digest.Length + 1];
        input[0] = 0x00;
        digest.CopyTo(input, 1);
        return SHA256.HashData(input);
    }

    public static byte[] NodeHash(byte[] left, byte[] right)
    {
        var input = new byte[1 + left.Length + right.Length];
        input[0] = 0x01;
        left.CopyTo(input, 1);
        right.CopyTo(input, 1 + left.Length);
        return SHA256.HashData(input);
    }

    public static byte[] Root(IReadOnlyList<byte[]> digests)
    {
        if (digests.Count == 0)
            throw new ArgumentException("A tree needs at least one leaf.", nameof(digests));

        return Hash(digests, 0, digests.Count);
    }

    /// <summary>Siblings from the leaf upward.</summary>
    public static IReadOnlyList<MerklePathStep> AuditPath(IReadOnlyList<byte[]> digests, int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, digests.Count);

        var path = new List<MerklePathStep>();
        Path(digests, 0, digests.Count, index, path);
        return path;
    }

    private static byte[] Hash(IReadOnlyList<byte[]> d, int start, int count)
    {
        if (count == 1) return LeafHash(d[start]);

        var k = SplitPoint(count);
        return NodeHash(Hash(d, start, k), Hash(d, start + k, count - k));
    }

    // RFC 6962 PATH(m, D[n]): the recursion reaches the leaf first, so siblings are appended
    // leaf-to-root, which is the order the proof document lists them in.
    private static void Path(IReadOnlyList<byte[]> d, int start, int count, int m, List<MerklePathStep> path)
    {
        if (count == 1) return;

        var k = SplitPoint(count);
        if (m < k)
        {
            Path(d, start, k, m, path);
            path.Add(new MerklePathStep("right", Hash(d, start + k, count - k)));
        }
        else
        {
            Path(d, start + k, count - k, m - k, path);
            path.Add(new MerklePathStep("left", Hash(d, start, k)));
        }
    }

    /// <summary>The largest power of two smaller than n.</summary>
    private static int SplitPoint(int n)
    {
        var k = 1;
        while (k << 1 < n) k <<= 1;
        return k;
    }
}

/// <summary>The same RFC 6962 tree, fed one leaf at a time in O(log n) memory: its root, and one leaf's audit path.</summary>
// A checkpoint or proof over a million leaves would otherwise hold every leaf, twice, for the length of a
// request. Only complete subtrees are kept: one per set bit of the count, merged as soon as two match.
// The tree splits at the largest power of two below n, so those subtrees, folded from the right, are its root.
public sealed class StreamingMerkleTree
{
    private readonly List<(long Start, long Size, byte[] Hash)> _complete = [];

    // The target's siblings inside the subtree that ends up holding it, by level: the left ones are the
    // subtrees already complete when it arrives, the right ones complete after it.
    private readonly Dictionary<int, byte[]> _siblings = [];
    private long? _target;

    public long Count { get; private set; }

    /// <summary>The position of the leaf streamed as the target, once it has arrived.</summary>
    public long? TargetIndex => _target;

    public void Append(byte[] digest, bool isTarget = false)
    {
        if (isTarget)
        {
            if (_target is not null) throw new InvalidOperationException("A tree streams one target leaf.");
            _target = Count;
            foreach (var (_, size, hash) in _complete) _siblings[Level(size)] = hash;
        }

        Push(Count, 1, MerkleTree.LeafHash(digest));
        Count++;

        while (_complete.Count >= 2 && _complete[^1].Size == _complete[^2].Size)
        {
            var right = _complete[^1];
            var left = _complete[^2];
            _complete.RemoveRange(_complete.Count - 2, 2);
            Push(left.Start, left.Size * 2, MerkleTree.NodeHash(left.Hash, right.Hash));
        }
    }

    public byte[] Root()
    {
        if (Count == 0) throw new InvalidOperationException("A tree needs at least one leaf.");
        return Fold(0);
    }

    /// <summary>Siblings from the target leaf upward, as <see cref="MerkleTree.AuditPath"/> lists them.</summary>
    public IReadOnlyList<MerklePathStep> AuditPath()
    {
        if (_target is not { } target) throw new InvalidOperationException("No target leaf was streamed.");

        var holder = _complete.FindIndex(c => target >= c.Start && target < c.Start + c.Size);
        var path = new List<MerklePathStep>();

        for (var level = 0; level < Level(_complete[holder].Size); level++)
            path.Add(new MerklePathStep(((target >> level) & 1) == 1 ? "left" : "right", _siblings[level]));

        if (holder < _complete.Count - 1) path.Add(new MerklePathStep("right", Fold(holder + 1)));
        for (var i = holder - 1; i >= 0; i--) path.Add(new MerklePathStep("left", _complete[i].Hash));

        return path;
    }

    private void Push(long start, long size, byte[] hash)
    {
        _complete.Add((start, size, hash));

        // A right-hand sibling of the target: the aligned block of this size just after the target's own.
        if (_target is { } target && start > target && (start >> Level(size)) == ((target >> Level(size)) ^ 1))
            _siblings[Level(size)] = hash;
    }

    private byte[] Fold(int from)
    {
        var hash = _complete[^1].Hash;
        for (var i = _complete.Count - 2; i >= from; i--) hash = MerkleTree.NodeHash(_complete[i].Hash, hash);
        return hash;
    }

    private static int Level(long size) => System.Numerics.BitOperations.Log2((ulong)size);
}
