namespace Ddup;

/// <summary>
/// Interactive console UI for duplicate management
/// </summary>
public class InteractiveUI
{
    /// <summary>
    /// Process duplicate groups from a stream (progressive display)
    /// </summary>
    public static async Task ProcessDuplicatesStreamAsync(IAsyncEnumerable<DuplicateGroup> groupStream, int totalCount)
    {
        var deletedCount = 0;
        var freedSpace = 0L;
        var processedCount = 0;

        await foreach (var group in groupStream)
        {
            processedCount++;

            Console.WriteLine($"=== Group {processedCount} of {totalCount:N0} ===");
            Console.WriteLine($"Size: {DuplicateFinder.FormatSize(group.Size)}");
            if (group.Hash.HasValue)
            {
                Console.WriteLine($"Hash: {group.Hash.Value:X16}");
            }
            Console.WriteLine($"Files ({group.Count}):");
            Console.WriteLine();

            for (int j = 0; j < group.Files.Count; j++)
            {
                var file = group.Files[j];
                Console.WriteLine($"  [{j + 1}] {file.Path}");
                Console.WriteLine($"      Created: {file.CreatedDate:yyyy-MM-dd HH:mm:ss}  Modified: {file.ModifiedDate:yyyy-MM-dd HH:mm:ss}");
            }

            Console.WriteLine();
            Console.WriteLine("Actions:");
            Console.WriteLine("  [K]eep all (skip this group)");
            Console.WriteLine("  [D]elete by number (e.g., 'd 2,3' to delete files 2 and 3)");
            Console.WriteLine("  [O]ldest - keep oldest, delete others");
            Console.WriteLine("  [N]ewest - keep newest, delete others");
            Console.WriteLine("  [Q]uit");
            Console.Write("\nChoice: ");

            var input = Console.ReadLine()?.Trim().ToLowerInvariant();

            if (string.IsNullOrEmpty(input))
            {
                continue;
            }

            if (input == "q")
            {
                Console.WriteLine("\nQuitting...");
                break;
            }

            if (input == "k")
            {
                Console.WriteLine("Skipped.\n");
                continue;
            }

            if (input == "o")
            {
                // Keep oldest (earliest modified date)
                var oldest = group.Files.OrderBy(f => f.Modified).First();
                var toDelete = group.Files.Where(f => f.Id != oldest.Id).ToList();

                if (await ConfirmAndDelete(toDelete))
                {
                    deletedCount += toDelete.Count;
                    freedSpace += toDelete.Count * group.Size;
                }
                continue;
            }

            if (input == "n")
            {
                // Keep newest (latest modified date)
                var newest = group.Files.OrderByDescending(f => f.Modified).First();
                var toDelete = group.Files.Where(f => f.Id != newest.Id).ToList();

                if (await ConfirmAndDelete(toDelete))
                {
                    deletedCount += toDelete.Count;
                    freedSpace += toDelete.Count * group.Size;
                }
                continue;
            }

            if (input.StartsWith("d "))
            {
                // Delete specific files by number
                var numbersStr = input.Substring(2).Split(',', StringSplitOptions.RemoveEmptyEntries);
                var numbers = new HashSet<int>();

                foreach (var numStr in numbersStr)
                {
                    if (int.TryParse(numStr.Trim(), out var num) && num > 0 && num <= group.Files.Count)
                    {
                        numbers.Add(num - 1); // Convert to 0-based index
                    }
                }

                if (numbers.Count == 0)
                {
                    Console.WriteLine("Invalid file numbers.\n");
                    continue;
                }

                if (numbers.Count == group.Files.Count)
                {
                    Console.WriteLine("Cannot delete all files in a group. Keep at least one.\n");
                    continue;
                }

                var toDelete = numbers.Select(idx => group.Files[idx]).ToList();

                if (await ConfirmAndDelete(toDelete))
                {
                    deletedCount += toDelete.Count;
                    freedSpace += toDelete.Count * group.Size;
                }
                continue;
            }

            Console.WriteLine("Invalid choice. Skipping group.\n");
        }

        if (deletedCount > 0)
        {
            Console.WriteLine($"\n=== Summary ===");
            Console.WriteLine($"Deleted {deletedCount} duplicate files");
            Console.WriteLine($"Freed space: {DuplicateFinder.FormatSize(freedSpace)}");
        }
    }

