using System;
using System.IO;

namespace SimplePCMonitor.Core
{
    /// <summary>
    /// Shared guards for storage traversal and volume classification.
    /// Consumed by SafeTempCleaner, FolderSizeScanner, BloatDetector and DiskCollector.
    /// </summary>
    public static class FileSystemSafety
    {
        /// <summary>
        /// Windows refuses to create FAT32 volumes larger than this, so a fixed
        /// volume reporting FAT32 above the cap cannot be real physical storage.
        /// </summary>
        private const long MaxRealisticFat32Bytes = 32L * 1024L * 1024L * 1024L;

        /// <summary>
        /// Detects junctions, symlinks and cloud placeholder directories.
        /// Returns true when the attributes cannot be read, so callers refuse to
        /// traverse anything they cannot positively identify as a plain directory.
        /// </summary>
        public static bool IsReparsePoint(FileSystemInfo info)
        {
            if (info == null) return true;

            try
            {
                return (info.Attributes & FileAttributes.ReparsePoint) != 0;
            }
            catch
            {
                return true;
            }
        }

        /// <summary>
        /// Identifies cloud/virtual mounts that Windows reports as fixed drives
        /// (Google Drive, and other providers mounting through a filesystem filter).
        /// Their capacity mirrors the host volume, so counting them double-reports
        /// the machine's real storage.
        /// Takes primitives rather than a DriveInfo so it stays unit-testable.
        /// </summary>
        public static bool IsLikelyVirtualVolume(string driveFormat, long totalSizeBytes, bool isFixed)
        {
            if (!isFixed) return false;
            if (string.IsNullOrWhiteSpace(driveFormat)) return false;

            return string.Equals(driveFormat.Trim(), "FAT32", StringComparison.OrdinalIgnoreCase)
                   && totalSizeBytes > MaxRealisticFat32Bytes;
        }
    }
}
