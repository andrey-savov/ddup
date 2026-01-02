using System.Threading.Channels;

namespace Ddup;

/// <summary>
/// High-performance parallel file scanner using channels
/// </summary>
public class FileScanner
{
    private readonly Database _db;
    private readonly ScanOptions _options;
    private long _filesScanned;
    private long _filesSkipped;
    private long _filesUpdated;

    public FileScanner(Database db, ScanOptions options)
    {
        _db = db;
        _options = options;
    }

    public long FilesScanned => Interlocked.Read(ref _filesScanned);
    public long FilesSkipped => Interlocked.Read(ref _filesSkipped);
    public long FilesUpdated => Interlocked.Read(ref _filesUpdated);

    /// <summary>
    /// Scan directory tree and populate database
    /// </summary>
    public async Task ScanAsync(IProgress<ScanProgress>? progress = null)
    {
        var channel = Channel.CreateBounded<FileInfo>(new BoundedChannelOptions(10000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = true
        });

        // Producer task: enumerate all files
        var producerTask = Task.Run(async () =>
        {
            try
            {
                await EnumerateFilesAsync(_options.RootPath, channel.Writer, progress);
            }
            finally
            {
                channel.Writer.Complete();
            }
        });

        // Consumer tasks: process files in parallel
        var consumerTasks = Enumerable.Range(0, _options.Workers)
            .Select(_ => ProcessFilesAsync(channel.Reader, progress))
            .ToArray();

        await Task.WhenAll(consumerTasks);
        await producerTask;
    }

    /// <summary>
    /// Recursively enumerate all files and write to channel
    /// </summary>
    private async Task EnumerateFilesAsync(
        string path,
        ChannelWriter<FileInfo> writer,
        IProgress<ScanProgress>? progress)
    {
        var queue = new Queue<string>();
        queue.Enqueue(path);

        while (queue.Count > 0)
        {
            var currentPath = queue.Dequeue();

            try
            {
                // Enumerate subdirectories
                foreach (var directory in Directory.EnumerateDirectories(currentPath))
                {
                    queue.Enqueue(directory);
                }

                // Enumerate files
                foreach (var file in Directory.EnumerateFiles(currentPath))
                {
                    try
                    {
                        var fileInfo = new FileInfo(file);
                        await writer.WriteAsync(fileInfo);
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                    {
                        // Skip inaccessible files
                        Interlocked.Increment(ref _filesSkipped);
                    }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                // Skip inaccessible directories
                progress?.Report(new ScanProgress
                {
                    CurrentPath = currentPath,
                    Error = $"Skipped: {ex.Message}"
                });
            }
        }
    }

    /// <summary>
    /// Process files from channel (consumer)
    /// </summary>
    private async Task ProcessFilesAsync(
        ChannelReader<FileInfo> reader,
        IProgress<ScanProgress>? progress)
    {
        await foreach (var fileInfo in reader.ReadAllAsync())
        {
            try
            {
                await ProcessFileAsync(fileInfo);

                // Report progress periodically
                var scanned = Interlocked.Increment(ref _filesScanned);
                if (scanned % 1000 == 0)
                {
                    progress?.Report(new ScanProgress
                    {
                        FilesScanned = scanned,
                        FilesSkipped = _filesSkipped,
                        FilesUpdated = _filesUpdated,
                        CurrentPath = fileInfo.FullName
                    });
                }
            }
            catch (Exception ex)
            {
                progress?.Report(new ScanProgress
                {
                    CurrentPath = fileInfo.FullName,
                    Error = $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}"
                });
            }
        }
    }

    /// <summary>
    /// Process individual file (check if changed, update database)
    /// </summary>
    private async Task ProcessFileAsync(FileInfo fileInfo)
    {
        var path = fileInfo.FullName;
        var size = fileInfo.Length;
        var modified = new DateTimeOffset(fileInfo.LastWriteTimeUtc).ToUnixTimeSeconds();
        var created = PlatformUtils.GetCreationTimeUnix(path);

        // Check if file exists in database
        if (_options.Incremental && !_options.FullScan)
        {
            var existing = await _db.GetFileAsync(path);

            if (existing != null &&
                existing.Size == size &&
                existing.Modified == modified &&
                existing.Created == created)
            {
                // File unchanged, but update scan_id to include in current scan
                await _db.UpdateScanIdAsync(path);
                Interlocked.Increment(ref _filesSkipped);
                return;
            }
        }

        // Compute hash based on components (without content if content is included)
        byte[]? hash = null;
        if (!_options.IncludesContent)
        {
            // Compute metadata-only hash immediately
            var fileName = Path.GetFileName(path);
            hash = HashComputer.ComputeCompositeHash(
                _options.HashComponents,
                size,
                created,
                modified,
                fileName,
                contentHash: null);
        }

        // File is new or changed, update database
        await _db.UpsertFileAsync(path, size, modified, created, hash);
        Interlocked.Increment(ref _filesUpdated);
    }

    /// <summary>
    /// Hash content for files in duplicate size groups (when --hash-content is enabled)
    /// </summary>
    public async Task HashDuplicatesAsync(IProgress<HashProgress>? progress = null)
    {
        var sizes = await _db.GetDuplicateSizesAsync();

        var totalFiles = 0L;
        foreach (var size in sizes)
        {
            var files = await _db.GetFilesBySizeAsync(size);
            totalFiles += files.Count;
        }

        progress?.Report(new HashProgress { TotalFiles = totalFiles });

        var processed = 0L;

        foreach (var size in sizes)
        {
            var files = await _db.GetFilesBySizeAsync(size);

            // Hash files in parallel
            var hashTasks = files.Select(async file =>
            {
                try
                {
                    // First compute content hash
                    var contentHash = await HashComputer.ComputeSampledHashAsync(file.Path, file.Size);

                    // Then compute composite hash including content
                    var fileName = Path.GetFileName(file.Path);
                    var compositeHash = HashComputer.ComputeCompositeHash(
                        _options.HashComponents,
                        file.Size,
                        file.Created,
                        file.Modified,
                        fileName,
                        contentHash);

                    await _db.UpdateHashAsync(file.Path, compositeHash);

                    var current = Interlocked.Increment(ref processed);
                    progress?.Report(new HashProgress
                    {
                        FilesProcessed = current,
                        TotalFiles = totalFiles,
                        CurrentPath = file.Path
                    });
                }
                catch (Exception ex)
                {
                    progress?.Report(new HashProgress
                    {
                        FilesProcessed = Interlocked.Increment(ref processed),
                        TotalFiles = totalFiles,
                        CurrentPath = file.Path,
                        Error = ex.Message
                    });
                }
            });

            await Task.WhenAll(hashTasks);
        }
    }
}

/// <summary>
/// Progress information for file scanning
/// </summary>
public record ScanProgress
{
    public long FilesScanned { get; init; }
    public long FilesSkipped { get; init; }
    public long FilesUpdated { get; init; }
    public string? CurrentPath { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Progress information for hashing
/// </summary>
public record HashProgress
{
    public long FilesProcessed { get; init; }
    public long TotalFiles { get; init; }
    public string? CurrentPath { get; init; }
    public string? Error { get; init; }
}
