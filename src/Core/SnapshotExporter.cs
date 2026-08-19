using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using SimplePCMonitor.Models;

namespace SimplePCMonitor.Core
{
    public static class SnapshotExporter
    {
        public static string GenerateMarkdown(
            CpuMetric cpu,
            GpuMetric gpu,
            NpuMetric npu,
            MemoryMetric mem,
            List<DiskMetric> disks,
            NetworkMetric net,
            HardwareMetric hw,
            List<ProcessMetric> procs,
            ServiceMetric svc)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Simple PC Monitor - System Diagnostic Snapshot");
            sb.AppendLine();
            sb.AppendLine(string.Format("**Timestamp:** `{0}`", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
            sb.AppendLine(string.Format("**Machine:** `{0}` | **OS:** {1} ({2})", hw.ComputerName, hw.OsName, hw.OsBuild));
            sb.AppendLine(string.Format("**Uptime:** {0} | **Power Scheme:** {1}", hw.UptimeDisplay, hw.ActivePowerScheme));
            sb.AppendLine();

            sb.AppendLine("## 1. Hardware & Accelerators");
            sb.AppendLine(string.Format("- **CPU:** {0} ({1} Cores)", hw.CpuModel, cpu != null ? cpu.ProcessorCount : 0));
            sb.AppendLine(string.Format("- **GPU:** {0}", !string.IsNullOrEmpty(hw.GpuModel) ? hw.GpuModel : (gpu != null ? gpu.Name : "N/A")));
            sb.AppendLine(string.Format("- **NPU (AI Engine):** {0}", !string.IsNullOrEmpty(hw.NpuModel) ? hw.NpuModel : (npu != null && npu.IsPresent ? npu.Name : "None / Not Detected")));
            sb.AppendLine(string.Format("- **Power Source:** {0} ({1}% Battery)", hw.PowerSource, hw.BatteryPercent));
            sb.AppendLine();

            sb.AppendLine("## 2. Core Real-Time Telemetry");
            if (cpu != null)
                sb.AppendLine(string.Format("- **CPU Load:** `{0:N1}%`", cpu.LoadPercent));
            if (gpu != null && gpu.IsPresent)
                sb.AppendLine(string.Format("- **GPU Load:** `{0:N1}%` (3D: `{1:N1}%`, Compute: `{2:N1}%`, Video: `{3:N1}%` | VRAM: `{4:N0} MB Dedicated`)",
                    gpu.LoadPercent, gpu.Engines.Engine3DPercent, gpu.Engines.ComputePercent, gpu.Engines.VideoDecodePercent, gpu.DedicatedVramTotalMB));
            if (npu != null && npu.IsPresent)
                sb.AppendLine(string.Format("- **NPU Load:** `{0:N1}%` (Status: `{1}`)", npu.LoadPercent, npu.Status));
            if (mem != null)
                sb.AppendLine(string.Format("- **Memory (RAM):** `{0:N1} GB / {1:N1} GB` ({2:N0}% used)", mem.UsedGB, mem.TotalGB, mem.LoadPercent));
            if (net != null)
                sb.AppendLine(string.Format("- **Network I/O:** `↓ {0} | ↑ {1}` (Ping: `{2}` to DNS)", net.DownloadDisplay, net.UploadDisplay, net.PingDisplay));
            sb.AppendLine();

            sb.AppendLine("## 3. Storage Volumes");
            if (disks != null && disks.Count > 0)
            {
                sb.AppendLine("| Drive | Label | Format | Used / Total | Free | % Used |");
                sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- |");
                foreach (var d in disks)
                {
                    sb.AppendLine(string.Format("| **{0}** | {1} | {2} | {3:N1} GB / {4:N1} GB | {5:N1} GB | {6:N0}% |",
                        d.Name, d.VolumeLabel, d.DriveFormat, d.UsedGB, d.TotalGB, d.FreeGB, d.PercentUsed));
                }
            }
            sb.AppendLine();

            sb.AppendLine("## 4. Top Memory Consuming Processes");
            if (procs != null && procs.Count > 0)
            {
                sb.AppendLine("| PID | Process Name | Working Set (MB) | RAM Share (%) | Threads |");
                sb.AppendLine("| :--- | :--- | :--- | :--- | :--- |");
                foreach (var p in procs)
                {
                    sb.AppendLine(string.Format("| `{0}` | **{1}** | {2:N1} MB | {3:N1}% | {4} |",
                        p.Id, p.Name, p.MemoryMB, p.MemoryPercent, p.Threads));
                }
            }
            sb.AppendLine();

            if (svc != null)
            {
                sb.AppendLine("## 5. Services Summary");
                sb.AppendLine(string.Format("- **Total Services:** {0} (Running: `{1}`, Stopped: `{2}`)",
                    svc.TotalServices, svc.RunningCount, svc.StoppedCount));
            }

            return sb.ToString();
        }

        public static bool CopySnapshotToClipboard(
            CpuMetric cpu,
            GpuMetric gpu,
            NpuMetric npu,
            MemoryMetric mem,
            List<DiskMetric> disks,
            NetworkMetric net,
            HardwareMetric hw,
            List<ProcessMetric> procs,
            ServiceMetric svc)
        {
            try
            {
                string md = GenerateMarkdown(cpu, gpu, npu, mem, disks, net, hw, procs, svc);
                Clipboard.SetText(md);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
