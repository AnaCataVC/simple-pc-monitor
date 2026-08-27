using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace SimplePCMonitor.Core
{
    public class TempCleanResult
    {
        public long BytesFreed { get; set; }
        public int FilesDeleted { get; set; }
        public int DirectoriesRemoved { get; set; }
        public int LockedCount { get; set; }
        public List<string> SkippedReasons { get; set; }

        public TempCleanResult()
        {
            SkippedReasons = new List<string>();
        }

        public string HumanSize
        {
            get
            {
                if (BytesFreed >= 1024L * 1024L * 1024L)
                    return string.Format("{0:N2} GB", (double)BytesFreed / (1024.0 * 1024.0 * 1024.0));
                if (BytesFreed >= 1024L * 1024L)
                    return string.Format("{0:N1} MB", (double)BytesFreed / (1024.0 * 1024.0));
                if (BytesFreed >= 1024L)
                    return string.Format("{0:N0} KB", (double)BytesFreed / 1024.0);
                return string.Format("{0} Bytes", BytesFreed);
            }
        }
    }

    public static class SafeTempCleaner
    {
        private const int MaxDirectoryDepth = 15;

        private static readonly string[] PersistentExclusions = new string[]
        {
            "Microsoft.ScreenSketch", "ScreenSketch", "SnippingTool",
            "OneDrive", "OneDriveSync", "GoogleDrive", "McAfee", "LiveSafe",
            "Antigravity", "Claude", "Copilot", "Packages", "Teams",
            ".config", ".local", ".claude", ".antigravity"
        };

        private static readonly HashSet<string> ProtectedRootPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static SafeTempCleaner()
        {
            // Register system directories that must NEVER be cleaned as a root
            AddProtectedPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            AddProtectedPath(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            AddProtectedPath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            AddProtectedPath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
            AddProtectedPath(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            AddProtectedPath(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
        }

        private static void AddProtectedPath(string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                ProtectedRootPaths.Add(Path.GetFullPath(path).TrimEnd('\\', '/'));
            }
        }

        public static bool IsExcluded(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return true;
            try
            {
                string fullPath = Path.GetFullPath(path);
                foreach (var pattern in PersistentExclusions)
                {
                    if (fullPath.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            catch
            {
                return true;
            }

            return false;
        }

        private static bool IsSafeTempRoot(string rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath)) return false;
            try
            {
                string full = Path.GetFullPath(rootPath).TrimEnd('\\', '/');
                string root = Path.GetPathRoot(full);
                if (root != null) root = root.TrimEnd('\\', '/');

                // 1. Guard against drive roots (e.g. C:)
                if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
                    return false;

                // 2. Guard against system/user profile directories
                if (ProtectedRootPaths.Contains(full))
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsReparsePoint(FileSystemInfo info)
        {
            try
            {
                return (info.Attributes & FileAttributes.ReparsePoint) != 0;
            }
            catch
            {
                return true;
            }
        }

        public static TempCleanResult CleanDeepStorage(bool cleanWindowsUpdate = true, int minAgeHours = 24)
        {
            var result = new TempCleanResult();
            DateTime cutoff = DateTime.Now.AddHours(-Math.Max(1, minAgeHours));

            var pathsToClean = new List<string>();

            // 1. User Temp Directory (%TEMP% only)
            string userTemp = Path.GetTempPath();
            if (!string.IsNullOrEmpty(userTemp) && Directory.Exists(userTemp) && IsSafeTempRoot(userTemp))
            {
                pathsToClean.Add(Path.GetFullPath(userTemp));
            }

            // 2. Windows System Temp (C:\Windows\Temp)
            string windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrEmpty(windir))
            {
                string winTemp = Path.Combine(windir, "Temp");
                if (Directory.Exists(winTemp) && IsSafeTempRoot(winTemp) && !pathsToClean.Contains(winTemp))
                    pathsToClean.Add(Path.GetFullPath(winTemp));

                // 3. Windows Update Download Cache
                if (cleanWindowsUpdate)
                {
                    string softDist = Path.Combine(windir, "SoftwareDistribution", "Download");
                    if (Directory.Exists(softDist) && IsSafeTempRoot(softDist) && !pathsToClean.Contains(softDist))
                        pathsToClean.Add(Path.GetFullPath(softDist));
                }

                // 4. Windows WinSxS Temp
                string winsxsTemp = Path.Combine(windir, "WinSxS", "Temp");
                if (Directory.Exists(winsxsTemp) && IsSafeTempRoot(winsxsTemp) && !pathsToClean.Contains(winsxsTemp))
                    pathsToClean.Add(Path.GetFullPath(winsxsTemp));
            }

            // 5. Delivery Optimization Cache
            string progData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            if (!string.IsNullOrEmpty(progData) && cleanWindowsUpdate)
            {
                string deliveryOpt = Path.Combine(progData, "Microsoft", "Windows", "DeliveryOptimization");
                if (Directory.Exists(deliveryOpt) && IsSafeTempRoot(deliveryOpt))
                    pathsToClean.Add(Path.GetFullPath(deliveryOpt));
            }

            foreach (var rootPath in pathsToClean)
            {
                CleanDirectory(rootPath, cutoff, result, 0);
            }

            return result;
        }

        private static void CleanDirectory(string dirPath, DateTime cutoff, TempCleanResult result, int currentDepth)
        {
            if (currentDepth > MaxDirectoryDepth) return;

            try
            {
                if (IsExcluded(dirPath)) return;

                var dirInfo = new DirectoryInfo(dirPath);
                if (!dirInfo.Exists) return;

                // If folder is a Junction Point or Symlink, DO NOT traverse it (prevents sandbox escape)
                if (IsReparsePoint(dirInfo) && currentDepth > 0)
                {
                    try
                    {
                        if (dirInfo.LastWriteTime < cutoff && dirInfo.CreationTime < cutoff)
                        {
                            dirInfo.Delete(false);
                            result.DirectoriesRemoved++;
                        }
                    }
                    catch { result.LockedCount++; }
                    return;
                }

                // 1. Process files with resilient enumeration
                IEnumerable<FileInfo> files = null;
                try
                {
                    files = dirInfo.EnumerateFiles("*", SearchOption.TopDirectoryOnly);
                }
                catch { result.LockedCount++; }

                if (files != null)
                {
                    foreach (var file in files)
                    {
                        try
                        {
                            if (IsExcluded(file.FullName)) continue;

                            // DUAL TIMESTAMP GUARD: Verify BOTH LastWriteTime and CreationTime
                            // to never delete newly extracted files from ZIP/MSI installers
                            if (file.LastWriteTime < cutoff && file.CreationTime < cutoff)
                            {
                                long len = 0;
                                try { len = file.Length; } catch { }

                                if (!IsReparsePoint(file))
                                {
                                    file.Attributes = FileAttributes.Normal;
                                }

                                file.Delete();
                                result.BytesFreed += len;
                                result.FilesDeleted++;
                            }
                        }
                        catch
                        {
                            result.LockedCount++;
                        }
                    }
                }

                // 2. Process subdirectories
                IEnumerable<DirectoryInfo> subDirectories = null;
                try
                {
                    subDirectories = dirInfo.EnumerateDirectories("*", SearchOption.TopDirectoryOnly);
                }
                catch { result.LockedCount++; }

                if (subDirectories != null)
                {
                    foreach (var subDir in subDirectories)
                    {
                        try
                        {
                            if (IsExcluded(subDir.FullName)) continue;

                            // If subfolder is a Junction / Symlink, DO NOT enter recursively
                            if (IsReparsePoint(subDir))
                            {
                                if (subDir.LastWriteTime < cutoff && subDir.CreationTime < cutoff)
                                {
                                    subDir.Delete(false);
                                    result.DirectoriesRemoved++;
                                }
                                continue;
                            }

                            CleanDirectory(subDir.FullName, cutoff, result, currentDepth + 1);

                            if (subDir.LastWriteTime < cutoff && subDir.CreationTime < cutoff)
                            {
                                try
                                {
                                    if (subDir.GetFileSystemInfos().Length == 0)
                                    {
                                        subDir.Delete(false);
                                        result.DirectoriesRemoved++;
                                    }
                                }
                                catch { }
                            }
                        }
                        catch
                        {
                            result.LockedCount++;
                        }
                    }
                }
            }
            catch
            {
                result.LockedCount++;
            }
        }
    }
}
