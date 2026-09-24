using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SystemCoreMonitor.Core
{
    /// <summary>
    /// Pure heuristics that flag runaway processes: disk-wide scans and sustained CPU load.
    /// Detection only reports; terminating anything is always a user decision (AGENTS rule 13).
    /// </summary>
    public static class RunawayProcessDetector
    {
        public const double CpuThresholdPercent = 25.0;
        public static readonly TimeSpan MinSustained = TimeSpan.FromMinutes(5);

        private static readonly HashSet<string> DirectScanners = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "find", "rg", "fd", "findstr", "where", "robocopy"
        };

        private static readonly HashSet<string> ShellScanners = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "cmd", "powershell", "pwsh"
        };

        private static readonly Regex DriveRoot = new Regex(@"^[A-Za-z]:[\x5C/]?$", RegexOptions.Compiled);
        private static readonly Regex ArgumentToken = new Regex(@"""([^""]*)""|(\S+)", RegexOptions.Compiled);
        private static readonly char[] PathSeparators = { System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar };

        /// <summary>Cheap exe-name gate so the command-line syscall only runs for plausible scanners.</summary>
        public static bool IsScanCandidate(string exeName)
        {
            string name = NormalizeExeName(exeName);
            return DirectScanners.Contains(name) || ShellScanners.Contains(name);
        }

        /// <summary>
        /// Returns the disk-wide target the command line scans (a drive root or the whole user profile),
        /// or null when the process is not a recognized scanner or its target is scoped.
        /// </summary>
        public static string? ClassifyScan(string exeName, string commandLine)
        {
            if (string.IsNullOrWhiteSpace(commandLine)) return null;
            string name = NormalizeExeName(exeName);
            if (!IsScanCandidate(name)) return null;

            if (string.Equals(name, "cmd", StringComparison.OrdinalIgnoreCase) &&
                commandLine.IndexOf("/s", StringComparison.OrdinalIgnoreCase) < 0) return null;
            if ((string.Equals(name, "powershell", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "pwsh", StringComparison.OrdinalIgnoreCase)) &&
                commandLine.IndexOf("-Recurse", StringComparison.OrdinalIgnoreCase) < 0) return null;

            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            bool first = true;
            foreach (Match m in ArgumentToken.Matches(commandLine))
            {
                // The first token is the executable itself, never a scan target.
                if (first) { first = false; continue; }
                string arg = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                if (IsDiskWideTarget(arg, userProfile)) return arg;
            }
            return null;
        }

        private static bool IsDiskWideTarget(string arg, string userProfile)
        {
            if (arg == "/" || arg == "\\" || DriveRoot.IsMatch(arg)) return true;
            if (string.Equals(arg, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "~", StringComparison.Ordinal)) return true;
            return !string.IsNullOrEmpty(userProfile) &&
                   string.Equals(arg.TrimEnd(PathSeparators), userProfile.TrimEnd(PathSeparators), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeExeName(string exeName)
        {
            if (string.IsNullOrEmpty(exeName)) return string.Empty;
            string name = System.IO.Path.GetFileName(exeName);
            return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - 4) : name;
        }
    }

    /// <summary>
    /// Tracks how long each process identity (PID, StartTime) has stayed above the CPU threshold.
    /// </summary>
    public class SustainedLoadTracker
    {
        private readonly Dictionary<(int Pid, DateTime StartTime), DateTime> _loadSince = new Dictionary<(int, DateTime), DateTime>();
        private readonly object _lock = new object();

        /// <summary>
        /// Records one sample (a sample below the threshold closes the window, whatever its startTime)
        /// and returns how long the load has been sustained once it reaches
        /// <see cref="RunawayProcessDetector.MinSustained"/>, otherwise null.
        /// </summary>
        public TimeSpan? Observe(int pid, DateTime startTime, double cpuPercent, DateTime now)
        {
            lock (_lock)
            {
                if (cpuPercent < RunawayProcessDetector.CpuThresholdPercent)
                {
                    RemovePidLocked(pid);
                    return null;
                }

                var key = (pid, startTime);
                if (!_loadSince.TryGetValue(key, out DateTime since))
                {
                    // A recycled PID gets a fresh window: drop any entry left by the previous owner.
                    RemovePidLocked(pid);
                    _loadSince[key] = now;
                    return null;
                }

                TimeSpan sustained = now - since;
                return sustained >= RunawayProcessDetector.MinSustained ? sustained : (TimeSpan?)null;
            }
        }

        /// <summary>Evicts dead PIDs; an empty snapshot is treated as a transient failure (AGENTS rule 7).</summary>
        public void RemoveDeadPids(ICollection<int> allRunningPids)
        {
            if (allRunningPids == null || allRunningPids.Count == 0) return;
            lock (_lock)
            {
                var dead = new List<(int, DateTime)>();
                foreach (var key in _loadSince.Keys)
                {
                    if (!allRunningPids.Contains(key.Pid)) dead.Add(key);
                }
                foreach (var key in dead) _loadSince.Remove(key);
            }
        }

        private void RemovePidLocked(int pid)
        {
            var stale = new List<(int, DateTime)>();
            foreach (var key in _loadSince.Keys)
            {
                if (key.Pid == pid) stale.Add(key);
            }
            foreach (var key in stale) _loadSince.Remove(key);
        }
    }
}