    /// <summary>
    /// Display duplicate groups and let user choose actions (legacy - loads all first)
    /// </summary>
    public static async Task ProcessDuplicatesAsync(List<DuplicateGroup> groups)
    {
        if (groups.Count == 0)
        {
            Console.WriteLine("No duplicates found!");
            return;
        }

        var stats = DuplicateFinder.CalculateStats(groups);
        Console.WriteLine($"\nFound {stats.GroupCount} duplicate groups");
        Console.WriteLine($"Total duplicate files: {stats.TotalDuplicates}");
        Console.WriteLine($"Wasted space: {DuplicateFinder.FormatSize(stats.WastedSpace)}");
        Console.WriteLine();

        var deletedCount = 0;
        var freedSpace = 0L;

        for (int i = 0; i < groups.Count; i++)
        {
            var group = groups[i];

            Console.WriteLine($"=== Group {i + 1} of {groups.Count} ===");
            Console.WriteLine($"Size: {DuplicateFinder.FormatSize(group.Size)}");
            if (group.Hash.HasValue)
            {
                Console.WriteLine($"Hash: {group.Hash.Value:X16}");
            }
            Console.WriteLine($"Files ({group.Count}):");
            Console.WriteLine();

            for (int j = 0; j < group.Files.Count; j++)
            {
                var file = group.Files[j];
                Console.WriteLine($"  [{j + 1}] {file.Path}");
                Console.WriteLine($"      Created: {file.CreatedDate:yyyy-MM-dd HH:mm:ss}  Modified: {file.ModifiedDate:yyyy-MM-dd HH:mm:ss}");
            }

            Console.WriteLine();
            Console.WriteLine("Actions:");
            Console.WriteLine("  [K]eep all (skip this group)");
            Console.WriteLine("  [D]elete by number (e.g., 'd 2,3' to delete files 2 and 3)");
            Console.WriteLine("  [O]ldest - keep oldest, delete others");
            Console.WriteLine("  [N]ewest - keep newest, delete others");
            Console.WriteLine("  [Q]uit");
            Console.Write("\nChoice: ");

            var input = Console.ReadLine()?.Trim().ToLowerInvariant();

            if (string.IsNullOrEmpty(input))
            {
                continue;
            }

            if (input == "q")
            {
                Console.WriteLine("\nQuitting...");
                break;
            }

            if (input == "k")
            {
                Console.WriteLine("Skipped.\n");
                continue;
            }

            if (input == "o")
            {
                // Keep oldest (earliest modified date)
                var oldest = group.Files.OrderBy(f => f.Modified).First();
                var toDelete = group.Files.Where(f => f.Id != oldest.Id).ToList();

                if (await ConfirmAndDelete(toDelete))
                {
                    deletedCount += toDelete.Count;
                    freedSpace += toDelete.Count * group.Size;
                }
                continue;
            }

            if (input == "n")
            {
                // Keep newest (latest modified date)
                var newest = group.Files.OrderByDescending(f => f.Modified).First();
                var toDelete = group.Files.Where(f => f.Id != newest.Id).ToList();

                if (await ConfirmAndDelete(toDelete))
                {
                    deletedCount += toDelete.Count;
                    freedSpace += toDelete.Count * group.Size;
                }
                continue;
            }

            if (input.StartsWith("d "))
            {
                // Delete specific files by number
                var numbersStr = input.Substring(2).Split(',', StringSplitOptions.RemoveEmptyEntries);
                var numbers = new HashSet<int>();

                foreach (var numStr in numbersStr)
                {
                    if (int.TryParse(numStr.Trim(), out var num) && num > 0 && num <= group.Files.Count)
                    {
                        numbers.Add(num - 1); // Convert to 0-based index
                    }
                }

                if (numbers.Count == 0)
                {
                    Console.WriteLine("Invalid file numbers.\n");
                    continue;
                }

                if (numbers.Count == group.Files.Count)
                {
                    Console.WriteLine("Cannot delete all files in a group. Keep at least one.\n");
                    continue;
                }

                var toDelete = numbers.Select(idx => group.Files[idx]).ToList();

                if (await ConfirmAndDelete(toDelete))
                {
                    deletedCount += toDelete.Count;
                    freedSpace += toDelete.Count * group.Size;
                }
                continue;
            }

            Console.WriteLine("Invalid choice. Skipping group.\n");
        }

        if (deletedCount > 0)
        {
            Console.WriteLine($"\n=== Summary ===");
            Console.WriteLine($"Deleted {deletedCount} duplicate files");
            Console.WriteLine($"Freed space: {DuplicateFinder.FormatSize(freedSpace)}");
        }
    }

    /// <summary>
    /// Confirm and delete files
    /// </summary>
    private static async Task<bool> ConfirmAndDelete(List<FileRecord> files)
    {
        Console.WriteLine($"\nWill delete {files.Count} file(s):");
        foreach (var file in files)
        {
            Console.WriteLine($"  - {file.Path}");
        }

        Console.Write("Confirm deletion? [y/N]: ");
        var confirm = Console.ReadLine()?.Trim().ToLowerInvariant();

        if (confirm != "y" && confirm != "yes")
        {
            Console.WriteLine("Cancelled.\n");
            return false;
        }

        var deleted = 0;
        foreach (var file in files)
        {
            try
            {
                File.Delete(file.Path);
                deleted++;
                Console.WriteLine($"Deleted: {file.Path}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to delete {file.Path}: {ex.Message}");
            }
        }

        Console.WriteLine($"Successfully deleted {deleted} of {files.Count} files.\n");
        return true;
    }

    /// <summary>
    /// Display scan progress
    /// </summary>
    public static void DisplayScanProgress(ScanProgress progress)
    {
        if (progress.Error != null)
        {
            Console.WriteLine($"Error: {progress.Error}");
            return;
        }

        if (progress.FilesScanned % 1000 == 0)
        {
            Console.Write($"\rScanned: {progress.FilesScanned:N0} | Updated: {progress.FilesUpdated:N0} | Skipped: {progress.FilesSkipped:N0}");
        }
    }

    /// <summary>
    /// Display hash progress
    /// </summary>
    public static void DisplayHashProgress(HashProgress progress)
    {
        if (progress.Error != null)
        {
            Console.WriteLine($"\nError hashing {progress.CurrentPath}: {progress.Error}");
            return;
        }

        if (progress.TotalFiles > 0)
        {
            var percent = (progress.FilesProcessed * 100.0) / progress.TotalFiles;
            Console.Write($"\rHashing: {progress.FilesProcessed:N0} / {progress.TotalFiles:N0} ({percent:F1}%)");
        }
    }
}
