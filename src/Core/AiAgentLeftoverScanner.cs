using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace SystemCoreMonitor.Core
{
    public enum AiLeftoverKind
    {
        Worktree,
        TempScratch,
        StaleLock,
        McpLog
    }

    public class AiLeftoverItem
    {
        public AiLeftoverKind Kind { get; set; }
        public string Path { get; set; } = string.Empty;
        public string DisplayPath { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string SizeDisplay => MetricFormatting.FormatBytesAuto(SizeBytes);
        public string Reason { get; set; } = string.Empty;
        public string AgeDisplay { get; set; } = string.Empty;
        public bool CanDelete { get; set; }

        /// <summary>Owning repository for worktree items; cleanup goes through git on this repo.</summary>
        public string RepoPath { get; set; } = string.Empty;

        /// <summary>True for a worktree whose directory is gone and only the git registration remains.</summary>
        public bool IsDanglingRegistration { get; set; }

        public string KindDisplay => Kind switch
        {
            AiLeftoverKind.Worktree => "Worktree",
            AiLeftoverKind.TempScratch => "Temp",
            AiLeftoverKind.StaleLock => "Lock",
            _ => "MCP log"
        };
    }

    public class AiLeftoverReport
    {
        public List<AiLeftoverItem> Items { get; set; } = new List<AiLeftoverItem>();
        public bool Cancelled { get; set; }
        public long DeletableBytes => Items.Where(i => i.CanDelete).Sum(i => i.SizeBytes);
        public int DeletableCount => Items.Count(i => i.CanDelete);
    }

    public class AiLeftoverCleanResult
    {
        public int Removed { get; set; }
        public long BytesFreed { get; set; }
        public List<string> Failures { get; set; } = new List<string>();
    }

    /// <summary>
    /// Finds what AI coding agents leave on disk after a session ends: abandoned git worktrees,
    /// per-project scratch under %TEMP%, session records of dead processes, and old MCP logs.
    /// Scanning is read-only and runs only on explicit user action. Deletion re-validates every
    /// item against an exact-match gate derived from known roots (AGENTS rule 14); worktrees
    /// are only ever removed through git, never by raw directory deletion.
    /// </summary>
    public class AiAgentLeftoverScanner
    {
        public static readonly TimeSpan ScratchMinimumAge = TimeSpan.FromHours(24);

        // Two samples of the same process start can differ by rounding; beyond this they are different processes.
        private static readonly long StartTimeToleranceTicks = TimeSpan.FromSeconds(3).Ticks;
        private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan ProgressThrottle = TimeSpan.FromMilliseconds(150);

        private static readonly Regex ClaudeCwdFileRegex = new Regex(@"^claude-[0-9a-f]+-cwd$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex SessionJsonRegex = new Regex(@"^(\d+)\.json$", RegexOptions.CultureInvariant);
        private static readonly Regex SessionKeyRegex = new Regex(@"^(\d+)\.[0-9a-f]+\.key$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex CwdFieldRegex = new Regex("\"cwd\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.CultureInvariant);

        private readonly string _tempRoot;
        private readonly string _claudeHome;
        private readonly string _localAppData;

        public AiAgentLeftoverScanner() : this(null, null, null) { }

        public AiAgentLeftoverScanner(string? tempRoot, string? claudeHome, string? localAppData)
        {
            _tempRoot = Normalize(tempRoot ?? System.IO.Path.GetTempPath());
            _claudeHome = Normalize(claudeHome ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude"));
            _localAppData = Normalize(localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        }

        private string ClaudeTempRoot => System.IO.Path.Combine(_tempRoot, "claude");
        private string SessionsRoot => System.IO.Path.Combine(_claudeHome, "sessions");
        private string ProjectsRoot => System.IO.Path.Combine(_claudeHome, "projects");
        private string McpCacheRoot => System.IO.Path.Combine(_localAppData, "claude-cli-nodejs", "Cache");

        // ---------------------------------------------------------------- pure classifiers

        /// <summary>
        /// Scratch is stale only when both its creation and its newest write are older than the
        /// minimum age (dual timestamp, as in SafeTempCleaner) and no live session owns it.
        /// </summary>
        public static bool IsScratchStale(DateTime creationUtc, DateTime newestWriteUtc, DateTime nowUtc, bool ownedByLiveSession, TimeSpan minimumAge)
        {
            if (ownedByLiveSession) return false;
            return nowUtc - creationUtc >= minimumAge && nowUtc - newestWriteUtc >= minimumAge;
        }

        public static bool IsClaudeCwdFileName(string name) => !string.IsNullOrEmpty(name) && ClaudeCwdFileRegex.IsMatch(name);

        /// <summary>Claude Code names per-project folders after the cwd with every non-alphanumeric character replaced by '-'.</summary>
        public static string ToClaudeProjectDirName(string cwd) => Regex.Replace(cwd ?? string.Empty, "[^A-Za-z0-9]", "-");

        /// <summary>
        /// A session record is stale when its PID is not running (<paramref name="actualStartUtc"/>
        /// null) or is running with a different start time: the number was recycled.
        /// </summary>
        public static bool IsRecordStale(long recordedStartFileTimeUtc, DateTime? actualStartUtc)
        {
            if (actualStartUtc == null) return true;
            long actual = actualStartUtc.Value.ToUniversalTime().ToFileTimeUtc();
            return Math.Abs(actual - recordedStartFileTimeUtc) > StartTimeToleranceTicks;
        }

        /// <summary>
        /// Parses a Claude Code session record: "&lt;pid&gt;.json" carries "procStart", and
        /// "&lt;pid&gt;.&lt;hash&gt;.key" carries "procStartFt", both as FILETIME strings.
        /// </summary>
        public static bool TryParseSessionRecord(string fileName, string json, out int pid, out long startFileTime)
        {
            pid = 0;
            startFileTime = 0;
            string field;
            var m = SessionJsonRegex.Match(fileName ?? string.Empty);
            if (m.Success) field = "procStart";
            else
            {
                m = SessionKeyRegex.Match(fileName ?? string.Empty);
                if (!m.Success) return false;
                field = "procStartFt";
            }

            if (!int.TryParse(m.Groups[1].Value, out pid) || pid <= 4) return false;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty(field, out var prop)) return false;
                string raw = prop.ValueKind == JsonValueKind.String ? prop.GetString() ?? string.Empty : prop.GetRawText();
                return long.TryParse(raw, out startFileTime) && startFileTime > 0;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsMcpLogStale(DateTime lastWriteUtc, DateTime nowUtc, int retentionDays) =>
            nowUtc - lastWriteUtc >= TimeSpan.FromDays(Math.Max(1, retentionDays));

        /// <summary>
        /// A worktree registration (".git/worktrees/&lt;name&gt;/gitdir") is dangling when the
        /// ".git" file it points to no longer exists.
        /// </summary>
        public static bool IsDanglingWorktree(string gitdirFileContent, Func<string, bool> fileExists)
        {
            string target = (gitdirFileContent ?? string.Empty).Trim();
            if (target.Length == 0) return false; // Unreadable registration: do not guess
            return !fileExists(target);
        }

        // ---------------------------------------------------------------- scan

        public AiLeftoverReport Scan(int retentionDays, IProgress<string>? progress, CancellationToken token)
        {
            var report = new AiLeftoverReport();
            var clock = Stopwatch.StartNew();
            TimeSpan lastReport = TimeSpan.Zero;
            void Report(string stage)
            {
                if (progress == null || clock.Elapsed - lastReport < ProgressThrottle) return;
                lastReport = clock.Elapsed;
                progress.Report(stage);
            }

            var liveCwds = LoadLiveSessionCwds();
            DateTime nowUtc = DateTime.UtcNow;

            Report("Locks");
            report.Items.AddRange(ScanStaleLocks());
            if (token.IsCancellationRequested) { report.Cancelled = true; return report; }

            Report("%TEMP%");
            report.Items.AddRange(ScanTempScratch(liveCwds, nowUtc, token));
            if (token.IsCancellationRequested) { report.Cancelled = true; return report; }

            Report("MCP logs");
            report.Items.AddRange(ScanMcpLogs(retentionDays, nowUtc, token));
            if (token.IsCancellationRequested) { report.Cancelled = true; return report; }

            Report("Worktrees");
            report.Items.AddRange(ScanWorktrees(liveCwds, nowUtc, token, Report));
            report.Cancelled = token.IsCancellationRequested;
            return report;
        }

        /// <summary>Working directories of Claude sessions whose (PID, StartTime) is verifiably alive.</summary>
        private List<string> LoadLiveSessionCwds()
        {
            var cwds = new List<string>();
            if (!Directory.Exists(SessionsRoot)) return cwds;
            foreach (var file in SafeEnumerateFiles(SessionsRoot, "*.json"))
            {
                try
                {
                    string json = File.ReadAllText(file);
                    if (!TryParseSessionRecord(System.IO.Path.GetFileName(file), json, out int pid, out long ft)) continue;
                    if (IsRecordStale(ft, TryGetProcessStartUtc(pid))) continue;
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("cwd", out var cwd) && cwd.ValueKind == JsonValueKind.String)
                    {
                        cwds.Add(cwd.GetString() ?? string.Empty);
                    }
                }
                catch { }
            }
            return cwds;
        }

        private IEnumerable<AiLeftoverItem> ScanStaleLocks()
        {
            if (!Directory.Exists(SessionsRoot)) yield break;
            foreach (var file in SafeEnumerateFiles(SessionsRoot, "*"))
            {
                string name = System.IO.Path.GetFileName(file);
                string json;
                try { json = File.ReadAllText(file); } catch { continue; }
                if (!TryParseSessionRecord(name, json, out int pid, out long ft)) continue;
                if (!IsRecordStale(ft, TryGetProcessStartUtc(pid))) continue;

                long size = 0;
                try { size = new FileInfo(file).Length; } catch { }
                yield return new AiLeftoverItem
                {
                    Kind = AiLeftoverKind.StaleLock,
                    Path = file,
                    DisplayPath = ToDisplayPath(file),
                    SizeBytes = size,
                    Reason = string.Format("PID {0} ya no corresponde al proceso registrado", pid),
                    CanDelete = true
                };
            }
        }

        private IEnumerable<AiLeftoverItem> ScanTempScratch(List<string> liveCwds, DateTime nowUtc, CancellationToken token)
        {
            var liveDirNames = new HashSet<string>(liveCwds.Select(ToClaudeProjectDirName), StringComparer.OrdinalIgnoreCase);

            if (Directory.Exists(ClaudeTempRoot) && !FileSystemSafety.IsReparsePoint(ClaudeTempRoot))
            {
                foreach (var dir in SafeEnumerateDirectories(ClaudeTempRoot))
                {
                    if (token.IsCancellationRequested) yield break;
                    var info = new DirectoryInfo(dir);
                    if (FileSystemSafety.IsReparsePoint(info)) continue;

                    bool owned = liveDirNames.Contains(info.Name);
                    DateTime newest = NewestWriteUtc(info, token);
                    if (!IsScratchStale(info.CreationTimeUtc, newest, nowUtc, owned, ScratchMinimumAge)) continue;

                    yield return new AiLeftoverItem
                    {
                        Kind = AiLeftoverKind.TempScratch,
                        Path = info.FullName,
                        DisplayPath = ToDisplayPath(info.FullName),
                        SizeBytes = FolderSizeScanner.MeasureTotalBytes(info.FullName),
                        Reason = "Scratch de sesiones Claude sin actividad ni sesión viva",
                        AgeDisplay = MetricFormatting.FormatAge(nowUtc - newest),
                        CanDelete = true
                    };
                }
            }

            foreach (var file in SafeEnumerateFiles(_tempRoot, "claude-*-cwd"))
            {
                var info = new FileInfo(file);
                if (!IsClaudeCwdFileName(info.Name) || FileSystemSafety.IsReparsePoint(info)) continue;
                if (!IsScratchStale(info.CreationTimeUtc, info.LastWriteTimeUtc, nowUtc, false, ScratchMinimumAge)) continue;
                yield return new AiLeftoverItem
                {
                    Kind = AiLeftoverKind.TempScratch,
                    Path = info.FullName,
                    DisplayPath = ToDisplayPath(info.FullName),
                    SizeBytes = info.Length,
                    Reason = "Marcador de cwd de una shell Claude ya cerrada",
                    AgeDisplay = MetricFormatting.FormatAge(nowUtc - info.LastWriteTimeUtc),
                    CanDelete = true
                };
            }
        }

        private IEnumerable<AiLeftoverItem> ScanMcpLogs(int retentionDays, DateTime nowUtc, CancellationToken token)
        {
            if (!Directory.Exists(McpCacheRoot)) yield break;
            foreach (var projectDir in SafeEnumerateDirectories(McpCacheRoot))
            {
                if (token.IsCancellationRequested) yield break;
                if (FileSystemSafety.IsReparsePoint(projectDir)) continue;

                var stale = EnumerateStaleMcpLogs(projectDir, retentionDays, nowUtc).ToList();
                if (stale.Count == 0) continue;
                yield return new AiLeftoverItem
                {
                    Kind = AiLeftoverKind.McpLog,
                    Path = projectDir,
                    DisplayPath = ToDisplayPath(projectDir),
                    SizeBytes = stale.Sum(f => f.Length),
                    Reason = string.Format("{0} log(s) MCP con más de {1} días", stale.Count, retentionDays),
                    CanDelete = true
                };
            }
        }

        private static IEnumerable<FileInfo> EnumerateStaleMcpLogs(string projectDir, int retentionDays, DateTime nowUtc)
        {
            foreach (var logDir in SafeEnumerateDirectories(projectDir, "mcp-logs-*"))
            {
                if (FileSystemSafety.IsReparsePoint(logDir)) continue;
                foreach (var file in SafeEnumerateFiles(logDir, "*.jsonl"))
                {
                    var info = new FileInfo(file);
                    if (!FileSystemSafety.IsReparsePoint(info) && IsMcpLogStale(info.LastWriteTimeUtc, nowUtc, retentionDays))
                    {
                        yield return info;
                    }
                }
            }
        }

        private IEnumerable<AiLeftoverItem> ScanWorktrees(List<string> liveCwds, DateTime nowUtc, CancellationToken token, Action<string> report)
        {
            foreach (var repo in DiscoverRepositories(liveCwds, token))
            {
                if (token.IsCancellationRequested) yield break;
                report(ToDisplayPath(repo));

                string adminRoot = System.IO.Path.Combine(repo, ".git", "worktrees");
                foreach (var admin in SafeEnumerateDirectories(adminRoot))
                {
                    string gitdir;
                    try { gitdir = File.ReadAllText(System.IO.Path.Combine(admin, "gitdir")); } catch { continue; }
                    if (!IsDanglingWorktree(gitdir, File.Exists)) continue;
                    yield return new AiLeftoverItem
                    {
                        Kind = AiLeftoverKind.Worktree,
                        Path = Normalize(admin),
                        DisplayPath = ToDisplayPath(admin),
                        RepoPath = repo,
                        IsDanglingRegistration = true,
                        Reason = "Registro de worktree cuya carpeta ya no existe (git worktree prune)",
                        CanDelete = true
                    };
                }

                string agentRoot = System.IO.Path.Combine(repo, ".claude", "worktrees");
                foreach (var wt in SafeEnumerateDirectories(agentRoot))
                {
                    if (token.IsCancellationRequested) yield break;
                    if (FileSystemSafety.IsReparsePoint(wt)) continue;
                    string full = Normalize(wt);
                    if (liveCwds.Any(c => IsSameOrUnder(c, full))) continue;

                    DateTime lastActivity = WorktreeLastActivityUtc(full);
                    if (nowUtc - lastActivity < ScratchMinimumAge) continue;

                    string? porcelain = RunGit(full, "status", "--porcelain");
                    bool dirty = porcelain == null || porcelain.Trim().Length > 0;
                    yield return new AiLeftoverItem
                    {
                        Kind = AiLeftoverKind.Worktree,
                        Path = full,
                        DisplayPath = ToDisplayPath(full),
                        RepoPath = repo,
                        SizeBytes = FolderSizeScanner.MeasureTotalBytes(full),
                        AgeDisplay = MetricFormatting.FormatAge(nowUtc - lastActivity),
                        Reason = dirty
                            ? "Worktree de agente abandonado con cambios sin commitear (no se borra)"
                            : "Worktree de agente abandonado y limpio (git worktree remove)",
                        CanDelete = !dirty
                    };
                }
            }
        }

        /// <summary>
        /// Repositories agents worked in, read from the cwd recorded in each Claude project's
        /// newest transcript (the folder names themselves are lossy and cannot be reversed).
        /// </summary>
        private IEnumerable<string> DiscoverRepositories(List<string> liveCwds, CancellationToken token)
        {
            var cwds = new HashSet<string>(liveCwds, StringComparer.OrdinalIgnoreCase);
            foreach (var project in SafeEnumerateDirectories(ProjectsRoot))
            {
                if (token.IsCancellationRequested) break;
                var newest = SafeEnumerateFiles(project, "*.jsonl")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .FirstOrDefault();
                string? cwd = newest != null ? ReadFirstCwd(newest.FullName) : null;
                if (!string.IsNullOrEmpty(cwd)) cwds.Add(cwd);
            }

            var repos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var cwd in cwds)
            {
                string? repo = FindRepositoryRoot(cwd);
                if (repo != null) repos.Add(repo);
            }
            return repos;
        }

        private static string? ReadFirstCwd(string transcriptPath)
        {
            try
            {
                using var reader = new StreamReader(new FileStream(transcriptPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
                for (int i = 0; i < 40; i++)
                {
                    string? line = reader.ReadLine();
                    if (line == null) break;
                    var m = CwdFieldRegex.Match(line);
                    if (m.Success) return Regex.Unescape(m.Groups[1].Value);
                }
            }
            catch { }
            return null;
        }

        /// <summary>The main repository (with a real .git directory) containing a path, or null.</summary>
        private static string? FindRepositoryRoot(string path)
        {
            try
            {
                var dir = new DirectoryInfo(path);
                while (dir != null)
                {
                    if (Directory.Exists(System.IO.Path.Combine(dir.FullName, ".git"))) return Normalize(dir.FullName);
                    dir = dir.Parent;
                }
            }
            catch { }
            return null;
        }

        private static DateTime WorktreeLastActivityUtc(string worktree)
        {
            DateTime latest = DateTime.MinValue;
            try
            {
                latest = Directory.GetLastWriteTimeUtc(worktree);
                string gitFile = System.IO.Path.Combine(worktree, ".git");
                string content = File.ReadAllText(gitFile).Trim();
                const string prefix = "gitdir:";
                if (content.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    string admin = content.Substring(prefix.Length).Trim();
                    foreach (var name in new[] { "HEAD", "index", "logs\\HEAD" })
                    {
                        string p = System.IO.Path.Combine(admin, name);
                        if (File.Exists(p)) latest = Max(latest, File.GetLastWriteTimeUtc(p));
                    }
                }
            }
            catch { }
            return latest;
        }

        // ---------------------------------------------------------------- clean

        /// <summary>
        /// Exact-match gate: an item is deletable only when its path sits directly under the
        /// root its kind belongs to and matches that kind's name pattern. Scan results are
        /// never trusted on their own.
        /// </summary>
        public bool IsDeletionAllowed(AiLeftoverItem item)
        {
            if (item == null || !item.CanDelete || string.IsNullOrWhiteSpace(item.Path)) return false;
            string path = Normalize(item.Path);
            string parent = Normalize(System.IO.Path.GetDirectoryName(path) ?? string.Empty);
            string name = System.IO.Path.GetFileName(path);
            if (FileSystemSafety.IsReparsePoint(path)) return false;

            switch (item.Kind)
            {
                case AiLeftoverKind.TempScratch:
                    return (Eq(parent, ClaudeTempRoot) && Directory.Exists(path)) ||
                           (Eq(parent, _tempRoot) && IsClaudeCwdFileName(name) && File.Exists(path));
                case AiLeftoverKind.StaleLock:
                    return Eq(parent, SessionsRoot) && (SessionJsonRegex.IsMatch(name) || SessionKeyRegex.IsMatch(name));
                case AiLeftoverKind.McpLog:
                    return Eq(parent, McpCacheRoot);
                case AiLeftoverKind.Worktree:
                    if (string.IsNullOrWhiteSpace(item.RepoPath)) return false;
                    string repo = Normalize(item.RepoPath);
                    return item.IsDanglingRegistration
                        ? Eq(parent, System.IO.Path.Combine(repo, ".git", "worktrees"))
                        : Eq(parent, System.IO.Path.Combine(repo, ".claude", "worktrees"));
                default:
                    return false;
            }
        }

        public AiLeftoverCleanResult Clean(IEnumerable<AiLeftoverItem> items, int retentionDays)
        {
            var result = new AiLeftoverCleanResult();
            DateTime nowUtc = DateTime.UtcNow;
            var prunedRepos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in items)
            {
                if (!IsDeletionAllowed(item))
                {
                    result.Failures.Add(item.DisplayPath + ": rechazado por la lista blanca");
                    continue;
                }

                try
                {
                    switch (item.Kind)
                    {
                        case AiLeftoverKind.TempScratch:
                            CleanScratch(item, result);
                            break;
                        case AiLeftoverKind.StaleLock:
                            CleanLock(item, result);
                            break;
                        case AiLeftoverKind.McpLog:
                            foreach (var f in EnumerateStaleMcpLogs(item.Path, retentionDays, nowUtc).ToList())
                            {
                                long len = f.Length;
                                f.Delete();
                                result.BytesFreed += len;
                            }
                            foreach (var logDir in SafeEnumerateDirectories(item.Path, "mcp-logs-*"))
                            {
                                if (!Directory.EnumerateFileSystemEntries(logDir).Any()) Directory.Delete(logDir, false);
                            }
                            result.Removed++;
                            break;
                        case AiLeftoverKind.Worktree:
                            CleanWorktree(item, result, prunedRepos);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    result.Failures.Add(item.DisplayPath + ": " + ex.Message);
                }
            }
            return result;
        }

        private static void CleanScratch(AiLeftoverItem item, AiLeftoverCleanResult result)
        {
            if (File.Exists(item.Path))
            {
                long len = new FileInfo(item.Path).Length;
                File.Delete(item.Path);
                result.BytesFreed += len;
                result.Removed++;
                return;
            }

            // Re-applies the dual 24h timestamp per file, so anything written since the scan survives.
            var cleaned = SafeTempCleaner.CleanWhitelistedCache(item.Path, (int)ScratchMinimumAge.TotalHours);
            result.BytesFreed += cleaned.BytesFreed;
            try
            {
                if (Directory.Exists(item.Path) && !Directory.EnumerateFileSystemEntries(item.Path).Any())
                {
                    Directory.Delete(item.Path, false);
                }
            }
            catch { }
            result.Removed++;
        }

        private static void CleanLock(AiLeftoverItem item, AiLeftoverCleanResult result)
        {
            // The process could have been restarted under the same PID since the scan: re-check.
            string json = File.ReadAllText(item.Path);
            if (!TryParseSessionRecord(System.IO.Path.GetFileName(item.Path), json, out int pid, out long ft) ||
                !IsRecordStale(ft, TryGetProcessStartUtc(pid)))
            {
                result.Failures.Add(item.DisplayPath + ": el proceso volvió a estar vivo");
                return;
            }
            long len = new FileInfo(item.Path).Length;
            File.Delete(item.Path);
            result.BytesFreed += len;
            result.Removed++;
        }

        private static void CleanWorktree(AiLeftoverItem item, AiLeftoverCleanResult result, HashSet<string> prunedRepos)
        {
            if (item.IsDanglingRegistration)
            {
                if (prunedRepos.Add(item.RepoPath) && RunGit(item.RepoPath, "worktree", "prune") == null)
                {
                    result.Failures.Add(item.DisplayPath + ": git worktree prune falló");
                    return;
                }
                result.Removed++;
                return;
            }

            // Never remove a worktree that picked up changes after the scan.
            string? porcelain = RunGit(item.Path, "status", "--porcelain");
            if (porcelain == null || porcelain.Trim().Length > 0)
            {
                result.Failures.Add(item.DisplayPath + ": tiene cambios sin commitear");
                return;
            }
            if (RunGit(item.RepoPath, "worktree", "remove", item.Path) == null)
            {
                result.Failures.Add(item.DisplayPath + ": git worktree remove falló");
                return;
            }
            result.BytesFreed += item.SizeBytes;
            result.Removed++;
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>Runs git with an argument list; returns stdout, or null on failure or timeout.</summary>
        private static string? RunGit(string workingDirectory, params string[] args)
        {
            try
            {
                var psi = new ProcessStartInfo("git")
                {
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                foreach (var a in args) psi.ArgumentList.Add(a);
                using var proc = Process.Start(psi);
                if (proc == null) return null;
                var stdout = proc.StandardOutput.ReadToEndAsync();
                proc.StandardError.ReadToEndAsync();
                if (!proc.WaitForExit((int)GitTimeout.TotalMilliseconds))
                {
                    try { proc.Kill(true); } catch { }
                    return null;
                }
                return proc.ExitCode == 0 ? stdout.Result : null;
            }
            catch
            {
                return null;
            }
        }

        private static DateTime? TryGetProcessStartUtc(int pid)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                return p.StartTime.ToUniversalTime();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Newest write time in a tree, walked manually and without entering reparse points.</summary>
        private static DateTime NewestWriteUtc(DirectoryInfo root, CancellationToken token, int maxDepth = 12)
        {
            DateTime newest = root.LastWriteTimeUtc;
            var stack = new Stack<(DirectoryInfo Dir, int Depth)>();
            stack.Push((root, 0));
            while (stack.Count > 0)
            {
                if (token.IsCancellationRequested) break;
                var (dir, depth) = stack.Pop();
                IEnumerable<FileSystemInfo> entries;
                try { entries = dir.EnumerateFileSystemInfos("*", SearchOption.TopDirectoryOnly).ToList(); }
                catch { continue; }
                foreach (var e in entries)
                {
                    newest = Max(newest, e.LastWriteTimeUtc);
                    if (e is DirectoryInfo sub && depth < maxDepth && !FileSystemSafety.IsReparsePoint(sub))
                    {
                        stack.Push((sub, depth + 1));
                    }
                }
            }
            return newest;
        }

        private static IEnumerable<string> SafeEnumerateFiles(string dir, string pattern)
        {
            try { return Directory.Exists(dir) ? Directory.GetFiles(dir, pattern, SearchOption.TopDirectoryOnly) : Array.Empty<string>(); }
            catch { return Array.Empty<string>(); }
        }

        private static IEnumerable<string> SafeEnumerateDirectories(string dir, string pattern = "*")
        {
            try { return Directory.Exists(dir) ? Directory.GetDirectories(dir, pattern, SearchOption.TopDirectoryOnly) : Array.Empty<string>(); }
            catch { return Array.Empty<string>(); }
        }

        private string ToDisplayPath(string path)
        {
            string full = Normalize(path);
            if (IsSameOrUnder(full, _tempRoot)) return "%TEMP%" + full.Substring(_tempRoot.Length);
            if (IsSameOrUnder(full, _localAppData)) return "%LOCALAPPDATA%" + full.Substring(_localAppData.Length);
            string profile = Normalize(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            if (IsSameOrUnder(full, profile)) return "%USERPROFILE%" + full.Substring(profile.Length);
            return full;
        }

        private static bool IsSameOrUnder(string path, string root)
        {
            string p = Normalize(path);
            string r = Normalize(root);
            return Eq(p, r) || p.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase);
        }

        private static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            try { return System.IO.Path.GetFullPath(path).TrimEnd('\\', '/'); }
            catch { return path.TrimEnd('\\', '/'); }
        }

        private static bool Eq(string a, string b) => string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

        private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
    }
}
