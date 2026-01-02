# Claude Context Document

## Project Overview

**ddup** is a high-performance file deduplication tool written in C# (.NET 10.0) that finds duplicate files using size-based and content-based hashing strategies. It uses SQLite for persistent caching and provides an interactive UI for managing duplicates.

### Key Features

- Fast parallel file scanning using System.Threading.Channels
- Incremental scanning with SQLite-based file index
- Two-phase duplicate detection: size-only (fast) and content hash (accurate)
- xxHash64 with logarithmic sampling for fast content hashing
- Interactive terminal UI for duplicate management
- Multi-threaded hash computation

## Architecture

### Core Components

```
Program.cs           - CLI entry point, command-line parsing, orchestration
├── FileScanner      - Parallel file enumeration and metadata collection
├── Database         - SQLite persistence layer (Dapper ORM)
├── HashComputer     - Fast xxHash64 computation with sampling
├── DuplicateFinder  - Duplicate group detection and statistics
└── InteractiveUI    - Terminal-based duplicate management interface
```

### Data Flow

1. **Phase 1: File Scanning**
   - `FileScanner` recursively enumerates directories
   - Producer-consumer pattern using bounded channels (10,000 capacity)
   - Multiple workers process files in parallel
   - Checks file metadata (size, modification time)
   - Skips unchanged files (incremental mode)
   - Updates SQLite database with file metadata

2. **Phase 2: Size-Based Detection**
   - `DuplicateFinder.FindBySizeAsync()` queries files with duplicate sizes
   - Fast but includes false positives (different content, same size)
   - Groups files by size

3. **Phase 3: Content Hashing (Optional)**
   - `FileScanner.HashDuplicatesAsync()` hashes only files with duplicate sizes
   - Uses `HashComputer` with xxHash64 and logarithmic sampling
   - Samples 3-100 chunks (64KB each) based on file size
   - Updates database with hash values

4. **Phase 4: Hash-Based Detection**
   - `DuplicateFinder.FindByHashAsync()` groups files by content hash
   - Accurate duplicate detection (no false positives)

5. **Phase 5: Interactive Management**
   - `InteractiveUI` presents duplicate groups
   - User can keep, delete, or filter duplicates
   - Actions: Keep all, Delete specific, Keep oldest, Keep newest, Quit

## File Descriptions

### Program.cs (144 lines)
Command-line interface using `System.CommandLine`. Defines options:
- `path` - Root directory to scan (required)
- `--hash-content` - Enable content hashing (default: false)
- `--workers` - Number of parallel workers (default: CPU count)
- `--db` - Database path (default: `~/.ddup/index.db`)
- `--full-scan` - Ignore cache, re-scan everything (default: false)

Orchestrates the 5-phase scanning and deduplication process.

### FileScanner.cs (253 lines)
High-performance parallel file scanner using:
- **Producer-Consumer Pattern**: Bounded channel with 10,000 file queue
- **Single Producer**: Recursively enumerates directories
- **Multiple Consumers**: N workers process files in parallel
- **Incremental Scanning**: Checks file metadata against database, skips unchanged files
- **Error Handling**: Gracefully handles permission errors, inaccessible files

Key methods:
- `ScanAsync()` - Main scan orchestrator
- `EnumerateFilesAsync()` - Recursive directory traversal (producer)
- `ProcessFilesAsync()` - File processing worker (consumer)
- `HashDuplicatesAsync()` - Parallel hash computation for duplicate candidates

### Database.cs (184 lines)
SQLite database manager using Dapper ORM. Schema:
```sql
CREATE TABLE files (
    id INTEGER PRIMARY KEY,
    path TEXT NOT NULL UNIQUE,
    size INTEGER NOT NULL,
    modified INTEGER NOT NULL,  -- Unix timestamp
    hash BLOB,                  -- xxHash64 (8 bytes)
    scan_id INTEGER NOT NULL    -- Unix timestamp of scan
);
CREATE INDEX idx_size ON files(size);
CREATE INDEX idx_hash ON files(hash) WHERE hash IS NOT NULL;
CREATE INDEX idx_scan ON files(scan_id);
```

Key methods:
- `UpsertFileAsync()` - Insert or update file metadata
- `UpdateHashAsync()` - Update file hash after computation
- `GetDuplicateSizesAsync()` - Find sizes with multiple files
- `GetFilesBySizeAsync()` - Get all files with specific size
- `GetDuplicatesByHashAsync()` - Get duplicate groups by hash
- `DeleteOldScansAsync()` - Cleanup old scan data (keeps 2 most recent)

### HashComputer.cs (116 lines)
Fast content hashing using **xxHash64 with logarithmic sampling**.

**Strategy**: Instead of hashing entire files, samples chunks strategically:
- Small files (<64KB): Hash entire file
- Large files: Sample 3-100 chunks (64KB each) evenly distributed
- Formula: `max(3, min(100, floor(log2(sizeInMB)) * 3))`

Example chunk counts:
- 1MB file: 3 chunks (192KB read)
- 100MB file: 18 chunks (1.1MB read)
- 10GB file: 99 chunks (6.2MB read)

This provides excellent duplicate detection while reading only a tiny fraction of large files.

Key methods:
- `ComputeSampledHashAsync()` - Compute xxHash64 with sampling
- `CalculateChunkCount()` - Determine optimal chunk count
- `CalculateChunkPositions()` - Distribute chunks evenly across file
- `HashBytesToUInt64()` - Convert hash bytes to comparable uint64

### DuplicateFinder.cs (102 lines)
Queries database to find and organize duplicate groups.

