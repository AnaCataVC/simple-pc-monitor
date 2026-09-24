using System;
using System.Collections.Generic;
using System.Linq;
using SystemCoreMonitor.Models;

namespace SystemCoreMonitor.Core
{
    public enum OrphanKind
    {
        None,

        /// <summary>The process was seen inside a live agent session that has since ended.</summary>
        Lineage,

        /// <summary>Never seen inside a session, but a runtime whose dead parent was an agent process.</summary>
        Fallback
    }

    /// <summary>
    /// A session the tracker has seen alive, identified by its root (PID, StartTime).
    /// </summary>
    public sealed class AgentSessionRecord
    {
        public int RootPid { get; set; }
        public DateTime RootStartTime { get; set; }
        public string AgentName { get; set; } = string.Empty;
        public string SessionContext { get; set; } = string.Empty;
        public DateTime? EndedAt { get; set; }
        internal long LastSeenTick { get; set; }
    }

    /// <summary>
    /// Remembers which processes belonged to which agent session, keyed by (PID, StartTime) so a
    /// recycled PID never inherits a lineage (AGENTS rule 12). When a session ends, whatever it
    /// spawned and is still alive is an orphan regardless of its executable name. The tracker
    /// holds no process handles: callers feed it samples and it answers from memory, which keeps
    /// every decision unit-testable.
    /// </summary>
    public sealed class AgentLineageTracker
    {
        // Children of a killed agent often need a few seconds to notice and exit on their own.
        public static readonly TimeSpan SessionEndGrace = TimeSpan.FromSeconds(10);

        // Tolerance for StartTime comparisons: the same process sampled twice can differ slightly.
        private static readonly TimeSpan StartTimeTolerance = TimeSpan.FromSeconds(2);

        // A dead descendant stays remembered for a while: it is the evidence that a runtime it
        // spawned between two samples belongs to an agent (see FindSessionOfParent).
        public static readonly TimeSpan DeadLineageRetention = TimeSpan.FromHours(1);

        private sealed class DescendantEntry
        {
            public (int Pid, DateTime Start) Session;
            public DateTime? DiedAt;
        }

        private readonly Dictionary<(int Pid, DateTime Start), DescendantEntry> _descendantToSession = new();
        private readonly Dictionary<(int Pid, DateTime Start), AgentSessionRecord> _sessions = new();
        private readonly object _gate = new object();
        private long _tick;

        public void BeginTick()
        {
            lock (_gate) { _tick++; }
        }

        public void RecordLiveSession(int rootPid, DateTime rootStart, string agentName, string sessionContext, IEnumerable<(int Pid, DateTime Start)> descendants)
        {
            if (rootStart == DateTime.MinValue) return; // Identity unknown: remembering it would be a guess

            lock (_gate)
            {
                var key = (rootPid, rootStart);
                if (!_sessions.TryGetValue(key, out var record))
                {
                    record = new AgentSessionRecord { RootPid = rootPid, RootStartTime = rootStart };
                    _sessions[key] = record;
                }
                record.AgentName = agentName ?? string.Empty;
                record.SessionContext = sessionContext ?? string.Empty;
                record.EndedAt = null;
                record.LastSeenTick = _tick;

                foreach (var d in descendants)
                {
                    if (d.Start == DateTime.MinValue) continue;
                    _descendantToSession[(d.Pid, d.Start)] = new DescendantEntry { Session = key };
                }
            }
        }

        /// <summary>
        /// Closes sessions whose root is gone and evicts memory about processes dead for longer
        /// than <see cref="DeadLineageRetention"/>. Does nothing on an empty snapshot: a failed Toolhelp32 call proves nothing
        /// (AGENTS rule 7).
        /// </summary>
        public void EndTick(HashSet<int> allRunningPids, DateTime now)
        {
            if (allRunningPids == null || allRunningPids.Count == 0) return;

            lock (_gate)
            {
                foreach (var record in _sessions.Values)
                {
                    if (record.EndedAt == null && record.LastSeenTick != _tick && !allRunningPids.Contains(record.RootPid))
                    {
                        record.EndedAt = now;
                    }
                }

                foreach (var kv in _descendantToSession)
                {
                    if (kv.Value.DiedAt == null && !allRunningPids.Contains(kv.Key.Pid)) kv.Value.DiedAt = now;
                }
                var expired = _descendantToSession
                    .Where(kv => kv.Value.DiedAt.HasValue && now - kv.Value.DiedAt.Value >= DeadLineageRetention)
                    .Select(kv => kv.Key)
                    .ToList();
                foreach (var k in expired) _descendantToSession.Remove(k);

                var referenced = new HashSet<(int, DateTime)>(_descendantToSession.Values.Select(v => v.Session));
                var goneSessions = _sessions
                    .Where(kv => kv.Value.EndedAt != null && !referenced.Contains(kv.Key))
                    .Select(kv => kv.Key)
                    .ToList();
                foreach (var k in goneSessions) _sessions.Remove(k);
            }
        }

