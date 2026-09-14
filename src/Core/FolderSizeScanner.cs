using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SimplePCMonitor.Models;

namespace SimplePCMonitor.Core
{
    /// <summary>
    /// Measures the recursive size of each immediate child of a root directory,
    /// answering "where did my disk space go" rather than "how full is the volume".
    ///
    /// Read-only: never deletes or modifies anything.
    /// </summary>
    public static class FolderSizeScanner
    {
        private const int MaxDirectoryDepth = 40;
        private const int ProgressThrottleMs = 150;

        /// <summary>Shared mutable state for a single scan, guarded for parallel subtree walks.</summary>
        private sealed class ScanContext
        {
            public readonly object Gate = new object();
            public readonly List<SkippedEntry> Skipped = new List<SkippedEntry>();
            public readonly Stopwatch ProgressClock = Stopwatch.StartNew();
            public readonly IProgress<FolderScanProgress> Progress;
            public readonly CancellationToken Token;

            public long TotalBytes;
            public int DirectoriesVisited;
            private long _lastProgressMs;

            public ScanContext(IProgress<FolderScanProgress> progress, CancellationToken token)
            {
                Progress = progress;
                Token = token;
            }

            public void AddSkipped(string path, string reason)
            {
                lock (Gate)
                {
                    Skipped.Add(new SkippedEntry { Path = path ?? "", Reason = reason });
                }
            }

            /// <summary>
            /// Reports at most one update per throttle window. Without this, a tree full of
            /// tiny directories floods the UI dispatcher with thousands of callbacks a second.
            /// </summary>
            public void ReportProgress(string currentPath)
            {
                if (Progress == null) return;

                long now = ProgressClock.ElapsedMilliseconds;
                lock (Gate)
                {
                    if (now - _lastProgressMs < ProgressThrottleMs) return;
                    _lastProgressMs = now;
                }

                try
                {
                    Progress.Report(new FolderScanProgress
                    {
                        CurrentPath = currentPath ?? "",
                        BytesScannedSoFar = Interlocked.Read(ref TotalBytes),
                        DirectoriesVisited = DirectoriesVisited
                    });
                }
                catch { }
            }
        }

        /// <summary>Simple overload for tests and non-interactive callers.</summary>
        public static FolderSizeScanResult Scan(string rootPath, int topN = 10)
        {
            return Scan(rootPath, topN, null, CancellationToken.None);
        }

        /// <summary>
        /// Total recursive size of a single directory, reusing the same reparse-point-aware
        /// walk so callers never grow a second traversal implementation.
        /// Returns 0 when the directory is missing or unreadable.
        /// </summary>
        public static long MeasureTotalBytes(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return 0;
            return Scan(path, 0).TotalScannedBytes;
        }

        public static FolderSizeScanResult Scan(string rootPath, int topN, IProgress<FolderScanProgress> progress, CancellationToken cancellationToken)
        {
            var result = new FolderSizeScanResult { RootPath = rootPath ?? "" };
            var clock = Stopwatch.StartNew();

            if (string.IsNullOrWhiteSpace(rootPath))
            {
                result.ElapsedMilliseconds = clock.ElapsedMilliseconds;
                return result;
            }

            var context = new ScanContext(progress, cancellationToken);
            var entries = new List<FolderSizeEntry>();

            try
            {
                var rootInfo = new DirectoryInfo(rootPath);
                if (!rootInfo.Exists)
                {
                    result.ElapsedMilliseconds = clock.ElapsedMilliseconds;
                    return result;
                }

                result.RootPath = rootInfo.FullName;

                var childDirectories = new List<DirectoryInfo>();
                long looseFileBytes = 0;

                // Root level is enumerated separately so loose files still count toward the
                // total even though they belong to no child folder.
                try
                {
                    foreach (var item in rootInfo.EnumerateFileSystemInfos("*", SearchOption.TopDirectoryOnly))
                    {
                        var asDirectory = item as DirectoryInfo;
                        if (asDirectory != null)
                        {
                            childDirectories.Add(asDirectory);
                            continue;
                        }

                        var asFile = item as FileInfo;
                        if (asFile != null && !FileSystemSafety.IsReparsePoint(asFile))
                        {
                            try { looseFileBytes += asFile.Length; }
                            catch { }
                        }
                    }
                }
                catch (UnauthorizedAccessException) { context.AddSkipped(rootInfo.FullName, "AccessDenied"); }
                catch (PathTooLongException) { context.AddSkipped(rootInfo.FullName, "PathTooLong"); }
                catch (IOException) { context.AddSkipped(rootInfo.FullName, "IOError"); }

                Interlocked.Add(ref context.TotalBytes, looseFileBytes);

                // Subtrees are independent, so first-level children are walked in parallel.
                // Recursion inside each subtree stays sequential: the bottleneck is metadata
                // I/O, not CPU, and deeper parallelism adds contention without throughput.
                var measured = new FolderSizeEntry[childDirectories.Count];

                var options = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };
                Parallel.For(0, childDirectories.Count, options, index =>
                {
                    if (context.Token.IsCancellationRequested) return;

                    var child = childDirectories[index];
                    bool partial = false;

                    if (FileSystemSafety.IsReparsePoint(child))
                    {
                        // Junctions, symlinks and cloud placeholders project storage that does not
                        // live on this volume. Counting them invents space the disk does not have.
                        context.AddSkipped(child.FullName, "ReparsePoint");
                        return;
                    }

                    long size = MeasureDirectory(child, context, 0, ref partial);

                    measured[index] = new FolderSizeEntry
                    {
                        Name = child.Name,
                        FullPath = child.FullName,
                        SizeBytes = size,
                        SizeDisplay = MetricFormatting.FormatBytesAuto(size),
                        IsPartial = partial
                    };
                });

                foreach (var entry in measured)
                {
                    if (entry != null) entries.Add(entry);
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException("FolderSizeScanner", ex, false);
            }

            entries.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));

