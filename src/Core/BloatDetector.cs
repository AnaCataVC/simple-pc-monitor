using System;
using System.Collections.Generic;
using System.IO;
using SystemCoreMonitor.Models;

namespace SystemCoreMonitor.Core
{
    /// <summary>
    /// Finds large storage consumers that a per-volume usage bar structurally cannot show:
    /// dynamically grown virtual disks, regenerable build caches, the recycle bin and
    /// system-owned files that look like junk but must never be deleted.
    ///
    /// Detection is read-only. Deletion is offered only for whitelisted regenerable caches.
    /// </summary>
    public static class BloatDetector
    {
        private const long ReportFloorBytes = 100L * 1024L * 1024L;
        private const double CritThresholdGB = 20.0;
        private const double WarnThresholdGB = 5.0;

        public const string ActionSafeDelete    = "SafeDelete";
        public const string ActionLaunchTool    = "LaunchTool";
        public const string ActionInformational = "Informational";

        public static List<BloatFinding> Scan()
        {
            var findings = new List<BloatFinding>();

            try { DetectVirtualDisks(findings); } catch (Exception ex) { CrashLogger.LogException("BloatDetector.VirtualDisks", ex, false); }
            try { DetectBuildCaches(findings); } catch (Exception ex) { CrashLogger.LogException("BloatDetector.BuildCaches", ex, false); }
            try { DetectRecycleBin(findings); } catch (Exception ex) { CrashLogger.LogException("BloatDetector.RecycleBin", ex, false); }
            try { DetectSystemFiles(findings); } catch (Exception ex) { CrashLogger.LogException("BloatDetector.SystemFiles", ex, false); }
            try { DetectComponentStore(findings); } catch (Exception ex) { CrashLogger.LogException("BloatDetector.ComponentStore", ex, false); }

            foreach (var finding in findings)
            {
                finding.ActionLabel = ResolveActionLabel(finding.ActionKind);
            }

            findings.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
            return findings;
        }

        private static string ResolveActionLabel(string actionKind)
        {
            if (string.Equals(actionKind, ActionSafeDelete, StringComparison.OrdinalIgnoreCase))
                return LocalizationManager.Get("BloatActionClean");

            if (string.Equals(actionKind, ActionLaunchTool, StringComparison.OrdinalIgnoreCase))
                return LocalizationManager.Get("BloatActionTool");

            return "";
        }

        #region Whitelist

        /// <summary>
        /// The only directories this tool will ever delete. Every entry is a regenerable
        /// package or build cache that its own toolchain rebuilds on the next run.
        /// </summary>
        public static List<string> GetDeletableCachePaths()
        {
            var paths = new List<string>();
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            AddIfExists(paths, profile, ".gradle", "caches");
            AddIfExists(paths, profile, ".nuget", "packages");
            AddIfExists(paths, profile, ".android", "cache");
            AddIfExists(paths, profile, ".m2", "repository");
            AddIfExists(paths, localAppData, "npm-cache");
            AddIfExists(paths, localAppData, "pip", "cache");

            return paths;
        }

        /// <summary>
        /// Exact normalized match against the whitelist. Deliberately not a prefix check:
        /// deletion always targets a whole cache root, never a path derived from user input.
        /// </summary>
        public static bool IsWhitelistedForDeletion(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            string candidate;
            try
            {
                candidate = Path.GetFullPath(path).TrimEnd('\\', '/');
            }
            catch
            {
                return false;
            }

            foreach (var allowed in GetDeletableCachePaths())
            {
                if (string.Equals(candidate, allowed.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Clears a cache directory after confirming it is whitelisted. SafeTempCleaner
        /// applies the age guard and refuses to follow reparse points.
        /// </summary>
        public static TempCleanResult DeleteWhitelistedCache(string path, int minAgeHours = 24)
        {
            if (!IsWhitelistedForDeletion(path))
            {
                var rejected = new TempCleanResult();
                rejected.SkippedReasons.Add("Path is not whitelisted for deletion");
                return rejected;
            }

            return SafeTempCleaner.CleanWhitelistedCache(path, minAgeHours);
        }

        private static void AddIfExists(List<string> target, params string[] segments)
        {
            if (segments == null || segments.Length == 0) return;
            if (string.IsNullOrEmpty(segments[0])) return;

            try
            {
                string combined = segments[0];
                for (int i = 1; i < segments.Length; i++)
                {
                    combined = Path.Combine(combined, segments[i]);
                }

                if (Directory.Exists(combined))
                {
                    target.Add(Path.GetFullPath(combined));
                }
            }
            catch { }
        }

        #endregion

        #region Detectors

        /// <summary>
        /// Dynamically expanding VHDX files (Docker Desktop's WSL2 backend, WSL distros)
        /// grow as data is written but never shrink when it is deleted, so the file size
        /// on disk says nothing about how much is actually stored inside.
        /// </summary>
        private static void DetectVirtualDisks(List<BloatFinding> findings)
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(localAppData)) return;

            var roots = new List<string>
            {
                Path.Combine(localAppData, "Docker"),
                Path.Combine(localAppData, "Packages")
            };

            foreach (var root in roots)
            {
                if (!Directory.Exists(root)) continue;

                foreach (var file in EnumerateFilesSafely(root, "*.vhdx"))
                {
                    long size = 0;
                    try { size = file.Length; }
                    catch { continue; }

                    if (size < ReportFloorBytes) continue;

                    findings.Add(new BloatFinding
                    {
                        Title = LocalizationManager.Get("BloatVirtualDiskTitle") + " — " + file.Name,
                        Path = file.FullName,
                        SizeBytes = size,
                        SizeDisplay = MetricFormatting.FormatBytesAuto(size),
                        Severity = ClassifyBySize(size),
                        Category = "VirtualDisk",
                        ActionKind = ActionLaunchTool,
                        ActionHint = LocalizationManager.Get("BloatVirtualDiskHint")
                    });
                }
            }
        }

        private static void DetectBuildCaches(List<BloatFinding> findings)
        {
            foreach (var path in GetDeletableCachePaths())
            {
                long size = FolderSizeScanner.MeasureTotalBytes(path);
                if (size < ReportFloorBytes) continue;

                findings.Add(new BloatFinding
                {
                    Title = LocalizationManager.Get("BloatBuildCacheTitle") + " — " + new DirectoryInfo(path).Name,
                    Path = path,
                    SizeBytes = size,
                    SizeDisplay = MetricFormatting.FormatBytesAuto(size),
                    Severity = ClassifyBySize(size),
                    Category = "BuildCache",
                    ActionKind = ActionSafeDelete,
                    ActionHint = LocalizationManager.Get("BloatBuildCacheHint")
                });
            }
        }

        private static void DetectRecycleBin(List<BloatFinding> findings)
        {
            var info = new NativeMethods.SHQUERYRBINFO();
            info.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.SHQUERYRBINFO));

            int hr = NativeMethods.SHQueryRecycleBin(null, ref info);
            if (hr != 0 || info.i64Size < ReportFloorBytes) return;

            findings.Add(new BloatFinding
            {
                Title = LocalizationManager.Get("BloatRecycleBinTitle"),
                Path = "",
                SizeBytes = info.i64Size,
                SizeDisplay = MetricFormatting.FormatBytesAuto(info.i64Size),
                Severity = ClassifyBySize(info.i64Size),
                Category = "RecycleBin",
                ActionKind = ActionSafeDelete,
                ActionHint = LocalizationManager.Get("BloatRecycleBinHint")
            });
        }

