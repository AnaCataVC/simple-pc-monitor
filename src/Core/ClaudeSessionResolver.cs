using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SystemCoreMonitor.Core
{
    public class ClaudeSessionMeta
    {
        public bool IsDetected { get; set; }
        public string AgentDisplayName { get; set; } = "Claude Code / Desktop";
        public string SessionTitle { get; set; } = string.Empty;
        public string WorkspaceRepo { get; set; } = string.Empty;
        public string ContextDisplay { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string Entrypoint { get; set; } = string.Empty;
    }

    public static class ClaudeSessionResolver
    {
        private static readonly string ClaudeSessionsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude",
            "sessions"
        );

        public static ClaudeSessionMeta ResolveSession(int pid, IEnumerable<int>? descendantPids = null, string? processPath = null)
        {
            var meta = new ClaudeSessionMeta();

            // 1. Direct PID lookup in ~/.claude/sessions/<pid>.json
            string directFile = Path.Combine(ClaudeSessionsDir, string.Format("{0}.json", pid));
            if (File.Exists(directFile))
            {
                if (TryLoadSessionFile(directFile, meta))
                {
                    meta.IsDetected = true;
                    return meta;
                }
            }

            // 2. If not found directly on root, inspect descendant PIDs (e.g. Claude Desktop app host)
            if (descendantPids != null)
            {
                foreach (int childPid in descendantPids)
                {
                    string childFile = Path.Combine(ClaudeSessionsDir, string.Format("{0}.json", childPid));
                    if (File.Exists(childFile))
                    {
                        var childMeta = new ClaudeSessionMeta();
                        if (TryLoadSessionFile(childFile, childMeta))
                        {
                            meta.IsDetected = true;
                            meta.AgentDisplayName = "Claude Desktop";
                            meta.SessionTitle = childMeta.SessionTitle;
                            meta.WorkspaceRepo = childMeta.WorkspaceRepo;
                            meta.ContextDisplay = childMeta.ContextDisplay;
                            meta.Model = childMeta.Model;
                            meta.Entrypoint = "claude-desktop";
                            return meta;
                        }
                    }
                }
            }

            // 3. Fallback based on executable path or known launcher markers
            if (!string.IsNullOrEmpty(processPath))
            {
                string normPath = processPath.ToLowerInvariant();
                if (normPath.Contains("windowsapps\\claude") || normPath.Contains("appdata\\local\\programs\\claude"))
                {
                    meta.AgentDisplayName = "Claude Desktop";
                    meta.IsDetected = true;

                    // Inspect descendant processes for model flag (e.g. claude.exe --model ...)
                    if (descendantPids != null)
                    {
                        foreach (int childPid in descendantPids)
                        {
                            string childCmd = ProcessManager.GetProcessCommandLine(childPid);
                            if (!string.IsNullOrEmpty(childCmd))
                            {
                                var matchModel = System.Text.RegularExpressions.Regex.Match(childCmd, @"--model[\s=]+([a-zA-Z0-9_\-\.]+)");
                                if (matchModel.Success)
                                {
                                    meta.Model = matchModel.Groups[1].Value;
                                    break;
                                }
                            }
                        }
                    }

                    // Resolve active workspace and user prompt from ~/.claude/projects/
                    ResolveLatestDesktopProjectSession(meta);
                    return meta;
                }
                if (normPath.Contains(".local\\bin\\claude") || normPath.Contains("node_modules\\@anthropic-ai\\claude-code"))
                {
                    meta.AgentDisplayName = "Claude Code (CLI)";
                    meta.ContextDisplay = "🤖 Claude CLI Session";
                    meta.IsDetected = true;
                    return meta;
                }
            }

            return meta;
        }

        private static readonly string ClaudeProjectsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude",
            "projects"
        );

        private static void ResolveLatestDesktopProjectSession(ClaudeSessionMeta meta)
        {
            if (!Directory.Exists(ClaudeProjectsDir))
            {
                meta.ContextDisplay = "🖥️ Claude Desktop App";
                return;
            }

            try
            {
                var dirInfo = new DirectoryInfo(ClaudeProjectsDir);
                FileInfo? newestJsonl = null;

                foreach (var projDir in dirInfo.EnumerateDirectories())
                {
                    // Skip internal subagents directories when finding main session
                    if (projDir.Name.Equals("subagents", StringComparison.OrdinalIgnoreCase)) continue;

                    try
                    {
                        foreach (var file in projDir.EnumerateFiles("*.jsonl", SearchOption.TopDirectoryOnly))
                        {
                            if (newestJsonl == null || file.LastWriteTime > newestJsonl.LastWriteTime)
                            {
                                newestJsonl = file;
                            }
                        }
                    }
                    catch { }
                }

                if (newestJsonl == null)
                {
                    meta.ContextDisplay = "🖥️ Claude Desktop App";
                    return;
                }

                string folderName = newestJsonl.Directory?.Name ?? string.Empty;
                string repoName = DecodeWorkspaceFromFolder(folderName);
                string promptTitle = ExtractPromptFromJsonl(newestJsonl.FullName);

                meta.WorkspaceRepo = repoName;
                meta.SessionTitle = promptTitle;
                meta.ContextDisplay = FormatContext(repoName, promptTitle);
            }
            catch
            {
                if (string.IsNullOrEmpty(meta.ContextDisplay))
                {
                    meta.ContextDisplay = "🖥️ Claude Desktop App";
                }
            }
        }

        public static string DecodeWorkspaceFromFolder(string folderName)
        {
            if (string.IsNullOrWhiteSpace(folderName)) return string.Empty;

            try
            {
                int wtIdx = folderName.IndexOf("--claude-worktrees-", StringComparison.OrdinalIgnoreCase);
                string worktree = string.Empty;
                string baseFolder = folderName;
                if (wtIdx > 0)
                {
                    worktree = folderName.Substring(wtIdx + "--claude-worktrees-".Length);
                    baseFolder = folderName.Substring(0, wtIdx);
                }

                var match = System.Text.RegularExpressions.Regex.Match(baseFolder, @"(?i)(?:Repositories|Repos)[-_]+([a-zA-Z0-9_\-]+)$");
                string repo = match.Success ? match.Groups[1].Value : string.Empty;

                if (string.IsNullOrEmpty(repo))
                {
                    var parts = baseFolder.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0)
                    {
                        repo = parts[parts.Length - 1];
                    }
                }

                // Never leak user account name or bare drive letter as workspace
                if (string.Equals(repo, "C", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(repo, Environment.UserName, StringComparison.OrdinalIgnoreCase))
                {
                    return !string.IsNullOrEmpty(worktree) ? string.Format("Worktree [{0}]", worktree) : "Workspace";
                }

                if (!string.IsNullOrEmpty(worktree))
                {
                    return string.Format("{0} [{1}]", repo, worktree);
                }

                return repo;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string ExtractPromptFromJsonl(string filePath)
        {
            if (!File.Exists(filePath)) return string.Empty;

            try
            {
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream))
                {
                    string? line;
                    int linesChecked = 0;
                    while ((line = reader.ReadLine()) != null && linesChecked < 25)
                    {
                        linesChecked++;
                        int contentIdx = line.IndexOf("\"content\":\"", StringComparison.Ordinal);
                        if (contentIdx >= 0)
                        {
                            int start = contentIdx + 11;
                            int end = line.IndexOf("\"", start, StringComparison.Ordinal);
                            if (end > start)
                            {
                                string prompt = line.Substring(start, end - start);
                                prompt = prompt.Replace("\\\"", "\"").Replace("\\n", " ").Trim();
                                if (prompt.StartsWith("<system-reminder", StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }
                                if (prompt.Length > 45)
                                {
                                    return prompt.Substring(0, 42) + "...";
                                }
                                if (!string.IsNullOrWhiteSpace(prompt))
                                {
                                    return prompt;
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            return string.Empty;
        }

        private static bool TryLoadSessionFile(string filePath, ClaudeSessionMeta meta)
        {
            try
            {
                string json = File.ReadAllText(filePath);
                using (var doc = JsonDocument.Parse(json))
                {
                    var root = doc.RootElement;

                    string entrypoint = root.TryGetProperty("entrypoint", out var ep) ? ep.GetString() ?? string.Empty : string.Empty;
                    string name = root.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                    string cwd = root.TryGetProperty("cwd", out var c) ? c.GetString() ?? string.Empty : string.Empty;
                    string model = root.TryGetProperty("model", out var m) ? m.GetString() ?? string.Empty : string.Empty;

                    meta.Entrypoint = entrypoint;
                    meta.SessionTitle = name.Trim();
                    meta.Model = model.Trim();

                    if (string.Equals(entrypoint, "cli", StringComparison.OrdinalIgnoreCase))
                    {
                        meta.AgentDisplayName = "Claude Code (CLI)";
                    }
                    else if (string.Equals(entrypoint, "claude-desktop", StringComparison.OrdinalIgnoreCase))
                    {
                        meta.AgentDisplayName = "Claude Desktop";
                    }
                    else
                    {
                        meta.AgentDisplayName = "Claude";
                    }

                    meta.WorkspaceRepo = ExtractRepoName(cwd);
                    meta.ContextDisplay = FormatContext(meta.WorkspaceRepo, meta.SessionTitle);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public static string ExtractRepoName(string cwd)
        {
            if (string.IsNullOrWhiteSpace(cwd)) return string.Empty;

            try
            {
                string clean = cwd.Replace('/', '\\').TrimEnd('\\');

                // Check for Claude worktree structure: ...\repo\.claude\worktrees\<branch>
                int wtIdx = clean.IndexOf(".claude\\worktrees\\", StringComparison.OrdinalIgnoreCase);
                if (wtIdx > 0)
                {
                    string parentRepoPath = clean.Substring(0, wtIdx).TrimEnd('\\');
                    string repoName = Path.GetFileName(parentRepoPath);
                    string worktreeName = Path.GetFileName(clean);
                    if (!string.IsNullOrEmpty(repoName) && !string.IsNullOrEmpty(worktreeName))
                    {
                        return string.Format("{0} [{1}]", repoName, worktreeName);
                    }
                }

                // Standard folder / repo name
                string folder = Path.GetFileName(clean);
                return folder ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        public static string FormatContext(string repo, string sessionName)
        {
            if (!string.IsNullOrEmpty(repo) && !string.IsNullOrEmpty(sessionName))
            {
                return string.Format("📁 {0} • {1}", repo, sessionName);
            }
            if (!string.IsNullOrEmpty(repo))
            {
                return string.Format("📁 {0}", repo);
            }
            if (!string.IsNullOrEmpty(sessionName))
            {
                return string.Format("⚡ {0}", sessionName);
            }
            return "🤖 Claude Session";
        }
    }
}
