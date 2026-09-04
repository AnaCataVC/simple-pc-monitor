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

        private class CachedChildProcessInfo
        {
            public string ProcessDisplayName { get; set; }
            public string SemanticRole { get; set; }
            public string RoleBadgeColor { get; set; }
            public string CommandLineSummary { get; set; }
            public string TooltipText { get; set; }
            public bool IsMcpServer { get; set; }
        }

        // An MCP server is identified by evidence in its command line, not by the runtime
        // that hosts it: compiled servers (Go, Rust) are absent from KnownMcpRuntimes, while
        // a Node process may simply be running a build or a language server.
        private static readonly string[] McpCommandLineMarkers =
        {
            "mcp-remote",
            "modelcontextprotocol",
            "mcp-server",
            "mcp_server",
            "--stdio",
            "/mcp",
            "\\mcp"
        };

        // npx and uvx keep their launcher process alive next to the server they spawned.
        // Both would otherwise be counted as two MCP servers for a single logical one.
        private static readonly string[] McpPackageRunnerMarkers =
        {
            "npx-cli.js",
            "\\npx\\",
            "uvx.exe"
        };

        // A shell carries the whole server command line as its arguments, so it matches every
        // MCP marker while being only the launcher. It is never the server itself.
        private static readonly HashSet<string> ShellProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "cmd",
            "pwsh",
            "powershell",
            "bash",
            "sh",
            "wsl",
            "conhost"
        };

        // A CLI agent session runs as a child of its desktop application but is a session in
        // its own right, not a helper process. Its command line carries the session flags.
        private static readonly string[] AgentSessionCommandLineMarkers =
        {
            "--output-format stream-json",
            "--resume=",
            "--session-id"
        };

        private static readonly HashSet<int> CollapsedSessionPids = new HashSet<int>();
        private static readonly object ExpandedLock = new object();

        public static void ToggleSessionExpanded(int pid)
        {
            lock (ExpandedLock)
            {
                if (CollapsedSessionPids.Contains(pid))
                    CollapsedSessionPids.Remove(pid);
                else
                    CollapsedSessionPids.Add(pid);
            }
        }

        public static bool IsSessionExpanded(int pid)
        {
            lock (ExpandedLock)
            {
                return !CollapsedSessionPids.Contains(pid);
            }
        }

        private readonly Dictionary<string, CachedSessionInfo> _sessionContextCache = new Dictionary<string, CachedSessionInfo>();
        private readonly Dictionary<string, CachedChildProcessInfo> _childProcessCache = new Dictionary<string, CachedChildProcessInfo>();
        private readonly Dictionary<int, bool> _independentSessionCache = new Dictionary<int, bool>();
        private readonly Dictionary<int, Tuple<TimeSpan, DateTime>> _prevCpuSamples = new Dictionary<int, Tuple<TimeSpan, DateTime>>();
        private readonly object _syncLock = new object();
        private readonly object _sampleGate = new object();
        private readonly int _processorCount = Environment.ProcessorCount > 0 ? Environment.ProcessorCount : 1;

        public AiAgentMetric Sample()
        {
            lock (_sampleGate)
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
                        if (KnownAgentSignatures.Contains(parentExe) && !IsIndependentAgentSession(pid))
                        {
                            continue; // This is a sub-agent process, not the root orchestrator
                        }
                    }

                    rootAgentPids.Add(pid);
                }
            }

            // Sessions promoted to their own root must not also be counted inside the tree of
            // the application that launched them, or their RAM and CPU would be added twice.
            var rootPidSet = new HashSet<int>(rootAgentPids);

            DateTime now = DateTime.UtcNow;
            double grandTotalRamMB = 0.0;
            int totalMcpCount = 0;
            int totalChildCount = 0;

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
                    if (rootProc != null) rootProc.Dispose();
                    continue; // Root process died between snapshot and inspection
                }

                using (rootProc)
                {
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
                    CollectDescendants(rootPid, parentToChildren, rootStartTime, descendants, rootPidSet);

                    double childrenRamMB = 0.0;
                    double childrenCpuPct = 0.0;

                    foreach (int childPid in descendants)
                    {
                        try
                        {
                            using (var childProc = Process.GetProcessById(childPid))
                            {
                                long childWs = TryGetWorkingSet(childProc);
                                double childMemMB = Math.Round((double)childWs / (1024.0 * 1024.0), 1);
                                double childCpu = CalculateCpuDelta(childPid, childProc, now);
                                DateTime childStart = TryGetStartTime(childProc);
                                string childProcName = childProc.ProcessName;

                                childrenRamMB += childMemMB;
                                childrenCpuPct += childCpu;

                                var meta = ResolveChildMetadata(childPid, childProcName, childStart);
                                session.ChildPids.Add(childPid);
                                session.ChildProcesses.Add(new AiAgentMcpServer
                                {
                                    Pid = childPid,
                                    ProcessName = meta.ProcessDisplayName,
                                    Description = meta.SemanticRole,
                                    SemanticRole = meta.SemanticRole,
                                    RoleBadgeColor = meta.RoleBadgeColor,
                                    CommandLineSummary = meta.CommandLineSummary,
                                    TooltipText = meta.TooltipText,
                                    WorkingSetMB = childMemMB,
                                    MemoryDisplay = string.Format("{0:N1} MB", childMemMB),
                                    CpuPercent = childCpu,
                                    CpuDisplay = string.Format("{0:N1}%", childCpu),
                                    StartTime = childStart,
                                    IsMcpServer = meta.IsMcpServer
                                });
                            }
                        }
                        catch { }
                    }

                    session.ChildrenWorkingSetMB = Math.Round(childrenRamMB, 1);
                    session.ChildrenCpuPercent = Math.Round(childrenCpuPct, 1);
                    session.TotalWorkingSetMB = Math.Round(rootRamMB + childrenRamMB, 1);
                    session.TotalMemoryDisplay = string.Format("{0:N1} MB", session.TotalWorkingSetMB);
                    session.TotalCpuPercent = Math.Round(rootCpuPct + childrenCpuPct, 1);
                    session.TotalCpuDisplay = string.Format("{0:N1}%", session.TotalCpuPercent);
                    session.ChildProcessCount = session.ChildProcesses.Count;
                    session.McpServersCount = session.ChildProcesses.Count(c => c.IsMcpServer);

                    // Expand/Collapse state
                    session.IsExpanded = IsSessionExpanded(rootPid);
                    session.ExpandToggleText = session.IsExpanded
                        ? string.Format("▲ Ocultar ({0})", session.ChildProcessCount)
                        : string.Format("▼ Ver {0} subprocesos", session.ChildProcessCount);

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
                    totalChildCount += session.ChildProcessCount;
                }
            }

            metric.ActiveSessionsCount = metric.Sessions.Count;
            metric.TotalMcpServersCount = totalMcpCount;
            metric.TotalChildProcessesCount = totalChildCount;
            metric.TotalAggregatedRamMB = Math.Round(grandTotalRamMB, 1);
            metric.TotalAggregatedRamDisplay = string.Format("{0:N1} MB", grandTotalRamMB);

            // Cleanup stale CPU samples, cached session contexts and child metadata (only when snapshot succeeded)
            if (allRunningPids.Count > 0)
            {
                lock (_syncLock)
                {
                    var deadPids = _prevCpuSamples.Keys.Where(k => !allRunningPids.Contains(k)).ToList();
                    foreach (var dead in deadPids)
                    {
                        _prevCpuSamples.Remove(dead);
                    }

                    var deadSessionPids = _independentSessionCache.Keys.Where(k => !allRunningPids.Contains(k)).ToList();
                    foreach (var dead in deadSessionPids)
                    {
                        _independentSessionCache.Remove(dead);
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

                    var deadChildCacheKeys = _childProcessCache.Keys.Where(k =>
                    {
                        int pid;
                        var parts = k.Split('_');
                        if (parts.Length > 0 && int.TryParse(parts[0], out pid))
                        {
                            return !allRunningPids.Contains(pid);
                        }
                        return true;
                    }).ToList();

                    foreach (var deadKey in deadChildCacheKeys)
                    {
                        _childProcessCache.Remove(deadKey);
                    }

                    lock (ExpandedLock)
                    {
                        CollapsedSessionPids.RemoveWhere(pid => !allRunningPids.Contains(pid));
                    }
                }
            }

            return metric;
        }
        }

        private void CollectDescendants(int parentPid, Dictionary<int, List<int>> tree, DateTime parentStartTime, List<int> result, HashSet<int> sessionBoundaries)
        {
            List<int> directChildren;
            if (tree.TryGetValue(parentPid, out directChildren))
            {
                foreach (int childPid in directChildren)
                {
                    if (sessionBoundaries != null && sessionBoundaries.Contains(childPid))
                    {
                        continue; // Owned by its own session row; counting it here would duplicate it
                    }

                    try
                    {
                        using (var childProc = Process.GetProcessById(childPid))
                        {
                            DateTime childStart = TryGetStartTime(childProc);

                            // Invariant: child cannot start before parent
                            if (parentStartTime != DateTime.MinValue && childStart != DateTime.MinValue && childStart < parentStartTime.AddSeconds(-2))
                            {
                                continue; // PID reuse collision detected and rejected
                            }

                            if (!result.Contains(childPid))
                            {
                                result.Add(childPid);
                                CollectDescendants(childPid, tree, childStart != DateTime.MinValue ? childStart : parentStartTime, result, sessionBoundaries);
                            }
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
            if (string.Equals(procName, "claude", StringComparison.OrdinalIgnoreCase)) return "Worker Subprocess";
            if (string.Equals(procName, "antigravity", StringComparison.OrdinalIgnoreCase)) return "Worker Subprocess";
            if (string.Equals(procName, "cursor", StringComparison.OrdinalIgnoreCase)) return "Helper Process";
            if (string.Equals(procName, "windsurf", StringComparison.OrdinalIgnoreCase)) return "Cascade Worker";
            if (string.Equals(procName, "conhost", StringComparison.OrdinalIgnoreCase)) return "Console Host";
            if (string.Equals(procName, "bash", StringComparison.OrdinalIgnoreCase)) return "Bash Shell";
            if (string.Equals(procName, "sh", StringComparison.OrdinalIgnoreCase)) return "Shell Process";
            if (string.Equals(procName, "wsl", StringComparison.OrdinalIgnoreCase)) return "WSL Subprocess";
            // Reached only when the command line carried no MCP evidence, so these runtimes
            // are hosting something else: a build, a language server, a plain script.
            if (string.Equals(procName, "node", StringComparison.OrdinalIgnoreCase)) return "Node.js Process";
            if (string.Equals(procName, "python", StringComparison.OrdinalIgnoreCase) || string.Equals(procName, "python3", StringComparison.OrdinalIgnoreCase) || string.Equals(procName, "pythonw", StringComparison.OrdinalIgnoreCase)) return "Python Process";
            if (string.Equals(procName, "uvx", StringComparison.OrdinalIgnoreCase)) return "Package Runner (uvx)";
            if (string.Equals(procName, "uv", StringComparison.OrdinalIgnoreCase)) return "Python Package Tool (uv)";
            if (string.Equals(procName, "npx", StringComparison.OrdinalIgnoreCase)) return "Package Runner (npx)";
            if (string.Equals(procName, "bun", StringComparison.OrdinalIgnoreCase)) return "Bun Process";
            if (string.Equals(procName, "deno", StringComparison.OrdinalIgnoreCase)) return "Deno Process";
            if (string.Equals(procName, "docker", StringComparison.OrdinalIgnoreCase) || string.Equals(procName, "dockerd", StringComparison.OrdinalIgnoreCase)) return "MCP Container (Docker)";
            if (string.Equals(procName, "rg", StringComparison.OrdinalIgnoreCase)) return "Ripgrep Search Tool";
            if (string.Equals(procName, "git", StringComparison.OrdinalIgnoreCase)) return "Git Subprocess";
            if (string.Equals(procName, "pwsh", StringComparison.OrdinalIgnoreCase) || string.Equals(procName, "powershell", StringComparison.OrdinalIgnoreCase) || string.Equals(procName, "cmd", StringComparison.OrdinalIgnoreCase)) return "Terminal Shell Process";
            return procName;
        }

        private CachedChildProcessInfo ResolveChildMetadata(int pid, string procName, DateTime startTime)
        {
            string cacheKey = string.Format("{0}_{1}", pid, startTime.Ticks);
            lock (_syncLock)
            {
                CachedChildProcessInfo cached;
                if (_childProcessCache.TryGetValue(cacheKey, out cached))
                {
                    return cached;
                }
            }

            string rawCmd = ProcessManager.GetProcessCommandLine(pid);
            string sanitizedCmd = ProcessManager.SanitizeCommandLine(rawCmd);

            string displayName = procName;
            string role = "Subproceso";
            string color = "#64748B"; // Slate default
            bool isMcpServer = false;

            string lowerCmd = (sanitizedCmd ?? string.Empty).ToLowerInvariant();
            string lowerName = (procName ?? string.Empty).ToLowerInvariant();

            // 1. Chromium / Electron Framework Subprocesses
            if (lowerCmd.Contains("--type=renderer"))
            {
                displayName = procName;
                role = "Renderizador UI / Web";
                color = "#38BDF8"; // Sky Blue
            }
            else if (lowerCmd.Contains("--type=gpu-process"))
            {
                displayName = procName;
                role = "Aceleración GPU";
                color = "#A855F7"; // Purple
            }
            else if (lowerCmd.Contains("--type=crashpad-handler"))
            {
                displayName = "Crashpad";
                role = "Monitor de Diagnóstico";
                color = "#F43F5E"; // Rose
            }
            else if (lowerCmd.Contains("--type=utility"))
            {
                if (lowerCmd.Contains("network.mojom.networkservice"))
                {
                    displayName = procName;
                    role = "Servicio de Red (HTTP/WS)";
                    color = "#10B981"; // Emerald
                }
                else if (lowerCmd.Contains("audio.mojom.audioservice"))
                {
                    displayName = procName;
                    role = "Servicio de Audio";
                    color = "#F59E0B"; // Amber
                }
                else if (lowerCmd.Contains("video_capture.mojom"))
                {
                    displayName = procName;
                    role = "Captura de Video / Pantalla";
                    color = "#EC4899"; // Pink
                }
                else
                {
                    displayName = procName;
                    role = "Servicio de Utilidad";
                    color = "#64748B";
                }
            }
            // 2. Language Servers & Background Cron / Agents
            else if (lowerName.Contains("language_server"))
            {
                if (lowerCmd.Contains("schedule") || lowerCmd.Contains("cron"))
                {
                    displayName = "Cron Planner";
                    role = "Tarea Programada (Cron)";
                    color = "#6366F1"; // Indigo
                }
                else if (lowerCmd.Contains("agentapi") || lowerCmd.Contains("new-conversation"))
                {
                    displayName = "Subagente Worker";
                    role = "Agente Autónomo";
                    color = "#8B5CF6"; // Violet
                }
                else if (lowerCmd.Contains("lsp"))
                {
                    displayName = "LSP Server";
                    role = "Servidor de Lenguaje";
                    color = "#06B6D4"; // Cyan
                }
                else
                {
                    displayName = "Language Server";
                    role = "Servidor de Lenguaje / IA";
                    color = "#06B6D4";
                }
            }
            // 3. Windows Console Host
            else if (lowerName.Equals("conhost"))
            {
                displayName = "conhost";
                role = "Host de Consola Windows";
                color = "#64748B";
            }
            // 4. MCP package runners: they stay alive next to the server they launched, so
            // they are labelled apart to keep one logical MCP from being counted twice.
            else if (IsMcpPackageRunner(lowerCmd))
            {
                displayName = procName;
                role = "Lanzador de paquete MCP";
                color = "#64748B";
            }
            // 5. Model Context Protocol (MCP) servers, recognised by command line evidence
            else if (LooksLikeMcpServer(lowerName, lowerCmd))
            {
                string mcpName = ExtractMcpServerName(sanitizedCmd, lowerName);
                displayName = !string.IsNullOrEmpty(mcpName) ? mcpName : procName;
                role = "Servidor MCP (" + procName + ")";
                color = "#10B981"; // Emerald
                isMcpServer = true;
            }
            // 6. Developer & Shell Utilities
            else if (lowerName.Equals("rg"))
            {
                displayName = "ripgrep";
                role = "Búsqueda de Código";
                color = "#E11D48";
            }
            else if (lowerName.Equals("git"))
            {
                displayName = "git";
                role = "Control de Versiones";
                color = "#F97316";
            }
            else if (lowerName.Equals("pwsh") || lowerName.Equals("powershell") || lowerName.Equals("cmd") || lowerName.Equals("bash"))
            {
                displayName = procName;
                role = "Terminal Shell";
                color = "#64748B";
            }
            else
            {
                displayName = procName;
                role = FormatMcpDescription(procName);
                color = "#38BDF8";
            }

            // Build safe truncated tooltip
            string tooltip = string.Format("PID: {0} • {1} ({2})", pid, displayName, role);
            if (!string.IsNullOrWhiteSpace(sanitizedCmd))
            {
                string truncatedCmd = sanitizedCmd.Length > 220 ? sanitizedCmd.Substring(0, 217) + "..." : sanitizedCmd;
                tooltip += "\n" + truncatedCmd;
            }

            var info = new CachedChildProcessInfo
            {
                ProcessDisplayName = displayName,
                SemanticRole = role,
                RoleBadgeColor = color,
                CommandLineSummary = sanitizedCmd,
                TooltipText = tooltip,
                IsMcpServer = isMcpServer
            };

            lock (_syncLock)
            {
                _childProcessCache[cacheKey] = info;
            }

            return info;
        }

        private static bool IsMcpPackageRunner(string lowerCmd)
        {
            if (string.IsNullOrEmpty(lowerCmd)) return false;

            foreach (string marker in McpPackageRunnerMarkers)
            {
                if (lowerCmd.Contains(marker)) return true;
            }

            return false;
        }

        private static bool LooksLikeMcpServer(string procName, string lowerCmd)
        {
            if (string.IsNullOrEmpty(lowerCmd)) return false;
            if (ShellProcessNames.Contains(procName ?? string.Empty)) return false;

            foreach (string marker in McpCommandLineMarkers)
            {
                if (lowerCmd.Contains(marker)) return true;
            }

            return false;
        }

        /// <summary>
        /// True when a process named like a known agent is a session of its own rather than a
        /// helper of the agent that spawned it. Cached per PID and cleared when the PID dies.
        /// </summary>
        private bool IsIndependentAgentSession(int pid)
        {
            lock (_syncLock)
            {
                bool cached;
                if (_independentSessionCache.TryGetValue(pid, out cached))
                {
                    return cached;
                }
            }

            string cmd = ProcessManager.GetProcessCommandLine(pid);
            if (cmd == null)
            {
                // Process is starting up (PEB uninitialized) or inaccessible.
                // Do not cache negative result yet so subsequent cycles can re-evaluate.
                return false;
            }

            bool isSession = false;
            string lowerCmd = cmd.ToLowerInvariant();
            foreach (string marker in AgentSessionCommandLineMarkers)
            {
                if (lowerCmd.Contains(marker))
                {
                    isSession = true;
                    break;
                }
            }

            lock (_syncLock)
            {
                _independentSessionCache[pid] = isSession;
            }

            return isSession;
        }

        private static string ExtractMcpServerName(string cmd, string runtime)
        {
            if (string.IsNullOrWhiteSpace(cmd)) return null;

            try
            {
                var match = System.Text.RegularExpressions.Regex.Match(cmd, @"(?i)(?:mcp[-_]server[-_][a-zA-Z0-9_\-]+|@?[a-zA-Z0-9_-]+/server[-_][a-zA-Z0-9_\-]+)");
                if (match.Success)
                {
                    return match.Value;
                }

                if (runtime.StartsWith("python"))
                {
                    var pyMatch = System.Text.RegularExpressions.Regex.Match(cmd, @"(?i)([a-zA-Z0-9_\-]+\.py)");
                    if (pyMatch.Success) return pyMatch.Value;
                }

                var jsMatch = System.Text.RegularExpressions.Regex.Match(cmd, @"(?i)([a-zA-Z0-9_\-]+)[\\/](?:index|server|main)\.js");
                if (jsMatch.Success) return jsMatch.Groups[1].Value;
            }
            catch { }

            return null;
        }

        private static string ExtractCommandLineFlag(string cmd, string pattern)
        {
            if (string.IsNullOrWhiteSpace(cmd)) return null;

            try
            {
                var match = System.Text.RegularExpressions.Regex.Match(cmd, pattern);
                if (match.Success) return match.Groups[1].Value;
            }
            catch { }

            return null;
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
                            using (var cp = Process.GetProcessById(childPid))
                            {
                                if (!string.IsNullOrWhiteSpace(cp.MainWindowTitle))
                                {
                                    rawTitle = cp.MainWindowTitle;
                                    break;
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            string rootCmd = ProcessManager.SanitizeCommandLine(ProcessManager.GetProcessCommandLine(rootPid));
            string modelName = ExtractCommandLineFlag(rootCmd, @"--model[\s=]+([a-zA-Z0-9_\-\.]+)");
            string resumeId = ExtractCommandLineFlag(rootCmd, @"--resume[=\s]+([a-fA-F0-9\-]{8,})");

            string cleanWorkspace = CleanWindowTitle(rawTitle);
            string context = string.Empty;

            if (!string.IsNullOrWhiteSpace(cleanWorkspace))
            {
                context = "📂 " + cleanWorkspace;
            }
            else if (!string.IsNullOrWhiteSpace(resumeId))
            {
                context = "🔗 Sesión " + resumeId.Substring(Math.Max(0, resumeId.Length - 8));
            }
            else
            {
                context = FormatDefaultContext(rootExe, rootPid);
            }

            var info = new CachedSessionInfo
            {
                Context = context,
                Workspace = cleanWorkspace,
                Model = modelName ?? string.Empty
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
