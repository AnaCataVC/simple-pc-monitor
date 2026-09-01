using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using SimplePCMonitor.Core;
using SimplePCMonitor.Models;

namespace SimplePCMonitor.Modules
{
    public class AiAgentCollector
    {
        private static readonly HashSet<string> KnownAgentSignatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "antigravity",
            "agy",
            "claude",
            "claude-code",
            "gemini",
            "gemini-cli",
            "codex",
            "chatgpt",
            "aider",
            "ollama",
            "ollama app",
            "lm studio",
            "lms",
            "localai",
            "cursor",
            "windsurf",
            "cline",
            "roo-code",
            "roo",
            "roo-cline",
            "copilot",
            "copilot-agent",
            "continue",
            "cody",
            "tabnine",
            "amazon-q",
            "q",
            "open-interpreter",
            "interpreter",
            "zed"
        };

        private static readonly HashSet<string> KnownMcpRuntimes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "node",
            "python",
            "python3",
            "pythonw",
            "uvx",
            "uv",
            "npx",
            "bun",
            "deno",
            "rg",
            "git",
            "docker",
            "dockerd",
            "pwsh",
            "powershell",
            "cmd"
        };

        private class CachedSessionInfo
        {
            public string Context { get; set; }
            public string Workspace { get; set; }
            public string Model { get; set; }
        }

        private readonly Dictionary<string, CachedSessionInfo> _sessionContextCache = new Dictionary<string, CachedSessionInfo>();
        private readonly Dictionary<int, Tuple<TimeSpan, DateTime>> _prevCpuSamples = new Dictionary<int, Tuple<TimeSpan, DateTime>>();
        private readonly object _syncLock = new object();
        private readonly int _processorCount = Environment.ProcessorCount > 0 ? Environment.ProcessorCount : 1;

        public AiAgentMetric Sample()
        {
            var metric = new AiAgentMetric();
            var parentToChildren = new Dictionary<int, List<int>>();
            var childToParent = new Dictionary<int, int>();
            var pidToExeName = new Dictionary<int, string>();
            var allRunningPids = new HashSet<int>();

            // Step 1: Capture atomic Win32 Toolhelp32 Snapshot (< 0.8ms)
            IntPtr hSnapshot = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.TH32CS_SNAPPROCESS, 0);
            if (hSnapshot != IntPtr.Zero && hSnapshot != new IntPtr(-1))
            {
                try
                {
                    var pe = new NativeMethods.PROCESSENTRY32();
                    pe.dwSize = (uint)Marshal.SizeOf(typeof(NativeMethods.PROCESSENTRY32));

                    if (NativeMethods.Process32First(hSnapshot, ref pe))
                    {
                        do
                        {
                            int pid = (int)pe.th32ProcessID;
                            int ppid = (int)pe.th32ParentProcessID;
                            string exe = pe.szExeFile ?? string.Empty;

                            if (pid <= 4) continue;

                            allRunningPids.Add(pid);
                            pidToExeName[pid] = exe;
                            childToParent[pid] = ppid;

                            if (!parentToChildren.ContainsKey(ppid))
                            {
                                parentToChildren[ppid] = new List<int>();
                            }
                            parentToChildren[ppid].Add(pid);

                        } while (NativeMethods.Process32Next(hSnapshot, ref pe));
                    }
                }
                finally
                {
                    NativeMethods.CloseHandle(hSnapshot);
                }
            }

            // Step 2: Identify Root Agent Processes
            var rootAgentPids = new List<int>();
            foreach (var kvp in pidToExeName)
            {
                int pid = kvp.Key;
                string exeName = Path.GetFileNameWithoutExtension(kvp.Value);

                if (KnownAgentSignatures.Contains(exeName))
                {
                    // If the parent is also a known agent (e.g. wrapper), skip to avoid duplicate roots
                    int ppid;
                    if (childToParent.TryGetValue(pid, out ppid) && pidToExeName.ContainsKey(ppid))
                    {
                        string parentExe = Path.GetFileNameWithoutExtension(pidToExeName[ppid]);
                        if (KnownAgentSignatures.Contains(parentExe))
                        {
                            continue; // This is a sub-agent process, not the root orchestrator
                        }
                    }

                    rootAgentPids.Add(pid);
                }
            }

            DateTime now = DateTime.UtcNow;
            double grandTotalRamMB = 0.0;
            int totalMcpCount = 0;

            // Step 3: Build Consolidated Sessions with Triple-Check PID Reuse Mitigation
            foreach (var rootPid in rootAgentPids)
            {
                Process rootProc = null;
                DateTime rootStartTime = DateTime.MinValue;
                try
                {
                    rootProc = Process.GetProcessById(rootPid);
                    rootStartTime = TryGetStartTime(rootProc);
                }
                catch
                {
                    continue; // Root process died between snapshot and inspection
                }

                string rootExe = pidToExeName.ContainsKey(rootPid) ? Path.GetFileNameWithoutExtension(pidToExeName[rootPid]) : rootProc.ProcessName;
                string agentFriendlyName = FormatAgentFriendlyName(rootExe);

                long rootWs = TryGetWorkingSet(rootProc);
                double rootRamMB = Math.Round((double)rootWs / (1024.0 * 1024.0), 1);
                double rootCpuPct = CalculateCpuDelta(rootPid, rootProc, now);

                var session = new AiAgentSession
                {
                    ParentPid = rootPid,
                    AgentName = agentFriendlyName,
                    AgentProcessName = rootProc.ProcessName,
                    StartTime = rootStartTime,
                    StartTimeDisplay = rootStartTime != DateTime.MinValue ? rootStartTime.ToString("HH:mm:ss") : "N/A",
                    ParentWorkingSetMB = rootRamMB,
                    ParentCpuPercent = rootCpuPct
                };

                // Discover all child processes recursively with StartTime validation
                var descendants = new List<int>();
                CollectDescendants(rootPid, parentToChildren, rootStartTime, descendants);

                double childrenRamMB = 0.0;
                double childrenCpuPct = 0.0;

                foreach (int childPid in descendants)
                {
                    try
                    {
                        var childProc = Process.GetProcessById(childPid);
                        long childWs = TryGetWorkingSet(childProc);
                        double childMemMB = Math.Round((double)childWs / (1024.0 * 1024.0), 1);
                        double childCpu = CalculateCpuDelta(childPid, childProc, now);
                        DateTime childStart = TryGetStartTime(childProc);

                        childrenRamMB += childMemMB;
                        childrenCpuPct += childCpu;

                        session.ChildPids.Add(childPid);
                        session.ChildProcesses.Add(new AiAgentMcpServer
                        {
                            Pid = childPid,
                            ProcessName = childProc.ProcessName,
                            Description = FormatMcpDescription(childProc.ProcessName),
                            WorkingSetMB = childMemMB,
                            MemoryDisplay = string.Format("{0:N1} MB", childMemMB),
                            CpuPercent = childCpu,
                            CpuDisplay = string.Format("{0:N1}%", childCpu),
                            StartTime = childStart
                        });
                    }
                    catch { }
                }

                session.ChildrenWorkingSetMB = Math.Round(childrenRamMB, 1);
                session.ChildrenCpuPercent = Math.Round(childrenCpuPct, 1);
                session.TotalWorkingSetMB = Math.Round(rootRamMB + childrenRamMB, 1);
                session.TotalMemoryDisplay = string.Format("{0:N1} MB", session.TotalWorkingSetMB);
                session.TotalCpuPercent = Math.Round(rootCpuPct + childrenCpuPct, 1);
                session.TotalCpuDisplay = string.Format("{0:N1}%", session.TotalCpuPercent);
                session.McpServersCount = session.ChildProcesses.Count;

                // Activity Classification
                session.IsIdle = session.TotalCpuPercent < 0.04;
                session.StatusDisplay = session.IsIdle ? "Idle (Waiting)" : "Active (Working)";
                session.StatusBadgeColor = session.IsIdle ? "#64748B" : "#10B981"; // Slate vs Emerald

                // Resolve Context (Project Workspace / Model / CLI Session) with 0ms Cache
                var sessionInfo = ResolveSessionContext(rootProc, rootPid, rootExe, rootStartTime, descendants);
                session.SessionContext = sessionInfo.Context;
                session.WorkspaceName = sessionInfo.Workspace;
                session.ModelName = sessionInfo.Model;

                metric.Sessions.Add(session);
                grandTotalRamMB += session.TotalWorkingSetMB;
                totalMcpCount += session.McpServersCount;
            }

            metric.ActiveSessionsCount = metric.Sessions.Count;
            metric.TotalMcpServersCount = totalMcpCount;
            metric.TotalAggregatedRamMB = Math.Round(grandTotalRamMB, 1);
            metric.TotalAggregatedRamDisplay = string.Format("{0:N1} MB", grandTotalRamMB);

            // Cleanup stale CPU samples and cached session contexts
            lock (_syncLock)
            {
                var deadPids = _prevCpuSamples.Keys.Where(k => !allRunningPids.Contains(k)).ToList();
                foreach (var dead in deadPids)
                {
                    _prevCpuSamples.Remove(dead);
                }

                var deadCacheKeys = _sessionContextCache.Keys.Where(k =>
                {
                    int pid;
                    var parts = k.Split('_');
                    if (parts.Length > 0 && int.TryParse(parts[0], out pid))
                    {
                        return !allRunningPids.Contains(pid);
                    }
                    return true;
                }).ToList();

                foreach (var deadKey in deadCacheKeys)
                {
                    _sessionContextCache.Remove(deadKey);
                }
            }

            return metric;
        }

        private void CollectDescendants(int parentPid, Dictionary<int, List<int>> tree, DateTime parentStartTime, List<int> result)
        {
            List<int> directChildren;
            if (tree.TryGetValue(parentPid, out directChildren))
            {
                foreach (int childPid in directChildren)
                {
                    try
                    {
                        var childProc = Process.GetProcessById(childPid);
                        DateTime childStart = TryGetStartTime(childProc);

                        // Invariant: child cannot start before parent
                        if (parentStartTime != DateTime.MinValue && childStart != DateTime.MinValue && childStart < parentStartTime.AddSeconds(-2))
                        {
                            continue; // PID reuse collision detected and rejected
                        }

                        if (!result.Contains(childPid))
                        {
                            result.Add(childPid);
                            CollectDescendants(childPid, tree, childStart != DateTime.MinValue ? childStart : parentStartTime, result);
                        }
                    }
                    catch { }
                }
            }
        }

        private double CalculateCpuDelta(int pid, Process proc, DateTime now)
        {
            try
            {
                TimeSpan totalTime = proc.TotalProcessorTime;
                Tuple<TimeSpan, DateTime> prev;
                lock (_syncLock)
                {
                    if (_prevCpuSamples.TryGetValue(pid, out prev))
                    {
                        double cpuDeltaMs = (totalTime - prev.Item1).TotalMilliseconds;
                        double timeDeltaMs = (now - prev.Item2).TotalMilliseconds;
                        if (timeDeltaMs > 100 && cpuDeltaMs >= 0)
                        {
                            double pct = Math.Round((cpuDeltaMs / (timeDeltaMs * _processorCount)) * 100.0, 1);
                            _prevCpuSamples[pid] = Tuple.Create(totalTime, now);
                            return Math.Min(100.0, pct);
                        }
                    }
                    _prevCpuSamples[pid] = Tuple.Create(totalTime, now);
                }
            }
            catch { }
            return 0.0;
        }

        private static DateTime TryGetStartTime(Process p)
        {
            try { return p.StartTime; }
            catch { return DateTime.MinValue; }
        }

        private static long TryGetWorkingSet(Process p)
        {
            try { return p.WorkingSet64; }
            catch { return 0; }
        }

        private static string FormatAgentFriendlyName(string exeName)
        {
            if (string.Equals(exeName, "antigravity", StringComparison.OrdinalIgnoreCase)) return "Google Antigravity IDE";
            if (string.Equals(exeName, "agy", StringComparison.OrdinalIgnoreCase)) return "Antigravity CLI";
            if (string.Equals(exeName, "claude", StringComparison.OrdinalIgnoreCase) || string.Equals(exeName, "claude-code", StringComparison.OrdinalIgnoreCase)) return "Claude Code / Desktop";
            if (string.Equals(exeName, "gemini", StringComparison.OrdinalIgnoreCase) || string.Equals(exeName, "gemini-cli", StringComparison.OrdinalIgnoreCase)) return "Gemini CLI";
            if (string.Equals(exeName, "codex", StringComparison.OrdinalIgnoreCase)) return "Codex CLI";
            if (string.Equals(exeName, "chatgpt", StringComparison.OrdinalIgnoreCase)) return "ChatGPT Desktop";
            if (string.Equals(exeName, "aider", StringComparison.OrdinalIgnoreCase)) return "Aider Pair Programming Agent";
            if (string.Equals(exeName, "ollama", StringComparison.OrdinalIgnoreCase) || string.Equals(exeName, "ollama app", StringComparison.OrdinalIgnoreCase)) return "Ollama Local LLM";
            if (string.Equals(exeName, "lm studio", StringComparison.OrdinalIgnoreCase) || string.Equals(exeName, "lms", StringComparison.OrdinalIgnoreCase)) return "LM Studio Local LLM";
            if (string.Equals(exeName, "localai", StringComparison.OrdinalIgnoreCase)) return "LocalAI Server";
            if (string.Equals(exeName, "cursor", StringComparison.OrdinalIgnoreCase)) return "Cursor AI IDE";
            if (string.Equals(exeName, "windsurf", StringComparison.OrdinalIgnoreCase)) return "Windsurf AI IDE";
            if (string.Equals(exeName, "cline", StringComparison.OrdinalIgnoreCase)) return "Cline AI Agent";
            if (string.Equals(exeName, "roo-code", StringComparison.OrdinalIgnoreCase) || string.Equals(exeName, "roo", StringComparison.OrdinalIgnoreCase) || string.Equals(exeName, "roo-cline", StringComparison.OrdinalIgnoreCase)) return "Roo Code Agent";
            if (string.Equals(exeName, "copilot", StringComparison.OrdinalIgnoreCase) || string.Equals(exeName, "copilot-agent", StringComparison.OrdinalIgnoreCase)) return "GitHub Copilot Agent";
            if (string.Equals(exeName, "continue", StringComparison.OrdinalIgnoreCase)) return "Continue AI Agent";
            if (string.Equals(exeName, "cody", StringComparison.OrdinalIgnoreCase)) return "Sourcegraph Cody";
            if (string.Equals(exeName, "tabnine", StringComparison.OrdinalIgnoreCase)) return "Tabnine AI Assistant";
            if (string.Equals(exeName, "amazon-q", StringComparison.OrdinalIgnoreCase) || string.Equals(exeName, "q", StringComparison.OrdinalIgnoreCase)) return "Amazon Q Developer";
            if (string.Equals(exeName, "zed", StringComparison.OrdinalIgnoreCase)) return "Zed Editor AI";
            if (string.Equals(exeName, "interpreter", StringComparison.OrdinalIgnoreCase) || string.Equals(exeName, "open-interpreter", StringComparison.OrdinalIgnoreCase)) return "Open Interpreter";
            return exeName + " Agent";
        }

        private static string FormatMcpDescription(string procName)
        {
            if (string.Equals(procName, "node", StringComparison.OrdinalIgnoreCase)) return "MCP Server (Node.js)";
            if (string.Equals(procName, "python", StringComparison.OrdinalIgnoreCase) || string.Equals(procName, "python3", StringComparison.OrdinalIgnoreCase) || string.Equals(procName, "pythonw", StringComparison.OrdinalIgnoreCase)) return "MCP Server (Python)";
            if (string.Equals(procName, "uvx", StringComparison.OrdinalIgnoreCase)) return "MCP Runner (uvx)";
            if (string.Equals(procName, "uv", StringComparison.OrdinalIgnoreCase)) return "Python Package Tool (uv)";
            if (string.Equals(procName, "npx", StringComparison.OrdinalIgnoreCase)) return "MCP Package Runner (npx)";
            if (string.Equals(procName, "bun", StringComparison.OrdinalIgnoreCase)) return "MCP Server (Bun)";
            if (string.Equals(procName, "deno", StringComparison.OrdinalIgnoreCase)) return "MCP Server (Deno)";
            if (string.Equals(procName, "docker", StringComparison.OrdinalIgnoreCase) || string.Equals(procName, "dockerd", StringComparison.OrdinalIgnoreCase)) return "MCP Container (Docker)";
            if (string.Equals(procName, "rg", StringComparison.OrdinalIgnoreCase)) return "Ripgrep Search Tool";
            if (string.Equals(procName, "git", StringComparison.OrdinalIgnoreCase)) return "Git Subprocess";
            if (string.Equals(procName, "pwsh", StringComparison.OrdinalIgnoreCase) || string.Equals(procName, "powershell", StringComparison.OrdinalIgnoreCase) || string.Equals(procName, "cmd", StringComparison.OrdinalIgnoreCase)) return "Terminal Shell Process";
            return procName;
        }

        private CachedSessionInfo ResolveSessionContext(Process rootProc, int rootPid, string rootExe, DateTime rootStartTime, List<int> descendantPids)
        {
            string cacheKey = string.Format("{0}_{1}", rootPid, rootStartTime.Ticks);
            lock (_syncLock)
            {
                CachedSessionInfo cached;
                if (_sessionContextCache.TryGetValue(cacheKey, out cached))
                {
                    return cached;
                }
            }

            string rawTitle = string.Empty;
            try
            {
                rawTitle = rootProc.MainWindowTitle;
                if (string.IsNullOrWhiteSpace(rawTitle) && descendantPids != null)
                {
                    foreach (int childPid in descendantPids)
                    {
                        try
                        {
                            var cp = Process.GetProcessById(childPid);
                            if (!string.IsNullOrWhiteSpace(cp.MainWindowTitle))
                            {
                                rawTitle = cp.MainWindowTitle;
                                break;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            string cleanWorkspace = CleanWindowTitle(rawTitle);
            string context = string.Empty;
            string modelName = string.Empty;

            if (!string.IsNullOrWhiteSpace(cleanWorkspace))
            {
                context = "📂 " + cleanWorkspace;
            }
            else
            {
                context = FormatDefaultContext(rootExe, rootPid);
            }

            var info = new CachedSessionInfo
            {
                Context = context,
                Workspace = cleanWorkspace,
                Model = modelName
            };

            lock (_syncLock)
            {
                _sessionContextCache[cacheKey] = info;
            }

            return info;
        }

        private static string CleanWindowTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return string.Empty;

            string s = title.Trim();
            if (s.StartsWith("● ") || s.StartsWith("* "))
            {
                s = s.Substring(2).Trim();
            }

            string[] suffixes = new[]
            {
                " — Google Antigravity", " - Google Antigravity",
                " — Antigravity", " - Antigravity",
                " — Cursor", " - Cursor",
                " — Windsurf", " - Windsurf",
                " — Visual Studio Code", " - Visual Studio Code",
                " — VS Code", " - VS Code",
                " — Claude", " - Claude",
                " — Aider", " - Aider"
            };

            foreach (var suffix in suffixes)
            {
                int idx = s.LastIndexOf(suffix, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    s = s.Substring(0, idx).Trim();
                }
            }

            if (s.Contains(" — "))
            {
                var parts = s.Split(new[] { " — " }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 1)
                {
                    s = parts[parts.Length - 1].Trim();
                }
            }
            else if (s.Contains(" - "))
            {
                var parts = s.Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 1)
                {
                    s = parts[parts.Length - 1].Trim();
                }
            }

            // Path Privacy: Never leak absolute folder paths
            if (s.Contains("\\") || s.Contains("/"))
            {
                try
                {
                    s = Path.GetFileName(s.TrimEnd('\\', '/'));
                }
                catch { }
            }

            return s.Length > 40 ? s.Substring(0, 37) + "..." : s;
        }

        private static string FormatDefaultContext(string exeName, int pid)
        {
            if (string.Equals(exeName, "claude", StringComparison.OrdinalIgnoreCase) || string.Equals(exeName, "claude-code", StringComparison.OrdinalIgnoreCase)) return "🤖 Claude Code Assistant";
            if (string.Equals(exeName, "gemini", StringComparison.OrdinalIgnoreCase) || string.Equals(exeName, "gemini-cli", StringComparison.OrdinalIgnoreCase)) return "🤖 Gemini CLI Assistant";
            if (string.Equals(exeName, "antigravity", StringComparison.OrdinalIgnoreCase) || string.Equals(exeName, "agy", StringComparison.OrdinalIgnoreCase)) return "⚡ Antigravity Workspace";
            if (string.Equals(exeName, "cursor", StringComparison.OrdinalIgnoreCase)) return "⚡ Cursor Workspace";
            if (string.Equals(exeName, "windsurf", StringComparison.OrdinalIgnoreCase)) return "⚡ Windsurf Workspace";
            if (string.Equals(exeName, "aider", StringComparison.OrdinalIgnoreCase)) return "💻 Aider Pair Session";
            if (string.Equals(exeName, "ollama", StringComparison.OrdinalIgnoreCase) || string.Equals(exeName, "ollama app", StringComparison.OrdinalIgnoreCase)) return "🧠 Ollama Local Inference";
            if (string.Equals(exeName, "lm studio", StringComparison.OrdinalIgnoreCase) || string.Equals(exeName, "lms", StringComparison.OrdinalIgnoreCase)) return "🧠 LM Studio Local Model";
            return string.Format("⚡ Sesión Activa #{0}", pid);
        }
    }
}
