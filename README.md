# ddup - High-Performance File Deduplication Tool

A fast, flexible file deduplication tool written in C# that finds duplicate files using configurable hash components.

## Features

- **Configurable Hash Components**: Choose what defines a "duplicate" - file size, creation time, modification time, content, or file name
- **Fast Incremental Scanning**: SQLite-based caching skips unchanged files on subsequent scans
- **Smart Content Hashing**: xxHash64 with logarithmic sampling reads only a fraction of large files while maintaining accuracy
- **Interactive UI**: Review and manage duplicate groups with simple keyboard commands
- **Cross-Platform**: Works on Windows and Linux (with real birth time support via statx())
- **Parallel Processing**: Multi-threaded scanning and hashing for maximum performance

## Installation

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### Build from Source

```bash
git clone <repository-url>
cd ddup
dotnet build -c Release
```

## Usage

### Basic Syntax

```bash
ddup <path> [options]
```

### Hash Components

Control what defines duplicate files using these options:

| Option | Description | Default |
|--------|-------------|---------|
| `--content <+\|->` | Include/exclude file content | `-` (off) |
| `--size <+\|->` | Include/exclude file size | `+` (on) |
| `--mtime <+\|->` | Include/exclude modification time | `-` (off) |
| `--ctime <+\|->` | Include/exclude creation time | `-` (off) |
| `--name <+\|->` | Include/exclude file name (case insensitive) | `-` (off) |

### Other Options

| Option | Description | Default |
|--------|-------------|---------|
| `--workers <n>` | Number of parallel workers | CPU count |
| `--db <path>` | Database location | `.dups.db` |
| `--full-scan` | Re-scan everything (ignore cache) | `false` |

### Examples

**Find files with identical size** (default, fastest):
```bash
ddup /path/to/folder
```

**Find files with identical content** (accurate, slower):
```bash
ddup /path/to/folder --content +
```

**Find files with identical size AND creation time**:
```bash
ddup /path/to/folder --ctime +
```

**Find files with identical name** (case-insensitive):
```bash
ddup /path/to/folder --name +
```

**Find identical content, ignoring size** (useful for detecting renamed files):
```bash
ddup /path/to/folder --content + --size -
```

**Combination: size + creation time + modification time**:
```bash
ddup /path/to/folder --ctime + --mtime +
```

## Interactive Mode

After scanning, ddup presents duplicate groups interactively:

```
=== Group 1 of 2,169 ===
Size: 6.04 KB
Hash: 6915AA25A29F1B00
Files (2):

  [1] L:\Photos\vacation\IMG_001.jpg
      Created: 2024-06-15 14:30:00  Modified: 2024-06-15 14:30:00
  [2] L:\Photos\backup\IMG_001.jpg
      Created: 2024-06-20 10:15:00  Modified: 2024-06-15 14:30:00

Actions:
  [K]eep all (skip this group)
  [D]elete by number (e.g., 'd 2,3' to delete files 2 and 3)
  [O]ldest - keep oldest, delete others
  [N]ewest - keep newest, delete others
  [Q]uit

Choice:
```

### Actions

- **K** - Keep all files in this group, move to next
- **D [numbers]** - Delete specific files by their number (e.g., `d 2` or `d 1,3`)
- **O** - Keep the oldest file (by modification time), delete the rest
- **N** - Keep the newest file (by modification time), delete the rest
- **Q** - Quit the interactive session

## How It Works

### Phase 1: File Scanning

- Recursively enumerates all files in the directory tree
- Uses producer-consumer pattern with bounded channels (10,000 capacity)
- Captures file metadata: size, modification time, creation time
- For metadata-only hashing (size, times, name), computes hash immediately
- Checks database cache to skip unchanged files (incremental mode)
- Stores file records in SQLite database

### Phase 2: Content Hashing (Optional)

If `--content +` is specified:
- Identifies files with duplicate sizes/metadata
- Uses xxHash64 with logarithmic sampling for fast content hashing
- For large files (e.g., 10GB), reads only ~6MB via strategic chunk sampling
- Updates database with composite hash values

### Phase 3: Duplicate Detection

- Groups files by their composite hash
- Presents groups in descending size order (largest duplicates first)
- Interactive UI allows selective deletion

## Performance

### Hash Component Performance

| Components | Speed | Use Case |
|------------|-------|----------|
| Size only | **Instant** | Quick scan, may have false positives |
| Size + ctime | **Instant** | Filter by creation time metadata |
| Size + mtime | **Instant** | Filter by modification time |
| Name only | **Instant** | Find duplicate filenames |
| Content | **Slow** | Accurate duplicate detection |

### Content Hashing Performance

- **Small files** (<64KB): Full hash
- **1MB file**: Reads 192KB (3 chunks)
- **100MB file**: Reads 1.1MB (18 chunks)
- **10GB file**: Reads 6.2MB (99 chunks)

## Technical Details

- **Language**: C# 10.0, .NET 10.0
- **Database**: SQLite with WAL mode
- **Hashing**: xxHash64 (non-cryptographic, optimized for speed)
- **ORM**: Dapper
- **Linux Support**: P/Invoke to `statx()` for real birth time (kernel 4.11+)

## Configuration Persistence

Hash component configuration is stored in the database. If you change hash options between scans, ddup automatically forces a full rescan to maintain consistency.

## Limitations

- **Sampling Trade-off**: Content hashing uses sampling, which is extremely fast but theoretically could miss duplicates if files differ only in unsampled regions (extremely rare in practice)
- **Linux Creation Time**: On older kernels (<4.11) or unsupported filesystems, falls back to `min(mtime, ctime)` approximation
- **No Symbolic Link Support**: Currently doesn't follow or deduplicate via symlinks/hardlinks

## License

## License

[MIT License](LICENSE)

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines on how to contribute to this project.
