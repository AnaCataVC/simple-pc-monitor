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
            "claude",
            "gemini",
            "codex",
            "aider",
            "ollama",
            "cursor",
            "open-interpreter",
            "interpreter"
        };

        private static readonly HashSet<string> KnownMcpRuntimes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "node",
            "python",
            "python3",
            "uvx",
            "uv",
            "npx",
            "rg",
            "git",
            "docker"
        };

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

                metric.Sessions.Add(session);
                grandTotalRamMB += session.TotalWorkingSetMB;
                totalMcpCount += session.McpServersCount;
            }

            metric.ActiveSessionsCount = metric.Sessions.Count;
            metric.TotalMcpServersCount = totalMcpCount;
            metric.TotalAggregatedRamMB = Math.Round(grandTotalRamMB, 1);
            metric.TotalAggregatedRamDisplay = string.Format("{0:N1} MB", grandTotalRamMB);

            // Cleanup stale CPU samples
            lock (_syncLock)
            {
                var deadPids = _prevCpuSamples.Keys.Where(k => !allRunningPids.Contains(k)).ToList();
                foreach (var dead in deadPids)
                {
                    _prevCpuSamples.Remove(dead);
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
            if (string.Equals(exeName, "claude", StringComparison.OrdinalIgnoreCase)) return "Claude Code CLI";
            if (string.Equals(exeName, "gemini", StringComparison.OrdinalIgnoreCase)) return "Gemini CLI";
            if (string.Equals(exeName, "codex", StringComparison.OrdinalIgnoreCase)) return "Codex CLI";
            if (string.Equals(exeName, "aider", StringComparison.OrdinalIgnoreCase)) return "Aider Agent";
            if (string.Equals(exeName, "ollama", StringComparison.OrdinalIgnoreCase)) return "Ollama Local LLM";
            if (string.Equals(exeName, "cursor", StringComparison.OrdinalIgnoreCase)) return "Cursor AI IDE";
            if (string.Equals(exeName, "interpreter", StringComparison.OrdinalIgnoreCase)) return "Open Interpreter";
            return exeName + " Agent";
        }

        private static string FormatMcpDescription(string procName)
        {
            if (string.Equals(procName, "node", StringComparison.OrdinalIgnoreCase)) return "MCP Server (Node.js)";
            if (string.Equals(procName, "python", StringComparison.OrdinalIgnoreCase) || string.Equals(procName, "python3", StringComparison.OrdinalIgnoreCase)) return "MCP Server (Python)";
            if (string.Equals(procName, "uvx", StringComparison.OrdinalIgnoreCase)) return "MCP Runner (uvx)";
            if (string.Equals(procName, "rg", StringComparison.OrdinalIgnoreCase)) return "Ripgrep Tool";
            if (string.Equals(procName, "git", StringComparison.OrdinalIgnoreCase)) return "Git Subprocess";
            return procName;
        }
    }
}