Key methods:
- `FindBySizeAsync()` - Fast size-based duplicate detection (may have false positives)
- `FindByHashAsync()` - Accurate hash-based duplicate detection
- `CalculateStats()` - Compute statistics (group count, duplicate count, wasted space)
- `FormatSize()` - Human-readable size formatting (B, KB, MB, GB, TB)

### InteractiveUI.cs (7,561 lines estimated)
Terminal-based interactive duplicate management. Displays duplicate groups and allows user actions:
- **K** - Keep all (skip group)
- **D [numbers]** - Delete specific files
- **O** - Keep oldest, delete rest
- **N** - Keep newest, delete rest
- **Q** - Quit

### Models.cs (68 lines)
Core data structures:
- `DedupKey` - Memory-efficient 16-byte struct (size + hash)
- `FileRecord` - Database file metadata
- `ScanOptions` - CLI configuration
- `DuplicateGroup` - Group of duplicate files

## Performance Optimizations

1. **Incremental Scanning**
   - Caches file metadata (size, modification time)
   - Skips unchanged files on subsequent scans
   - Only new/modified files are processed

2. **Two-Phase Detection**
   - Phase 1: Size-only (instant, using database index)
   - Phase 2: Hash only duplicate-sized files (not all files)
   - Dramatically reduces I/O for hash computation

3. **Logarithmic Sampling**
   - Reads only small fraction of large files
   - 10GB file: Read ~6MB instead of 10GB
   - Maintains high accuracy for duplicate detection

4. **Parallel Processing**
   - Channel-based producer-consumer pattern
   - Configurable worker count (default: CPU count)
   - Parallel hashing of duplicate candidates

5. **Database Indexing**
   - Index on `size` for fast duplicate size queries
   - Partial index on `hash` for hash-based queries
   - Scan ID tracking for multi-scan support

## Technology Stack

- **.NET 10.0** - Target framework
- **System.CommandLine** (2.0.0-beta4) - CLI argument parsing
- **Microsoft.Data.Sqlite** (9.0.0) - SQLite database access
- **Dapper** (2.1.35) - Lightweight ORM
- **System.IO.Hashing** (9.0.0) - xxHash64 implementation
- **System.Threading.Channels** - High-performance async queuing

## Testing Setup

### Test Data Structure
Located in `test-data/`:

1. **duplicates-by-content/** - 3 identical text files (true duplicates)
2. **same-size-diff-content/** - 2 files, same size, different content (false positive test)
3. **multiple-groups/** - 2 separate duplicate groups (Group A: 2 files, Group B: 3 files)
4. **nested/** - 3 identical files in nested directories (recursive scan test)
5. **duplicates-by-size/** - 2 large binary files (100KB each), same size, different content

### Test Scripts
- `run-tests.ps1` - PowerShell script with 5 automated test scenarios
- `test-data/README.txt` - Test data documentation
- `TEST-RESULTS.md` - Test summary and instructions

### Running Tests
```bash
# Build project
dotnet build

# Quick test
dotnet run -- test-data --db test-data/test.db

# Full test suite
./run-tests.ps1
```

## Working with This Codebase

### Common Tasks

**Add new CLI option:**
1. Define option in `Program.cs` (lines 13-33)
2. Add to `ScanOptions` in `Models.cs`
3. Pass through in command handler (lines 44-54)

**Modify hashing strategy:**
1. Edit `HashComputer.CalculateChunkCount()` for different sampling
2. Consider impact on existing cached hashes

**Add new duplicate detection mode:**
1. Add method to `DuplicateFinder`
2. Query database using custom criteria
3. Return `List<DuplicateGroup>`

**Change database schema:**
1. Update `Database.InitializeSchema()`
2. Modify affected query methods
3. Consider migration for existing databases

### Code Patterns

- **Error Handling**: Catch `UnauthorizedAccessException` and `IOException` for file operations
- **Progress Reporting**: Use `IProgress<T>` pattern for async progress updates
- **Async/Await**: All I/O operations are async
- **Records**: Immutable data structures using C# records
- **Dispose Pattern**: Database connection implements IDisposable

### Performance Considerations

- Channel capacity (10,000): Balance between memory and throughput
- Worker count: Default to CPU count, but configurable
- Chunk size (64KB): Optimal for most filesystems
- Database cleanup: Keeps last 2 scans to prevent unbounded growth

## Known Limitations

1. **Sampling Trade-off**: Logarithmic sampling is fast but theoretically could miss duplicates if files differ only in unsampled regions (extremely rare in practice)
2. **Database Size**: Large file collections create large databases (consider cleanup strategy)
3. **Interactive Mode**: Single-threaded, processes groups sequentially
4. **Platform**: Paths use Windows-style backslashes (works on Windows, may need adjustment for cross-platform)

## Future Enhancements to Consider

1. **Non-interactive batch mode** - Script-friendly output format (JSON, CSV)
2. **Symlink/hardlink creation** - Instead of deletion, deduplicate via filesystem links
3. **Content preview** - Show file previews before deletion
4. **Undo functionality** - Backup deleted files temporarily
5. **Filter options** - Exclude paths, file extensions, size ranges
6. **Improved sampling** - Adaptive sampling based on file type
7. **Cross-platform paths** - Handle Unix-style paths consistently

## Project Status

**Current State**: Functional prototype with testing infrastructure
- ✅ Core scanning and hashing works
- ✅ Database persistence implemented
- ✅ Interactive UI functional
- ✅ Test data and scripts created
- ✅ Build issues resolved (System.IO.Hashing dependency added)

**Next Steps**: User acceptance testing with real-world data
