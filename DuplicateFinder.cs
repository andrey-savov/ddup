namespace Ddup;

/// <summary>
/// Find and organize duplicate files
/// </summary>
public class DuplicateFinder
{
    private readonly Database _db;

    public DuplicateFinder(Database db)
    {
        _db = db;
    }

    /// <summary>
    /// Count duplicate size groups without fetching data (fast)
    /// </summary>
    public async Task<int> CountBySizeAsync()
    {
        return await _db.CountDuplicateSizesAsync();
    }

    /// <summary>
    /// Find duplicates by size only (fast, may have false positives)
    /// </summary>
    public async Task<List<DuplicateGroup>> FindBySizeAsync()
    {
        var sizes = await _db.GetDuplicateSizesAsync();

        // Fetch all file groups in parallel
        var groupTasks = sizes.Select(async size =>
        {
            var files = await _db.GetFilesBySizeAsync(size);

            if (files.Count > 1)
            {
                return new DuplicateGroup
                {
                    Size = size,
                    Hash = null,
                    Files = files
                };
            }

            return null;
        });

        var allGroups = await Task.WhenAll(groupTasks);

        // Filter out null results
        return allGroups.Where(g => g != null).ToList()!;
    }

    /// <summary>
    /// Stream duplicates by size as they're found (for progressive display)
    /// </summary>
    public async IAsyncEnumerable<DuplicateGroup> StreamBySizeAsync()
    {
        var sizes = await _db.GetDuplicateSizesAsync();

        // Process sizes in parallel batches for better throughput
        const int batchSize = 100;
        for (int i = 0; i < sizes.Count; i += batchSize)
        {
            var batch = sizes.Skip(i).Take(batchSize).ToList();

            var groupTasks = batch.Select(async size =>
            {
                var files = await _db.GetFilesBySizeAsync(size);

                if (files.Count > 1)
                {
                    return new DuplicateGroup
                    {
                        Size = size,
                        Hash = null,
                        Files = files
                    };
                }

                return null;
            });

            var groups = await Task.WhenAll(groupTasks);

            // Yield each non-null group as soon as batch completes
            foreach (var group in groups)
            {
                if (group != null)
                {
                    yield return group;
                }
            }
        }
    }

    /// <summary>
    /// Count duplicate hash groups without fetching data (fast)
    /// </summary>
    public async Task<int> CountByHashAsync()
    {
        return await _db.CountDuplicateHashesAsync();
    }

    /// <summary>
    /// Find duplicates by content hash (accurate)
    /// </summary>
    public async Task<List<DuplicateGroup>> FindByHashAsync()
    {
        return await _db.GetDuplicatesByHashAsync();
    }

    /// <summary>
    /// Stream duplicates by hash as they're found (for progressive display)
    /// </summary>
    public async IAsyncEnumerable<DuplicateGroup> StreamByHashAsync()
    {
        var hashes = await _db.GetDuplicateHashesAsync();

        // Process hashes in parallel batches for better throughput
        const int batchSize = 100;
        for (int i = 0; i < hashes.Count; i += batchSize)
        {
            var batch = hashes.Skip(i).Take(batchSize).ToList();

            var groupTasks = batch.Select(async hash =>
            {
                var files = await _db.GetFilesByHashAsync(hash);

                if (files.Count > 1)
                {
                    return new DuplicateGroup
                    {
                        Size = files[0].Size,
                        Hash = BitConverter.ToUInt64(hash),
                        Files = files
                    };
                }

                return null;
            });

            var groups = await Task.WhenAll(groupTasks);

            // Yield each non-null group as soon as batch completes
            foreach (var group in groups)
            {
                if (group != null)
                {
                    yield return group;
                }
            }
        }
    }

    /// <summary>
    /// Get statistics about duplicates
    /// </summary>
    public static DuplicateStats CalculateStats(List<DuplicateGroup> groups)
    {
        var totalDuplicates = 0L;
        var wasted = 0L;
        var groupCount = groups.Count;

        foreach (var group in groups)
        {
            // All files except the first are duplicates
            var duplicateCount = group.Count - 1;
            totalDuplicates += duplicateCount;
            wasted += group.Size * duplicateCount;
        }

        return new DuplicateStats
        {
            GroupCount = groupCount,
            TotalDuplicates = totalDuplicates,
            WastedSpace = wasted
        };
    }

    /// <summary>
    /// Format file size for display
    /// </summary>
    public static string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;

        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }
}

/// <summary>
/// Statistics about found duplicates
/// </summary>
public record DuplicateStats
{
    public int GroupCount { get; init; }
    public long TotalDuplicates { get; init; }
    public long WastedSpace { get; init; }
}
