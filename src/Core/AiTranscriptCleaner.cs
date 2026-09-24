using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using SystemCoreMonitor.Models;

namespace SystemCoreMonitor.Core
{
    public class AiTranscriptCleaner
    {
        /// <summary>
        /// Safety guard: never touch transcripts modified in the last 24 hours even if retention is 0 days.
        /// Protects active, recently resumed, or momentarily idle sessions.
        /// </summary>
        public static readonly TimeSpan ActiveSessionGrace = TimeSpan.FromHours(24);

        private static readonly HashSet<string> ProtectedFileNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "CLAUDE.md",
            "GEMINI.md",
            "settings.json",
            "config.json",
            "history.jsonl",
            "package.json"
        };

        private static readonly HashSet<string> ProtectedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "memory",
            "knowledge",
            ".git",
            ".vscode"
        };

        private readonly string _claudeProjectsRoot;
        private readonly string _claudeLiveSessionsRoot;
        private readonly string _claudeDesktopRoot;
        private readonly string _geminiBrainRoot;
        private readonly string _geminiConversationsRoot;

        public AiTranscriptCleaner() : this(null, null, null, null, null) { }

        public AiTranscriptCleaner(
            string? claudeProjectsRoot = null,
            string? claudeLiveSessionsRoot = null,
            string? claudeDesktopRoot = null,
            string? geminiBrainRoot = null,
            string? geminiConversationsRoot = null)
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            _claudeProjectsRoot = claudeProjectsRoot ?? Path.Combine(userProfile, ".claude", "projects");
            _claudeLiveSessionsRoot = claudeLiveSessionsRoot ?? Path.Combine(userProfile, ".claude", "sessions");
            _claudeDesktopRoot = claudeDesktopRoot ?? ResolveClaudeDesktopSessionsRoot();
            _geminiBrainRoot = geminiBrainRoot ?? Path.Combine(userProfile, ".gemini", "antigravity", "brain");
            _geminiConversationsRoot = geminiConversationsRoot ?? Path.Combine(userProfile, ".gemini", "antigravity-cli", "conversations");
        }

        public static string ResolveClaudeDesktopSessionsRoot()
        {
            try
            {
                string classicRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude", "claude-code-sessions");
                if (Directory.Exists(classicRoot))
                {
                    return classicRoot;
                }

                string packagesDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
                if (Directory.Exists(packagesDir))
                {
                    foreach (var packageDir in Directory.GetDirectories(packagesDir, "Claude_*"))
                    {
                        string packagedRoot = Path.Combine(packageDir, "LocalCache", "Roaming", "Claude", "claude-code-sessions");
                        if (Directory.Exists(packagedRoot))
                        {
                            return packagedRoot;
                        }
                    }
                }

                return classicRoot;
            }
            catch
            {
                return string.Empty;
            }
        }

        public static bool IsClaudeProcessRunning()
        {
            try
            {
                var names = new[] { "claude", "claude-code", "Claude" };
                foreach (var name in names)
                {
                    var procs = Process.GetProcessesByName(name);
                    try
                    {
                        if (procs.Length > 0) return true;
                    }
                    finally
                    {
                        foreach (var p in procs) p.Dispose();
                    }
                }
            }
            catch { }
            return false;
        }

        public static bool IsAntigravityProcessRunning()
        {
            try
            {
                var names = new[] { "antigravity", "agy" };
                foreach (var name in names)
                {
                    var procs = Process.GetProcessesByName(name);
                    try
                    {
                        if (procs.Length > 0) return true;
                    }
                    finally
                    {
                        foreach (var p in procs) p.Dispose();
                    }
                }
            }
            catch { }
            return false;
        }

        public Dictionary<string, (int Pid, string Cwd, string Name)> LoadVerifiedLiveClaudeSessions()
        {
            var result = new Dictionary<string, (int Pid, string Cwd, string Name)>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(_claudeLiveSessionsRoot)) return result;

            try
            {
                foreach (var file in Directory.GetFiles(_claudeLiveSessionsRoot, "*.json", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(file));
                        var root = doc.RootElement;

                        if (!root.TryGetProperty("sessionId", out var sidProp) ||
                            !root.TryGetProperty("pid", out var pidProp) ||
                            !root.TryGetProperty("procStart", out var procStartProp))
                        {
                            continue;
                        }

                        string sid = sidProp.GetString() ?? string.Empty;
                        int pid = pidProp.GetInt32();
                        string cwd = root.TryGetProperty("cwd", out var cwdProp) ? cwdProp.GetString() ?? string.Empty : string.Empty;
                        string name = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty;

                        if (string.IsNullOrEmpty(sid) || !long.TryParse(procStartProp.GetString(), out long procStartFileTime))
                        {
                            continue;
                        }

                        if (IsProcessGenuinelyAlive(pid, procStartFileTime))
                        {
                            result[sid] = (pid, cwd, name);
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return result;
        }

        private static bool IsProcessGenuinelyAlive(int pid, long recordedStartFileTime)
        {
            if (pid <= 4) return false;
            try
            {
                using var proc = Process.GetProcessById(pid);
                DateTime startTime = proc.StartTime;
                // Claude records procStart as a Win32 FILETIME, not .NET ticks.
                long actualFileTime = startTime.ToFileTimeUtc();
                return Math.Abs(actualFileTime - recordedStartFileTime) < TimeSpan.FromSeconds(3).Ticks;
            }
            catch
            {
                return false;
            }
        }

        public AiTranscriptReport ScanReport(int retentionDays = 7)
        {
            var report = new AiTranscriptReport
            {
                ClaudeIsRunning = IsClaudeProcessRunning(),
                AntigravityIsRunning = IsAntigravityProcessRunning()
            };

            DateTime now = DateTime.Now;
            DateTime staleThreshold = now.AddDays(-retentionDays);
            DateTime activeGraceThreshold = now - ActiveSessionGrace;

            var liveClaudeSessions = LoadVerifiedLiveClaudeSessions();

            // 1. Scan Claude CLI Projects
            var claudeStore = ScanClaudeCliProjects(_claudeProjectsRoot, staleThreshold, activeGraceThreshold, liveClaudeSessions);
            report.Stores.Add(claudeStore.Store);
            report.Items.AddRange(claudeStore.Items);

            // 2. Scan Gemini & Antigravity Brain / Conversations
            var geminiStore = ScanGeminiStorage(_geminiBrainRoot, _geminiConversationsRoot, staleThreshold, activeGraceThreshold, report.AntigravityIsRunning);
            report.Stores.Add(geminiStore.Store);
            report.Items.AddRange(geminiStore.Items);

            // 3. Scan Claude Desktop Sessions Index
            var desktopStore = ScanClaudeDesktopSessions(_claudeDesktopRoot, staleThreshold, activeGraceThreshold, report.ClaudeIsRunning);
            report.Stores.Add(desktopStore.Store);
            report.Items.AddRange(desktopStore.Items);

            // Aggregate metrics
            report.TotalSizeBytes = report.Stores.Sum(s => s.TotalSizeBytes);
            report.StaleSizeBytes = report.Stores.Sum(s => s.StaleSizeBytes);
            report.TotalFilesCount = report.Stores.Sum(s => s.TotalFileCount);
            report.StaleFilesCount = report.Stores.Sum(s => s.StaleFileCount);

            return report;
        }

        private (AiTranscriptStoreInfo Store, List<AiTranscriptItem> Items) ScanClaudeCliProjects(
            string rootPath,
            DateTime staleThreshold,
            DateTime activeGraceThreshold,
            Dictionary<string, (int Pid, string Cwd, string Name)> liveSessions)
        {
            var store = new AiTranscriptStoreInfo
            {
                StoreName = "Claude Code (CLI Projects)",
                BasePath = rootPath,
                TargetType = AiTranscriptTargetType.ClaudeCli
            };
            var items = new List<AiTranscriptItem>();

            if (!Directory.Exists(rootPath)) return (store, items);

            try
            {
                foreach (var projectDir in Directory.GetDirectories(rootPath))
                {
                    if (FileSystemSafety.IsReparsePoint(projectDir)) continue;

                    string projName = Path.GetFileName(projectDir);
                    if (ProtectedDirectoryNames.Contains(projName)) continue;

                    string[] files;
                    try
                    {
                        files = Directory.GetFiles(projectDir, "*.jsonl", SearchOption.TopDirectoryOnly);
                    }
                    catch { continue; }

                    foreach (var file in files)
                    {
                        var fi = new FileInfo(file);
                        if (ProtectedFileNames.Contains(fi.Name)) continue;

                        string sessionId = Path.GetFileNameWithoutExtension(fi.Name);
                        bool isLive = liveSessions.TryGetValue(sessionId, out var live);

                        bool isRecent = fi.LastWriteTime >= activeGraceThreshold;
                        bool isOlderThanRetention = fi.LastWriteTime < staleThreshold;
                        bool isStale = !isLive && !isRecent && isOlderThanRetention;

                        store.TotalFileCount++;
                        store.TotalSizeBytes += fi.Length;

                        if (isStale)
                        {
                            store.StaleFileCount++;
                            store.StaleSizeBytes += fi.Length;
                        }

                        items.Add(new AiTranscriptItem
                        {
                            SessionId = sessionId,
                            DisplayName = isLive ? (!string.IsNullOrEmpty(live.Name) ? live.Name : sessionId) : sessionId,
                            WorkingDirectory = isLive && !string.IsNullOrEmpty(live.Cwd) ? live.Cwd : projName,
                            FilePath = fi.FullName,
                            TargetType = AiTranscriptTargetType.ClaudeCli,
                            LastModified = fi.LastWriteTime,
                            FileSizeBytes = fi.Length,
                            IsStale = isStale,
                            IsActive = isLive,
                            ProcessId = isLive ? live.Pid : null
                        });
                    }
                }
            }
            catch { }

            return (store, items);
        }

        private (AiTranscriptStoreInfo Store, List<AiTranscriptItem> Items) ScanGeminiStorage(
            string brainRoot,
            string conversationsRoot,
            DateTime staleThreshold,
            DateTime activeGraceThreshold,
            bool isAntigravityRunning)
        {
            var store = new AiTranscriptStoreInfo
            {
                StoreName = "Google Gemini / Antigravity",
                BasePath = brainRoot,
                TargetType = AiTranscriptTargetType.GeminiAntigravity
            };
            var items = new List<AiTranscriptItem>();

            // 1. Antigravity Brain (<conversation-uuid>\.system_generated\logs\transcript*.jsonl)
            if (Directory.Exists(brainRoot))
            {
                try
                {
                    foreach (var convDir in Directory.GetDirectories(brainRoot))
                    {
                        if (FileSystemSafety.IsReparsePoint(convDir)) continue;

                        string convId = Path.GetFileName(convDir);
                        string logsDir = Path.Combine(convDir, ".system_generated", "logs");

                        if (!Directory.Exists(logsDir)) continue;

                        foreach (var file in Directory.GetFiles(logsDir, "*.jsonl", SearchOption.TopDirectoryOnly))
                        {
                            var fi = new FileInfo(file);
                            bool isRecent = fi.LastWriteTime >= activeGraceThreshold;
                            bool isOlderThanRetention = fi.LastWriteTime < staleThreshold;
                            bool isStale = !isRecent && isOlderThanRetention;

                            store.TotalFileCount++;
                            store.TotalSizeBytes += fi.Length;

                            if (isStale)
                            {
                                store.StaleFileCount++;
                                store.StaleSizeBytes += fi.Length;
                            }

                            items.Add(new AiTranscriptItem
                            {
                                SessionId = convId,
                                DisplayName = convId.Length > 8 ? convId.Substring(0, 8) + "..." : convId,
                                WorkingDirectory = "Antigravity Workspace",
                                FilePath = fi.FullName,
                                TargetType = AiTranscriptTargetType.GeminiAntigravity,
                                LastModified = fi.LastWriteTime,
                                FileSizeBytes = fi.Length,
                                IsStale = isStale,
                                IsActive = isRecent && isAntigravityRunning
                            });
                        }
                    }
                }
                catch { }
            }

            // 2. Gemini CLI Conversations (<uuid>.db)
            if (Directory.Exists(conversationsRoot))
            {
                try
                {
                    foreach (var file in Directory.GetFiles(conversationsRoot, "*.db", SearchOption.TopDirectoryOnly))
                    {
                        var fi = new FileInfo(file);
                        bool isRecent = fi.LastWriteTime >= activeGraceThreshold;
                        bool isOlderThanRetention = fi.LastWriteTime < staleThreshold;
                        bool isStale = !isRecent && isOlderThanRetention;

                        store.TotalFileCount++;
                        store.TotalSizeBytes += fi.Length;

                        if (isStale)
                        {
                            store.StaleFileCount++;
                            store.StaleSizeBytes += fi.Length;
                        }

                        items.Add(new AiTranscriptItem
                        {
                            SessionId = Path.GetFileNameWithoutExtension(fi.Name),
                            DisplayName = fi.Name,
                            WorkingDirectory = "Gemini CLI History",
                            FilePath = fi.FullName,
                            TargetType = AiTranscriptTargetType.GeminiAntigravity,
                            LastModified = fi.LastWriteTime,
                            FileSizeBytes = fi.Length,
                            IsStale = isStale,
                            IsActive = false
                        });
                    }
                }
                catch { }
            }

            return (store, items);
        }

        private (AiTranscriptStoreInfo Store, List<AiTranscriptItem> Items) ScanClaudeDesktopSessions(
            string desktopRoot,
            DateTime staleThreshold,
            DateTime activeGraceThreshold,
            bool isClaudeRunning)
        {
            var store = new AiTranscriptStoreInfo
            {
                StoreName = "Claude Desktop (Session Index)",
                BasePath = desktopRoot,
                TargetType = AiTranscriptTargetType.ClaudeDesktop
            };
            var items = new List<AiTranscriptItem>();

            if (!Directory.Exists(desktopRoot)) return (store, items);

            try
            {
                foreach (var file in Directory.GetFiles(desktopRoot, "*.json", SearchOption.TopDirectoryOnly))
                {
                    var fi = new FileInfo(file);
                    if (ProtectedFileNames.Contains(fi.Name)) continue;

                    bool isRecent = fi.LastWriteTime >= activeGraceThreshold;
                    bool isOlderThanRetention = fi.LastWriteTime < staleThreshold;
                    bool isStale = !isRecent && isOlderThanRetention;

                    store.TotalFileCount++;
                    store.TotalSizeBytes += fi.Length;

                    if (isStale)
                    {
                        store.StaleFileCount++;
                        store.StaleSizeBytes += fi.Length;
                    }

                    items.Add(new AiTranscriptItem
                    {
                        SessionId = Path.GetFileNameWithoutExtension(fi.Name),
                        DisplayName = fi.Name,
                        WorkingDirectory = "Claude Desktop",
                        FilePath = fi.FullName,
                        TargetType = AiTranscriptTargetType.ClaudeDesktop,
                        LastModified = fi.LastWriteTime,
                        FileSizeBytes = fi.Length,
                        IsStale = isStale,
                        IsActive = isRecent && isClaudeRunning
                    });
                }
            }
            catch { }

            return (store, items);
        }

        public AiTranscriptCleanupResult CleanStaleTranscripts(int retentionDays = 7, IEnumerable<string>? specificFiles = null)
        {
            var result = new AiTranscriptCleanupResult();
            var report = ScanReport(retentionDays);

            DateTime now = DateTime.Now;
            DateTime activeGraceThreshold = now - ActiveSessionGrace;

            var targetFiles = specificFiles != null
                ? new HashSet<string>(specificFiles, StringComparer.OrdinalIgnoreCase)
                : null;

            var candidates = report.Items.Where(i =>
            {
                if (targetFiles != null)
                {
                    return targetFiles.Contains(i.FilePath);
                }
                return i.IsStale && !i.IsActive;
            }).ToList();

            foreach (var item in candidates)
            {
                // Strict 24-Hour Safety Invariant: NEVER delete files modified in the last 24 hours
                if (item.LastModified >= activeGraceThreshold)
                {
                    result.SkippedCount++;
                    continue;
                }

                // Strict Blacklist Invariant
                string fileName = Path.GetFileName(item.FilePath);
                if (ProtectedFileNames.Contains(fileName))
                {
                    result.SkippedCount++;
                    continue;
                }

                if (!File.Exists(item.FilePath))
                {
                    result.SkippedCount++;
                    continue;
                }

                try
                {
                    long size = new FileInfo(item.FilePath).Length;
                    File.Delete(item.FilePath);
                    result.FilesProcessed++;
                    result.BytesFreed += size;

                    // Clean companion subagent or tool-results directory in Claude CLI if empty or associated
                    if (item.TargetType == AiTranscriptTargetType.ClaudeCli)
                    {
                        TryCleanCompanionDirectory(item.FilePath);
                    }
                }
                catch (Exception ex)
                {
                    result.Failures.Add($"{fileName}: {ex.Message}");
                }
            }

            result.Skipped = result.FilesProcessed == 0 && result.Failures.Count == 0;
            result.Message = result.FilesProcessed == 0
                ? (result.Failures.Count > 0
                    ? $"No se pudieron borrar archivos ({result.Failures.Count} bloqueados o en uso)."
                    : "No había transcripts fuera de la ventana de retención.")
                : (result.Failures.Count > 0
                    ? $"Se eliminaron {result.FilesProcessed} transcripts y se liberaron {result.BytesFreedDisplay} ({result.Failures.Count} bloqueados)."
                    : $"Se eliminaron {result.FilesProcessed} transcripts y se liberaron {result.BytesFreedDisplay}.");

            return result;
        }

        private static void TryCleanCompanionDirectory(string transcriptFilePath)
        {
            try
            {
                string dir = Path.GetDirectoryName(transcriptFilePath) ?? string.Empty;
                string sessionId = Path.GetFileNameWithoutExtension(transcriptFilePath);
                string companionDir = Path.Combine(dir, sessionId);

                if (Directory.Exists(companionDir) && !FileSystemSafety.IsReparsePoint(companionDir))
                {
                    Directory.Delete(companionDir, true);
                }
            }
            catch { }
        }
    }
}
