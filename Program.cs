using System.CommandLine;

namespace Ddup;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootPathArgument = new Argument<string>(
            name: "path",
            description: "Root directory to scan for duplicates");

        // Hash component options: use + to include, - to exclude
        // Defaults: size and ctime are included
        var contentOption = new Option<string?>(
            name: "--content",
            description: "Include (+) or exclude (-) file content in hash [default: -]");

        var sizeOption = new Option<string?>(
            name: "--size",
            description: "Include (+) or exclude (-) file size in hash [default: +]");

        var mtimeOption = new Option<string?>(
            name: "--mtime",
            description: "Include (+) or exclude (-) modification time in hash [default: -]");

        var ctimeOption = new Option<string?>(
            name: "--ctime",
            description: "Include (+) or exclude (-) creation time in hash [default: -]");

        var nameOption = new Option<string?>(
            name: "--name",
            description: "Include (+) or exclude (-) file name (case insensitive) in hash [default: -]");

        var workersOption = new Option<int>(
            name: "--workers",
            description: "Number of parallel workers",
            getDefaultValue: () => Environment.ProcessorCount);

        var dbPathOption = new Option<string>(
            name: "--db",
            description: "Database location",
            getDefaultValue: () => ".dups.db");

        var fullScanOption = new Option<bool>(
            name: "--full-scan",
            description: "Re-scan everything (ignore cache)",
            getDefaultValue: () => false);

        var rootCommand = new RootCommand("High-performance file deduplication tool")
        {
            rootPathArgument,
            contentOption,
            sizeOption,
            mtimeOption,
            ctimeOption,
            nameOption,
            workersOption,
            dbPathOption,
            fullScanOption
        };

        rootCommand.SetHandler(async (context) =>
        {
            var path = context.ParseResult.GetValueForArgument(rootPathArgument);
            var content = context.ParseResult.GetValueForOption(contentOption);
            var size = context.ParseResult.GetValueForOption(sizeOption);
            var mtime = context.ParseResult.GetValueForOption(mtimeOption);
            var ctime = context.ParseResult.GetValueForOption(ctimeOption);
            var name = context.ParseResult.GetValueForOption(nameOption);
            var workers = context.ParseResult.GetValueForOption(workersOption);
            var dbPath = context.ParseResult.GetValueForOption(dbPathOption)!;
            var fullScan = context.ParseResult.GetValueForOption(fullScanOption);

            // Build HashComponents flags: start with defaults (size only)
            var hashComponents = HashComponents.Size;

            // Apply overrides: + includes, - excludes
            hashComponents = ApplyComponentOption(hashComponents, HashComponents.Content, content);
            hashComponents = ApplyComponentOption(hashComponents, HashComponents.Size, size);
            hashComponents = ApplyComponentOption(hashComponents, HashComponents.ModifiedTime, mtime);
            hashComponents = ApplyComponentOption(hashComponents, HashComponents.CreatedTime, ctime);
            hashComponents = ApplyComponentOption(hashComponents, HashComponents.FileName, name);

            await RunAsync(new ScanOptions
            {
                RootPath = path,
                HashComponents = hashComponents,
                Workers = workers,
                DbPath = dbPath,
                FullScan = fullScan
            });
        });

        return await rootCommand.InvokeAsync(args);
    }

    static HashComponents ApplyComponentOption(HashComponents current, HashComponents flag, string? value)
    {
        if (string.IsNullOrEmpty(value)) return current;
        
        return value.Trim() switch
        {
            "+" => current | flag,
            "-" => current & ~flag,
            _ => current
        };
    }

    static string FormatHashComponents(HashComponents components)
    {
        var parts = new List<string>();
        if ((components & HashComponents.Size) != 0) parts.Add("size");
        if ((components & HashComponents.CreatedTime) != 0) parts.Add("ctime");
        if ((components & HashComponents.ModifiedTime) != 0) parts.Add("mtime");
        if ((components & HashComponents.FileName) != 0) parts.Add("name");
        if ((components & HashComponents.Content) != 0) parts.Add("content");
        return parts.Count > 0 ? string.Join(" + ", parts) : "none";
    }

    static async Task RunAsync(ScanOptions options)
    {
        try
        {
            Console.WriteLine("ddup - High-Performance File Deduplication Tool");
            Console.WriteLine($"Scanning: {options.RootPath}");
            Console.WriteLine($"Workers: {options.Workers}");
            Console.WriteLine($"Database: {options.DbPath}");
            Console.WriteLine($"Hashing by: {FormatHashComponents(options.HashComponents)}");
            Console.WriteLine($"Mode: {(options.FullScan ? "Full scan" : "Incremental")}");
            Console.WriteLine();

            // Verify root path exists
            if (!Directory.Exists(options.RootPath))
            {
                Console.WriteLine($"Error: Directory not found: {options.RootPath}");
                return;
            }

            using var db = new Database(options.DbPath);

            // Check if hash components changed since last scan
            var storedComponents = await db.GetConfigAsync("hash_components");
            var currentComponentsStr = ((int)options.HashComponents).ToString();
            var forceFullScan = options.FullScan;

            if (storedComponents != null && storedComponents != currentComponentsStr)
            {
                Console.WriteLine("Hash configuration changed, forcing full rescan.");
                Console.WriteLine();
                forceFullScan = true;
            }

            // Update stored hash components
            await db.SetConfigAsync("hash_components", currentComponentsStr);

            var effectiveOptions = forceFullScan && !options.FullScan
                ? options with { FullScan = true }
                : options;

            // Phase 1: Scan files
            Console.WriteLine("Phase 1: Scanning files...");
            var scanner = new FileScanner(db, effectiveOptions);
            var scanProgress = new Progress<ScanProgress>(InteractiveUI.DisplayScanProgress);
            await scanner.ScanAsync(scanProgress);

            Console.WriteLine($"\nScanned: {scanner.FilesScanned:N0} files");
            Console.WriteLine($"Updated: {scanner.FilesUpdated:N0} files");
            Console.WriteLine($"Skipped: {scanner.FilesSkipped:N0} files (unchanged)");
            Console.WriteLine();

            // Phase 2: Content hashing (if content is included in hash components)
            if (effectiveOptions.IncludesContent)
            {
                Console.WriteLine("Phase 2: Hashing file content for duplicate candidates...");
                var hashProgress = new Progress<HashProgress>(InteractiveUI.DisplayHashProgress);
                await scanner.HashDuplicatesAsync(hashProgress);
                Console.WriteLine("\nHashing complete.");
                Console.WriteLine();
            }

            // Phase 3: Find duplicates by hash
            Console.WriteLine($"Phase {(effectiveOptions.IncludesContent ? "3" : "2")}: Finding duplicates by hash...");
            var finder = new DuplicateFinder(db);
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var groupCount = await finder.CountByHashAsync();
            stopwatch.Stop();

            if (groupCount == 0)
            {
                Console.WriteLine("No duplicates found.");
                return;
            }

            Console.WriteLine($"Found {groupCount:N0} duplicate groups");
            Console.WriteLine($"Time: {stopwatch.ElapsedMilliseconds}ms");
            Console.WriteLine();

            // Interactive mode
            Console.WriteLine("Interactive Mode");
            Console.WriteLine("================");
            await InteractiveUI.ProcessDuplicatesStreamAsync(finder.StreamByHashAsync(), groupCount);

            // Cleanup old scans
            await db.DeleteOldScansAsync(keepCount: 2);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nFatal error: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }
}
