using System;
using System.Collections.Generic;
using SystemCoreMonitor.Core.Mvvm;

namespace SystemCoreMonitor.Models
{
    public class AiAgentMcpServer
    {
        public int Pid { get; set; }
        public string ProcessName { get; set; }
        public string SemanticRole { get; set; }
        public string RoleBadgeColor { get; set; }
        public string TooltipText { get; set; }
        public double WorkingSetMB { get; set; }
        public string MemoryDisplay { get; set; }
        public double CpuPercent { get; set; }
        public string CpuDisplay { get; set; }
        public DateTime StartTime { get; set; }
        public bool IsMcpServer { get; set; }
        public int ParentPid { get; set; }
        public string OrphanReason { get; set; }
        public string AgeDisplay { get; set; }
        public string CommandLine { get; set; }

        // Identity of the dead session an orphan row belongs to; RootPid 0 means unknown origin.
        public int OrphanSessionRootPid { get; set; }
        public DateTime OrphanSessionStartTime { get; set; }
        public string OrphanSessionAgent { get; set; } = string.Empty;
        public DateTime? OrphanSessionEndedAt { get; set; }

        public AiAgentMcpServer()
        {
            ProcessName = string.Empty;
            SemanticRole = "Subproceso";
            RoleBadgeColor = "#38BDF8";
            TooltipText = string.Empty;
            MemoryDisplay = "0.0 MB";
            CpuDisplay = "0.0%";
            OrphanReason = string.Empty;
            AgeDisplay = string.Empty;
            CommandLine = string.Empty;
        }
    }

    public class AiAgentSession : ObservableObject
    {
        private bool _isExpanded;
        private string _expandToggleText = "▼ Ver subprocesos";

        public int ParentPid { get; set; }
        public string AgentName { get; set; }
        public string AgentProcessName { get; set; }
        public string CommandLine { get; set; }
        public DateTime StartTime { get; set; }
        public string StartTimeDisplay { get; set; }
        public double ParentWorkingSetMB { get; set; }
        public double ChildrenWorkingSetMB { get; set; }
        public double TotalWorkingSetMB { get; set; }
        public string TotalMemoryDisplay { get; set; }
        public double ParentCpuPercent { get; set; }
        public double ChildrenCpuPercent { get; set; }
        public double TotalCpuPercent { get; set; }
        public string TotalCpuDisplay { get; set; }
        public int McpServersCount { get; set; }
        public int ChildProcessCount { get; set; }
        public bool HasChildProcesses => ChildProcessCount > 0;
        public List<int> ChildPids { get; set; }
        public List<AiAgentMcpServer> ChildProcesses { get; set; }
        public bool IsIdle { get; set; }

        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        public string ExpandToggleText
        {
            get => _expandToggleText;
            set => SetProperty(ref _expandToggleText, value);
        }

        public string StatusDisplay { get; set; }
        public string StatusBadgeColor { get; set; }
        public string SessionContext { get; set; }
        public string ModelName { get; set; }

        public AiAgentSession()
        {
            AgentName = string.Empty;
            AgentProcessName = string.Empty;
            CommandLine = string.Empty;
            StartTimeDisplay = string.Empty;
            TotalMemoryDisplay = "0.0 MB";
            TotalCpuDisplay = "0.0%";
            ChildPids = new List<int>();
            ChildProcesses = new List<AiAgentMcpServer>();
            IsExpanded = false;
            ExpandToggleText = "▼ Ver subprocesos";
            StatusDisplay = "Active";
            StatusBadgeColor = "#10B981"; // Emerald
            SessionContext = string.Empty;
            ModelName = string.Empty;
        }
    }

    /// <summary>
    /// Orphans left behind by one ended agent session, or the "unknown origin" bucket.
    /// </summary>
    public class AiAgentOrphanGroup : ObservableObject
    {
        private bool _isExpanded;

        public int RootPid { get; set; }
        public DateTime RootStartTime { get; set; }
        public bool IsUnknownOrigin { get; set; }
        public string AgentName { get; set; } = string.Empty;
        public DateTime? EndedAt { get; set; }
        public string EndedDisplay { get; set; } = string.Empty;
        public double TotalRamMB { get; set; }
        public string TotalRamDisplay { get; set; } = "0.0 MB";
        public int ProcessCount { get; set; }
        public List<AiAgentMcpServer> Processes { get; set; } = new List<AiAgentMcpServer>();

        public string Header => IsUnknownOrigin
            ? string.Format("{0} • {1} proceso(s) • {2}", AgentName, ProcessCount, TotalRamDisplay)
            : string.Format("{0} (PID {1}) • terminó hace {2} • {3} proceso(s) • {4}", AgentName, RootPid, EndedDisplay, ProcessCount, TotalRamDisplay);

        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }
    }

    public class AiAgentMetric
    {
        public int ActiveSessionsCount { get; set; }
        public int TotalMcpServersCount { get; set; }
        public double TotalAggregatedRamMB { get; set; }
        public string TotalAggregatedRamDisplay { get; set; }
        public List<AiAgentSession> Sessions { get; set; }

        /// <summary>
        /// Processes an ended agent session left behind (see AgentLineageTracker). Reported for
        /// the user to judge, never acted on automatically.
        /// </summary>
        public List<AiAgentMcpServer> OrphanProcesses { get; set; }
        public List<AiAgentOrphanGroup> OrphanGroups { get; set; } = new List<AiAgentOrphanGroup>();
        public int OrphanCount { get; set; }
        public double OrphanRamMB { get; set; }
        public string OrphanRamDisplay { get; set; }

        public AiAgentMetric()
        {
            Sessions = new List<AiAgentSession>();
            OrphanProcesses = new List<AiAgentMcpServer>();
            TotalAggregatedRamDisplay = "0.0 MB";
            OrphanRamDisplay = "0.0 MB";
        }
    }
}