        /// <summary>Cheap pre-filter so the collector only opens handles for PIDs it remembers.</summary>
        public bool KnowsPid(int pid)
        {
            lock (_gate)
            {
                return _descendantToSession.Keys.Any(k => k.Pid == pid) || _sessions.Keys.Any(k => k.Pid == pid);
            }
        }

        /// <summary>The session this exact process was seen in, or null.</summary>
        public AgentSessionRecord? FindSessionOf(int pid, DateTime startTime)
        {
            if (startTime == DateTime.MinValue) return null;
            lock (_gate)
            {
                foreach (var kv in _descendantToSession)
                {
                    if (kv.Key.Pid == pid && SameStart(kv.Key.Start, startTime) && _sessions.TryGetValue(kv.Value.Session, out var record))
                    {
                        return record;
                    }
                }
                return null;
            }
        }

        /// <summary>
        /// The session a parent PID belonged to when it spawned a child that started at
        /// <paramref name="childStart"/>. A remembered process that started after the child
        /// cannot be its parent: that number was recycled.
        /// </summary>
        public AgentSessionRecord? FindSessionOfParent(int parentPid, DateTime childStart)
        {
            if (childStart == DateTime.MinValue) return null;
            lock (_gate)
            {
                foreach (var kv in _sessions)
                {
                    if (kv.Key.Pid == parentPid && kv.Key.Start <= childStart + StartTimeTolerance) return kv.Value;
                }
                foreach (var kv in _descendantToSession)
                {
                    if (kv.Key.Pid == parentPid && kv.Key.Start <= childStart + StartTimeTolerance && _sessions.TryGetValue(kv.Value.Session, out var record))
                    {
                        return record;
                    }
                }
                return null;
            }
        }

        /// <summary>
        /// Pure orphan decision. A process with lineage is an orphan once its session ended
        /// (after a short grace). Without lineage, only a runtime whose dead parent belonged to an
        /// ended agent session qualifies: a runtime launched from a terminal that has since closed is
        /// normal and is not reported.
        /// </summary>
        public static OrphanKind Decide(
            AgentSessionRecord? lineageSession,
            bool isKnownRuntime,
            bool parentDeadOrRecycled,
            AgentSessionRecord? parentSession,
            DateTime now)
        {
            if (lineageSession != null)
            {
                return HasEnded(lineageSession, now) ? OrphanKind.Lineage : OrphanKind.None;
            }

            // A runtime detached from a still-running session (a dev server left behind by a
            // shell that exited) is presumably still wanted by that session's user.
            if (isKnownRuntime && parentDeadOrRecycled && parentSession != null && HasEnded(parentSession, now))
            {
                return OrphanKind.Fallback;
            }

            return OrphanKind.None;
        }

        private static bool HasEnded(AgentSessionRecord session, DateTime now) =>
            session.EndedAt.HasValue && now - session.EndedAt.Value >= SessionEndGrace;

        /// <summary>
        /// Groups orphan rows by the dead session that left them behind. Rows without a
        /// resolvable session land in a single "unknown origin" group, listed last.
        /// </summary>
        public static List<AiAgentOrphanGroup> BuildGroups(IEnumerable<AiAgentMcpServer> orphans, DateTime now)
        {
            var groups = new List<AiAgentOrphanGroup>();
            foreach (var bucket in orphans.GroupBy(o => (o.OrphanSessionRootPid, o.OrphanSessionStartTime)))
            {
                var rows = bucket.OrderByDescending(o => o.WorkingSetMB).ToList();
                var first = rows[0];
                bool unknown = bucket.Key.OrphanSessionRootPid <= 0;
                double ram = Math.Round(rows.Sum(o => o.WorkingSetMB), 1);
                groups.Add(new AiAgentOrphanGroup
                {
                    RootPid = bucket.Key.OrphanSessionRootPid,
                    RootStartTime = bucket.Key.OrphanSessionStartTime,
                    IsUnknownOrigin = unknown,
                    AgentName = unknown ? "Origen desconocido" : first.OrphanSessionAgent,
                    EndedAt = first.OrphanSessionEndedAt,
                    EndedDisplay = first.OrphanSessionEndedAt.HasValue
                        ? MetricFormatting.FormatAge(now - first.OrphanSessionEndedAt.Value)
                        : string.Empty,
                    TotalRamMB = ram,
                    TotalRamDisplay = string.Format("{0:N1} MB", ram),
                    ProcessCount = rows.Count,
                    Processes = rows
                });
            }

            return groups
                .OrderBy(g => g.IsUnknownOrigin)
                .ThenByDescending(g => g.TotalRamMB)
                .ToList();
        }

        private static bool SameStart(DateTime a, DateTime b) => (a - b).Duration() <= StartTimeTolerance;
    }
}