        /// <summary>
        /// Paging and hibernation files are reported so users stop hunting for them:
        /// they are large, owned by Windows, and resized through system settings only.
        /// </summary>
        private static void DetectSystemFiles(List<BloatFinding> findings)
        {
            string systemRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            if (string.IsNullOrEmpty(systemRoot)) return;

            AddSystemFileFinding(findings, Path.Combine(systemRoot, "pagefile.sys"), "BloatPageFileTitle", "BloatPageFileHint");
            AddSystemFileFinding(findings, Path.Combine(systemRoot, "hiberfil.sys"), "BloatHiberFileTitle", "BloatHiberFileHint");
        }

        private static void AddSystemFileFinding(List<BloatFinding> findings, string path, string titleKey, string hintKey)
        {
            try
            {
                var file = new FileInfo(path);
                if (!file.Exists || file.Length < ReportFloorBytes) return;

                findings.Add(new BloatFinding
                {
                    Title = LocalizationManager.Get(titleKey),
                    Path = path,
                    SizeBytes = file.Length,
                    SizeDisplay = MetricFormatting.FormatBytesAuto(file.Length),
                    Severity = "Info",
                    Category = "SystemFile",
                    ActionKind = ActionInformational,
                    ActionHint = LocalizationManager.Get(hintKey)
                });
            }
            catch { }
        }

        /// <summary>
        /// WinSxS is never safe to delete by hand; only DISM knows which components are
        /// still referenced. Reported so its size stops looking like unexplained usage.
        /// </summary>
        private static void DetectComponentStore(List<BloatFinding> findings)
        {
            string windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (string.IsNullOrEmpty(windir)) return;

            string winSxS = Path.Combine(windir, "WinSxS");
            if (!Directory.Exists(winSxS)) return;

            long size = FolderSizeScanner.MeasureTotalBytes(winSxS);
            if (size < ReportFloorBytes) return;

            findings.Add(new BloatFinding
            {
                Title = LocalizationManager.Get("BloatComponentStoreTitle"),
                Path = winSxS,
                SizeBytes = size,
                SizeDisplay = MetricFormatting.FormatBytesAuto(size),
                Severity = "Info",
                Category = "ComponentStore",
                ActionKind = ActionLaunchTool,
                ActionHint = LocalizationManager.Get("BloatComponentStoreHint")
            });
        }

        #endregion

        #region Helpers

        public static string ClassifyBySize(long sizeBytes)
        {
            double gb = sizeBytes / (1024.0 * 1024.0 * 1024.0);
            if (gb >= CritThresholdGB) return "Crit";
            if (gb >= WarnThresholdGB) return "Warn";
            return "Info";
        }

        /// <summary>
        /// Recursive file search that tolerates protected branches. The framework's
        /// AllDirectories option aborts the whole enumeration on the first denied folder.
        /// </summary>
        private static IEnumerable<FileInfo> EnumerateFilesSafely(string root, string pattern)
        {
            var matches = new List<FileInfo>();
            var pending = new Stack<DirectoryInfo>();

            try { pending.Push(new DirectoryInfo(root)); }
            catch { return matches; }

            while (pending.Count > 0)
            {
                var current = pending.Pop();

                try
                {
                    foreach (var file in current.EnumerateFiles(pattern, SearchOption.TopDirectoryOnly))
                    {
                        matches.Add(file);
                    }
                }
                catch { }

                try
                {
                    foreach (var child in current.EnumerateDirectories("*", SearchOption.TopDirectoryOnly))
                    {
                        if (!FileSystemSafety.IsReparsePoint(child)) pending.Push(child);
                    }
                }
                catch { }
            }

            return matches;
        }

        #endregion
    }
}