            long largest = entries.Count > 0 ? entries[0].SizeBytes : 0;
            int limit = topN > 0 ? Math.Min(topN, entries.Count) : entries.Count;

            for (int i = 0; i < limit; i++)
            {
                var entry = entries[i];
                entry.PercentOfLargest = largest > 0 ? (entry.SizeBytes * 100.0) / largest : 0.0;
                result.TopEntries.Add(entry);
            }

            result.TotalScannedBytes = Interlocked.Read(ref context.TotalBytes);
            result.SkippedEntries = context.Skipped;
            result.SkippedCount = context.Skipped.Count;
            result.WasCancelled = cancellationToken.IsCancellationRequested;
            result.ElapsedMilliseconds = clock.ElapsedMilliseconds;

            return result;
        }

        /// <summary>
        /// Walks one subtree sequentially, one directory level per call.
        ///
        /// EnumerateFileSystemInfos with SearchOption.AllDirectories cannot be used here:
        /// it throws UnauthorizedAccessException from inside the iterator and aborts the
        /// entire walk, with no way to skip the offending branch and continue.
        /// </summary>
        private static long MeasureDirectory(DirectoryInfo directory, ScanContext context, int depth, ref bool partial)
        {
            if (context.Token.IsCancellationRequested) return 0;

            if (depth > MaxDirectoryDepth)
            {
                context.AddSkipped(directory.FullName, "DepthLimit");
                partial = true;
                return 0;
            }

            Interlocked.Increment(ref context.DirectoriesVisited);
            context.ReportProgress(directory.FullName);

            long total = 0;
            IEnumerable<FileSystemInfo> children;

            try
            {
                children = directory.EnumerateFileSystemInfos("*", SearchOption.TopDirectoryOnly);
            }
            catch (UnauthorizedAccessException) { context.AddSkipped(directory.FullName, "AccessDenied"); partial = true; return 0; }
            catch (PathTooLongException) { context.AddSkipped(directory.FullName, "PathTooLong"); partial = true; return 0; }
            catch (DirectoryNotFoundException) { return 0; }
            catch (IOException) { context.AddSkipped(directory.FullName, "IOError"); partial = true; return 0; }

            using (var enumerator = children.GetEnumerator())
            {
                while (true)
                {
                    FileSystemInfo item;

                    // MoveNext itself can throw mid-enumeration on a protected or vanished entry;
                    // breaking out here loses the rest of this directory but never the whole scan.
                    try
                    {
                        if (!enumerator.MoveNext()) break;
                        item = enumerator.Current;
                    }
                    catch (UnauthorizedAccessException) { context.AddSkipped(directory.FullName, "AccessDenied"); partial = true; break; }
                    catch (PathTooLongException) { context.AddSkipped(directory.FullName, "PathTooLong"); partial = true; break; }
                    catch (IOException) { context.AddSkipped(directory.FullName, "IOError"); partial = true; break; }

                    if (context.Token.IsCancellationRequested) break;

                    var subDirectory = item as DirectoryInfo;
                    if (subDirectory != null)
                    {
                        if (FileSystemSafety.IsReparsePoint(subDirectory))
                        {
                            context.AddSkipped(subDirectory.FullName, "ReparsePoint");
                            continue;
                        }

                        total += MeasureDirectory(subDirectory, context, depth + 1, ref partial);
                        continue;
                    }

                    var file = item as FileInfo;
                    if (file == null || FileSystemSafety.IsReparsePoint(file)) continue;

                    try
                    {
                        long length = file.Length;
                        total += length;
                        Interlocked.Add(ref context.TotalBytes, length);
                    }
                    catch { }
                }
            }

            return total;
        }
    }
}
