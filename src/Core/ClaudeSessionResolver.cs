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
                    meta.ContextDisplay = "🖥️ Claude Desktop App";
                    meta.IsDetected = true;
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
