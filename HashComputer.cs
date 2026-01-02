using System.Buffers.Binary;
using System.IO.Hashing;
using System.Text;

namespace Ddup;

/// <summary>
/// Fast hash computation using xxHash64 with logarithmic sampling
/// </summary>
public static class HashComputer
{
    private const int ChunkSize = 64 * 1024; // 64KB per sample
    private const int MinChunks = 3;
    private const int MaxChunks = 100;

    /// <summary>
    /// Calculate number of chunks based on file size
    /// Formula: max(3, min(100, floor(log2(sizeInMB)) * 3))
    /// </summary>
    public static int CalculateChunkCount(long fileSize)
    {
        if (fileSize == 0) return MinChunks;

        var sizeInMB = fileSize / (1024.0 * 1024.0);
        var chunks = (int)(Math.Floor(Math.Log2(sizeInMB)) * 3);

        return Math.Clamp(chunks, MinChunks, MaxChunks);
    }

    /// <summary>
    /// Calculate evenly distributed chunk positions across the file
    /// </summary>
    public static long[] CalculateChunkPositions(long fileSize, int chunkCount)
    {
        if (fileSize <= ChunkSize)
        {
            return [0];
        }

        var positions = new long[chunkCount];
        var stride = fileSize / chunkCount;

        for (int i = 0; i < chunkCount; i++)
        {
            positions[i] = Math.Min(i * stride, fileSize - ChunkSize);
        }

        return positions;
    }

    /// <summary>
    /// Compute xxHash64 with logarithmic sampling
    /// </summary>
    public static async Task<byte[]> ComputeSampledHashAsync(string path, long size)
    {
        var chunkCount = CalculateChunkCount(size);
        var positions = CalculateChunkPositions(size, chunkCount);

        try
        {
            using var fs = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                ChunkSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var hasher = new XxHash64();
            var buffer = new byte[ChunkSize];

            foreach (var position in positions)
            {
                fs.Seek(position, SeekOrigin.Begin);

                var bytesToRead = (int)Math.Min(ChunkSize, size - position);
                var bytesRead = await fs.ReadAsync(buffer.AsMemory(0, bytesToRead));

                if (bytesRead > 0)
                {
                    hasher.Append(buffer.AsSpan(0, bytesRead));
                }
            }

            var hashBytes = hasher.GetHashAndReset();
            return hashBytes;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Return a special "error" hash (all zeros) for inaccessible files
            // These will be grouped together and can be skipped
            return new byte[8];
        }
    }

    /// <summary>
    /// Convert hash bytes to ulong for comparison
    /// </summary>
    public static ulong HashBytesToUInt64(byte[] hashBytes)
    {
        if (hashBytes.Length < 8)
        {
            throw new ArgumentException("Hash must be at least 8 bytes", nameof(hashBytes));
        }

        return BinaryPrimitives.ReadUInt64BigEndian(hashBytes);
    }

    /// <summary>
    /// Get human-readable hash string
    /// </summary>
    public static string HashToString(byte[] hashBytes)
    {
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
    }

    /// <summary>
    /// Compute composite hash based on selected components.
    /// This combines file metadata and optionally content into a single hash.
    /// </summary>
    public static byte[] ComputeCompositeHash(
        HashComponents components,
        long size,
        long created,
        long modified,
        string fileName,
        byte[]? contentHash)
    {
        var hasher = new XxHash64();

        // Add components in consistent order
        if ((components & HashComponents.Size) != 0)
        {
            Span<byte> sizeBytes = stackalloc byte[8];
            BinaryPrimitives.WriteInt64LittleEndian(sizeBytes, size);
            hasher.Append(sizeBytes);
        }

        if ((components & HashComponents.CreatedTime) != 0)
        {
            Span<byte> ctimeBytes = stackalloc byte[8];
            BinaryPrimitives.WriteInt64LittleEndian(ctimeBytes, created);
            hasher.Append(ctimeBytes);
        }

        if ((components & HashComponents.ModifiedTime) != 0)
        {
            Span<byte> mtimeBytes = stackalloc byte[8];
            BinaryPrimitives.WriteInt64LittleEndian(mtimeBytes, modified);
            hasher.Append(mtimeBytes);
        }

        if ((components & HashComponents.FileName) != 0)
        {
            // Use lowercase for case-insensitive comparison
            var nameBytes = Encoding.UTF8.GetBytes(fileName.ToLowerInvariant());
            hasher.Append(nameBytes);
        }

        if ((components & HashComponents.Content) != 0 && contentHash != null)
        {
            hasher.Append(contentHash);
        }

        return hasher.GetHashAndReset();
    }

    /// <summary>
    /// Compute composite hash for a file, optionally including content.
    /// If content is requested but not yet computed, returns null (caller should compute content hash first).
    /// </summary>
    public static async Task<byte[]?> ComputeFileHashAsync(
        HashComponents components,
        string path,
        long size,
        long created,
        long modified)
    {
        byte[]? contentHash = null;

        // If content is needed, compute it
        if ((components & HashComponents.Content) != 0)
        {
            contentHash = await ComputeSampledHashAsync(path, size);
        }

        var fileName = Path.GetFileName(path);
        return ComputeCompositeHash(components, size, created, modified, fileName, contentHash);
    }
}
