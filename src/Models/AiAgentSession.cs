using System;
using System.Collections.Generic;

namespace SimplePCMonitor.Models
{
    public class AiAgentMcpServer
    {
        public int Pid { get; set; }
        public string ProcessName { get; set; }
        public string Description { get; set; }
        public double WorkingSetMB { get; set; }
        public string MemoryDisplay { get; set; }
        public double CpuPercent { get; set; }
        public string CpuDisplay { get; set; }
        public DateTime StartTime { get; set; }

        public AiAgentMcpServer()
        {
            ProcessName = string.Empty;
            Description = string.Empty;
            MemoryDisplay = "0.0 MB";
            CpuDisplay = "0.0%";
        }
    }

    public class AiAgentSession
    {
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
        public List<int> ChildPids { get; set; }
        public List<AiAgentMcpServer> ChildProcesses { get; set; }
        public bool IsIdle { get; set; }
        public bool IsOrphaned { get; set; }
        public string StatusDisplay { get; set; }
        public string StatusBadgeColor { get; set; }

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
            StatusDisplay = "Active";
            StatusBadgeColor = "#10B981"; // Emerald
        }
    }

    public class AiAgentMetric
    {
        public int ActiveSessionsCount { get; set; }
        public int TotalMcpServersCount { get; set; }
        public double TotalAggregatedRamMB { get; set; }
        public string TotalAggregatedRamDisplay { get; set; }
        public List<AiAgentSession> Sessions { get; set; }
        public List<AiAgentMcpServer> OrphanedMcpServers { get; set; }

        public AiAgentMetric()
        {
            Sessions = new List<AiAgentSession>();
            OrphanedMcpServers = new List<AiAgentMcpServer>();
            TotalAggregatedRamDisplay = "0.0 MB";
        }
    }
}
