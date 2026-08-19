using System;
using System.Collections.Generic;
using System.IO;

namespace SimplePCMonitor.Core
{
    public class TempCleanResult
    {
        public long BytesFreed { get; set; }
        public int FilesDeleted { get; set; }
        public int DirectoriesRemoved { get; set; }
        public int LockedCount { get; set; }

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
        public static TempCleanResult CleanTempFiles(int minAgeHours = 24)
        {
            var result = new TempCleanResult();
            DateTime cutoff = DateTime.Now.AddHours(-minAgeHours);

            var pathsToClean = new List<string>();
            string userTemp = Path.GetTempPath();
            if (!string.IsNullOrEmpty(userTemp) && Directory.Exists(userTemp))
            {
                pathsToClean.Add(userTemp);
            }

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(localAppData))
            {
                string localTemp = Path.Combine(localAppData, "Temp");
                if (Directory.Exists(localTemp) && !pathsToClean.Contains(localTemp))
                {
                    pathsToClean.Add(localTemp);
                }
            }

            foreach (var rootPath in pathsToClean)
            {
                CleanDirectory(rootPath, cutoff, result);
            }

            return result;
        }

        private static void CleanDirectory(string dirPath, DateTime cutoff, TempCleanResult result)
        {
            try
            {
                var dirInfo = new DirectoryInfo(dirPath);
                if (!dirInfo.Exists) return;

                // Process files
                foreach (var file in dirInfo.EnumerateFiles("*", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        if (file.LastWriteTime < cutoff)
                        {
                            long len = 0;
                            try { len = file.Length; } catch { }

                            file.Attributes = FileAttributes.Normal;
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

                // Process subdirectories
                foreach (var subDir in dirInfo.EnumerateDirectories("*", SearchOption.TopDirectoryOnly))
                {
                    CleanDirectory(subDir.FullName, cutoff, result);

                    // Try removing empty subdirectories
                    try
                    {
                        if (subDir.LastWriteTime < cutoff && subDir.GetFileSystemInfos().Length == 0)
                        {
                            subDir.Delete(false);
                            result.DirectoriesRemoved++;
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
