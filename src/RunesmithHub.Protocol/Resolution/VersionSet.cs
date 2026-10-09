using System.Numerics;

namespace RunesmithHub.Protocol.Resolution;

/// <summary>An immutable subset of one package's known versions, as bits over the version indexes.</summary>
internal sealed class VersionSet : IEquatable<VersionSet>
{
    private readonly ulong[] _bits;

    private VersionSet(int size, ulong[] bits)
    {
        Size = size;
        _bits = bits;
    }

    /// <summary>Gets how many versions the package has.</summary>
    public int Size { get; }

    public bool IsEmpty => _bits.All(word => word == 0);

    public bool IsFull => Count == Size;

    public int Count => _bits.Sum(word => BitOperations.PopCount(word));

    public static VersionSet Empty(int size) => new(size, new ulong[Words(size)]);

    public static VersionSet Full(int size) => Of(size, _ => true);

    public static VersionSet Of(int size, Func<int, bool> contains)
    {
        var bits = new ulong[Words(size)];
        for (var i = 0; i < size; i++)
        {
            if (contains(i))
                bits[i / 64] |= 1UL << (i % 64);
        }

        return new VersionSet(size, bits);
    }

    public static VersionSet Single(int size, int index) => Of(size, i => i == index);

    public bool Contains(int index) => (_bits[index / 64] & (1UL << (index % 64))) != 0;

    /// <summary>Gets the indexes in the set, lowest first.</summary>
    public IEnumerable<int> Indexes()
    {
        for (var i = 0; i < Size; i++)
        {
            if (Contains(i))
                yield return i;
        }
    }

    public VersionSet Intersect(VersionSet other) => Combine(other, (a, b) => a & b);

    public VersionSet Union(VersionSet other) => Combine(other, (a, b) => a | b);

    public VersionSet Except(VersionSet other) => Combine(other, (a, b) => a & ~b);

    public bool IsSubsetOf(VersionSet other) => Except(other).IsEmpty;

    public bool IsDisjointFrom(VersionSet other) => Intersect(other).IsEmpty;

    private VersionSet Combine(VersionSet other, Func<ulong, ulong, ulong> combine)
    {
        var bits = new ulong[_bits.Length];
        for (var i = 0; i < bits.Length; i++)
            bits[i] = combine(_bits[i], other._bits[i]);
        return new VersionSet(Size, bits);
    }

    private static int Words(int size) => Math.Max(1, (size + 63) / 64);

    public bool Equals(VersionSet? other) => other is not null && Size == other.Size && _bits.AsSpan().SequenceEqual(other._bits);

    public override bool Equals(object? obj) => obj is VersionSet other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var word in _bits)
            hash.Add(word);
        return hash.ToHashCode();
    }
}
