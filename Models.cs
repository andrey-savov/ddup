namespace Ddup;

/// <summary>
/// Components to include in the duplicate detection hash
/// </summary>
[Flags]
public enum HashComponents
{
    None = 0,
    Content = 1,
    Size = 2,
    ModifiedTime = 4,
    CreatedTime = 8,
    FileName = 16
}

/// <summary>
/// Memory-efficient deduplication key (16 bytes total)
/// </summary>
public readonly struct DedupKey : IEquatable<DedupKey>
{
    public readonly long Size;
    public readonly ulong Hash;

    public DedupKey(long size, ulong hash = 0)
    {
        Size = size;
        Hash = hash;
    }

    public bool Equals(DedupKey other) => Size == other.Size && Hash == other.Hash;

    public override bool Equals(object? obj) => obj is DedupKey other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Size, Hash);

    public static bool operator ==(DedupKey left, DedupKey right) => left.Equals(right);

    public static bool operator !=(DedupKey left, DedupKey right) => !left.Equals(right);
}

/// <summary>
/// Database record for file metadata
/// </summary>
public record FileRecord
{
    public long Id { get; init; }
    public required string Path { get; init; }
    public long Size { get; init; }
    public long Modified { get; init; }
    public long Created { get; init; }
    public byte[]? Hash { get; init; }
    public long ScanId { get; init; }

    public DateTime ModifiedDate => DateTimeOffset.FromUnixTimeSeconds(Modified).DateTime;
    public DateTime CreatedDate => DateTimeOffset.FromUnixTimeSeconds(Created).DateTime;
}

/// <summary>
/// Command-line options for the scanner
/// </summary>
public record ScanOptions
{
    public required string RootPath { get; init; }
    public HashComponents HashComponents { get; init; } = HashComponents.Size;
    public int Workers { get; init; } = Environment.ProcessorCount;
    public string DbPath { get; init; } = ".dups.db";
    public bool Incremental { get; init; } = true;
    public bool FullScan { get; init; }

    public bool IncludesContent => (HashComponents & HashComponents.Content) != 0;
}

/// <summary>
/// Group of duplicate files
/// </summary>
public record DuplicateGroup
{
    public long Size { get; init; }
    public ulong? Hash { get; init; }
    public required List<FileRecord> Files { get; init; }
    public int Count => Files.Count;
}
