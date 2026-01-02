using System.Runtime.InteropServices;

namespace Ddup;

/// <summary>
/// Platform-specific utilities for file metadata
/// </summary>
public static class PlatformUtils
{
    private static bool _statxWarningShown;
    private static bool _statxSupported = true;

    /// <summary>
    /// Get file creation time (birth time) with platform-specific handling.
    /// On Windows: uses FileInfo.CreationTimeUtc
    /// On Linux: uses statx() syscall to get real birth time, with fallback
    /// </summary>
    public static DateTime GetCreationTimeUtc(string path)
    {
        if (OperatingSystem.IsLinux())
        {
            return GetLinuxCreationTime(path);
        }

        return File.GetCreationTimeUtc(path);
    }

    /// <summary>
    /// Get file creation time as Unix timestamp
    /// </summary>
    public static long GetCreationTimeUnix(string path)
    {
        var creationTime = GetCreationTimeUtc(path);
        return new DateTimeOffset(creationTime, TimeSpan.Zero).ToUnixTimeSeconds();
    }

    private static DateTime GetLinuxCreationTime(string path)
    {
        if (_statxSupported)
        {
            try
            {
                var result = StatxGetBirthTime(path);
                if (result.HasValue)
                {
                    return result.Value;
                }
            }
            catch
            {
                // statx not available, fall through to fallback
            }

            // statx failed or birth time not available
            if (!_statxWarningShown)
            {
                _statxWarningShown = true;
                _statxSupported = false;
                Console.WriteLine("Warning: statx() not supported on this system, falling back to approximate creation time.");
            }
        }

        // Fallback: use standard .NET API (returns min(mtime, ctime) on Linux)
        return File.GetCreationTimeUtc(path);
    }

    #region Linux statx() P/Invoke

    // statx() syscall constants
    private const int AT_FDCWD = -100;
    private const int AT_STATX_SYNC_AS_STAT = 0x0000;
    private const uint STATX_BTIME = 0x00000800U;

    [StructLayout(LayoutKind.Sequential)]
    private struct StatxTimestamp
    {
        public long tv_sec;
        public uint tv_nsec;
        private readonly int __reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Statx
    {
        public uint stx_mask;
        public uint stx_blksize;
        public ulong stx_attributes;
        public uint stx_nlink;
        public uint stx_uid;
        public uint stx_gid;
        public ushort stx_mode;
        private readonly ushort __spare0;
        public ulong stx_ino;
        public ulong stx_size;
        public ulong stx_blocks;
        public ulong stx_attributes_mask;
        public StatxTimestamp stx_atime;
        public StatxTimestamp stx_btime;  // Birth time
        public StatxTimestamp stx_ctime;
        public StatxTimestamp stx_mtime;
        public uint stx_rdev_major;
        public uint stx_rdev_minor;
        public uint stx_dev_major;
        public uint stx_dev_minor;
        public ulong stx_mnt_id;
        private readonly ulong __spare2;
        // Padding to match the kernel struct size (use explicit size)
        private readonly ulong __spare3_0, __spare3_1, __spare3_2, __spare3_3;
        private readonly ulong __spare3_4, __spare3_5, __spare3_6, __spare3_7;
        private readonly ulong __spare3_8, __spare3_9, __spare3_10, __spare3_11;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int statx(
        int dirfd,
        [MarshalAs(UnmanagedType.LPStr)] string pathname,
        int flags,
        uint mask,
        out Statx statxbuf);

    private static DateTime? StatxGetBirthTime(string path)
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        var result = statx(AT_FDCWD, path, AT_STATX_SYNC_AS_STAT, STATX_BTIME, out var buf);

        if (result != 0)
        {
            return null;
        }

        // Check if birth time is actually available
        if ((buf.stx_mask & STATX_BTIME) == 0)
        {
            return null;
        }

        // Check for invalid/zero birth time
        if (buf.stx_btime.tv_sec <= 0)
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeSeconds(buf.stx_btime.tv_sec).UtcDateTime;
    }

    #endregion
}
