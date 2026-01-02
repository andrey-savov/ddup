using Microsoft.Data.Sqlite;
using Dapper;

namespace Ddup;

/// <summary>
/// SQLite database manager for persistent file index
/// </summary>
public class Database : IDisposable
{
    private readonly string _connectionString;
    private long _currentScanId;

    public Database(string dbPath)
    {
        var directory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = $"Data Source={dbPath};Mode=ReadWriteCreate;Cache=Shared";

        // Initialize schema with a dedicated connection
        using var connection = CreateConnection();
        InitializeSchema(connection);
        _currentScanId = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    private SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        // Enable WAL mode for better concurrent access
        connection.Execute("PRAGMA journal_mode=WAL;");
        return connection;
    }

    private void InitializeSchema(SqliteConnection connection)
    {
        connection.Execute(@"
            CREATE TABLE IF NOT EXISTS files (
                id INTEGER PRIMARY KEY,
                path TEXT NOT NULL UNIQUE,
                size INTEGER NOT NULL,
                modified INTEGER NOT NULL,
                created INTEGER NOT NULL DEFAULT 0,
                hash BLOB,
                scan_id INTEGER NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_size ON files(size);
            CREATE INDEX IF NOT EXISTS idx_hash ON files(hash) WHERE hash IS NOT NULL;
            CREATE INDEX IF NOT EXISTS idx_scan ON files(scan_id);

            CREATE TABLE IF NOT EXISTS config (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
        ");
    }

    public long CurrentScanId => _currentScanId;

    /// <summary>
    /// Get existing file record by path
    /// </summary>
    public async Task<FileRecord?> GetFileAsync(string path)
    {
        using var connection = CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<FileRecord>(
            "SELECT id AS Id, path AS Path, size AS Size, modified AS Modified, created AS Created, hash AS Hash, scan_id AS ScanId FROM files WHERE path = @path",
            new { path });
    }

    /// <summary>
    /// Insert or update file record
    /// </summary>
    public async Task UpsertFileAsync(string path, long size, long modified, long created, byte[]? hash = null)
    {
        using var connection = CreateConnection();
        await connection.ExecuteAsync(@"
            INSERT INTO files (path, size, modified, created, hash, scan_id)
            VALUES (@path, @size, @modified, @created, @hash, @scanId)
            ON CONFLICT(path) DO UPDATE SET
                size = @size,
                modified = @modified,
                created = @created,
                hash = COALESCE(@hash, hash),
                scan_id = @scanId
        ", new { path, size, modified, created, hash, scanId = _currentScanId });
    }

    /// <summary>
    /// Get config value
    /// </summary>
    public async Task<string?> GetConfigAsync(string key)
    {
        using var connection = CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<string>(
            "SELECT value FROM config WHERE key = @key",
            new { key });
    }

    /// <summary>
    /// Set config value
    /// </summary>
    public async Task SetConfigAsync(string key, string value)
    {
        using var connection = CreateConnection();
        await connection.ExecuteAsync(@"
            INSERT INTO config (key, value) VALUES (@key, @value)
            ON CONFLICT(key) DO UPDATE SET value = @value
        ", new { key, value });
    }

    /// <summary>
    /// Update hash for a file
    /// </summary>
    public async Task UpdateHashAsync(string path, byte[] hash)
    {
        using var connection = CreateConnection();
        await connection.ExecuteAsync(
            "UPDATE files SET hash = @hash WHERE path = @path",
            new { path, hash });
    }

    /// <summary>
    /// Update scan_id for a file (to include unchanged files in current scan)
    /// </summary>
    public async Task UpdateScanIdAsync(string path)
    {
        using var connection = CreateConnection();
        await connection.ExecuteAsync(
            "UPDATE files SET scan_id = @scanId WHERE path = @path",
            new { path, scanId = _currentScanId });
    }

    /// <summary>
    /// Get sizes that have multiple files (potential duplicates)
    /// </summary>
    public async Task<List<long>> GetDuplicateSizesAsync()
    {
        using var connection = CreateConnection();
        var sizes = await connection.QueryAsync<long>(@"
            SELECT size
            FROM files
            WHERE scan_id = @scanId
            GROUP BY size
            HAVING COUNT(*) > 1
            ORDER BY size DESC
        ", new { scanId = _currentScanId });

        return sizes.ToList();
    }

    /// <summary>
    /// Count sizes that have multiple files (fast count-only query)
    /// </summary>
    public async Task<int> CountDuplicateSizesAsync()
    {
        using var connection = CreateConnection();
        return await connection.QuerySingleAsync<int>(@"
            SELECT COUNT(*) FROM (
                SELECT size
                FROM files
                WHERE scan_id = @scanId
                GROUP BY size
                HAVING COUNT(*) > 1
            )
        ", new { scanId = _currentScanId });
    }

    /// <summary>
    /// Get all files with a specific size
    /// </summary>
    public async Task<List<FileRecord>> GetFilesBySizeAsync(long size)
    {
        using var connection = CreateConnection();
        var files = await connection.QueryAsync<FileRecord>(
            "SELECT id AS Id, path AS Path, size AS Size, modified AS Modified, created AS Created, hash AS Hash, scan_id AS ScanId FROM files WHERE size = @size AND scan_id = @scanId ORDER BY path",
            new { size, scanId = _currentScanId });

        return files.ToList();
    }

    /// <summary>
    /// Count hashes that have multiple files (fast count-only query)
    /// </summary>
    public async Task<int> CountDuplicateHashesAsync()
    {
        using var connection = CreateConnection();
        return await connection.QuerySingleAsync<int>(@"
            SELECT COUNT(*) FROM (
                SELECT hash
                FROM files
                WHERE scan_id = @scanId AND hash IS NOT NULL
                GROUP BY hash
                HAVING COUNT(*) > 1
            )
        ", new { scanId = _currentScanId });
    }

    /// <summary>
    /// Get hashes that have multiple files (potential duplicates), ordered by size descending
    /// </summary>
    public async Task<List<byte[]>> GetDuplicateHashesAsync()
    {
        using var connection = CreateConnection();
        var hashes = await connection.QueryAsync<byte[]>(@"
            SELECT hash
            FROM files
            WHERE scan_id = @scanId AND hash IS NOT NULL
            GROUP BY hash
            HAVING COUNT(*) > 1
            ORDER BY MAX(size) DESC
        ", new { scanId = _currentScanId });

        return hashes.ToList();
    }

    /// <summary>
    /// Get all files with a specific hash
    /// </summary>
    public async Task<List<FileRecord>> GetFilesByHashAsync(byte[] hash)
    {
        using var connection = CreateConnection();
        var files = await connection.QueryAsync<FileRecord>(
            "SELECT id AS Id, path AS Path, size AS Size, modified AS Modified, created AS Created, hash AS Hash, scan_id AS ScanId FROM files WHERE hash = @hash AND scan_id = @scanId ORDER BY path",
            new { hash, scanId = _currentScanId });

        return files.ToList();
    }

    /// <summary>
    /// Get files grouped by hash (for content-based deduplication)
    /// </summary>
    public async Task<List<DuplicateGroup>> GetDuplicatesByHashAsync()
    {
        // First, get all hashes that have duplicates
        var hashes = await GetDuplicateHashesAsync();

        // Fetch all file groups in parallel
        var groupTasks = hashes.Select(async hash =>
        {
            var files = await GetFilesByHashAsync(hash);

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

        var allGroups = await Task.WhenAll(groupTasks);

        // Filter out null results
        return allGroups.Where(g => g != null).ToList()!;
    }

    /// <summary>
    /// Delete old scan data (cleanup)
    /// </summary>
    public async Task DeleteOldScansAsync(int keepCount = 2)
    {
        using var connection = CreateConnection();
        await connection.ExecuteAsync(@"
            DELETE FROM files
            WHERE scan_id NOT IN (
                SELECT DISTINCT scan_id
                FROM files
                ORDER BY scan_id DESC
                LIMIT @keepCount
            )
        ", new { keepCount });
    }

    /// <summary>
    /// Get database statistics
    /// </summary>
    public async Task<(long totalFiles, long totalSize, long currentScanFiles)> GetStatsAsync()
    {
        using var connection = CreateConnection();
        var total = await connection.QuerySingleAsync<(long count, long size)>(
            "SELECT COUNT(*), COALESCE(SUM(size), 0) FROM files");

        var currentCount = await connection.QuerySingleAsync<long>(
            "SELECT COUNT(*) FROM files WHERE scan_id = @scanId",
            new { scanId = _currentScanId });

        return (total.count, total.size, currentCount);
    }

    public void Dispose()
    {
        // No persistent connections to dispose
        // SQLite.Interop will handle connection pool cleanup
    }
}
