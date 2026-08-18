using System.Collections.Generic;

namespace SimplePCMonitor.Models
{
    public class CpuMetric
    {
        public double LoadPercent { get; set; }
        public int ProcessorCount { get; set; }
        public string Status { get; set; }

        public CpuMetric()
        {
            Status = "Ok";
        }
    }

    public class MemoryMetric
    {
        public double LoadPercent { get; set; }
        public double TotalGB { get; set; }
        public double UsedGB { get; set; }
        public double FreeGB { get; set; }
        public double PageFileTotalGB { get; set; }
        public double PageFileUsedGB { get; set; }
        public string Status { get; set; }

        public MemoryMetric()
        {
            Status = "Ok";
        }
    }

    public class DiskMetric
    {
        public string Name { get; set; }
        public string VolumeLabel { get; set; }
        public string DriveFormat { get; set; }
        public double TotalGB { get; set; }
        public double UsedGB { get; set; }
        public double FreeGB { get; set; }
        public double PercentUsed { get; set; }
        public string Status { get; set; }

        public DiskMetric()
        {
            Name = "";
            VolumeLabel = "";
            DriveFormat = "";
            Status = "Ok";
        }
    }

    public class NetworkMetric
    {
        public double DownloadSpeedKbps { get; set; }
        public double UploadSpeedKbps { get; set; }
        public string DownloadDisplay { get; set; }
        public string UploadDisplay { get; set; }
        public double TotalRxGB { get; set; }
        public double TotalTxGB { get; set; }
        public string AdapterName { get; set; }
        public string IPv4Address { get; set; }

        public NetworkMetric()
        {
            DownloadDisplay = "0 KB/s";
            UploadDisplay = "0 KB/s";
            AdapterName = "Disconnected";
            IPv4Address = "N/A";
        }
    }

    public class HardwareMetric
    {
        public string ComputerName { get; set; }
        public string OsName { get; set; }
        public string OsBuild { get; set; }
        public string CpuModel { get; set; }
        public string GpuModel { get; set; }
        public bool HasBattery { get; set; }
        public int BatteryPercent { get; set; }
        public bool IsCharging { get; set; }
        public string PowerSource { get; set; }
        public string UptimeDisplay { get; set; }

        public HardwareMetric()
        {
            ComputerName = "";
            OsName = "";
            OsBuild = "";
            CpuModel = "";
            GpuModel = "";
            BatteryPercent = 100;
            PowerSource = "AC Power";
            UptimeDisplay = "0d 0h 0m";
        }
    }

    public class ProcessMetric
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public double MemoryMB { get; set; }
        public string MemoryDisplay { get; set; }
        public double MemoryPercent { get; set; }
        public int Threads { get; set; }

        public ProcessMetric()
        {
            Name = "";
            MemoryDisplay = "";
        }
    }

    public class ServiceItem
    {
        public string ServiceName { get; set; }
        public string DisplayName { get; set; }
        public string Status { get; set; }
        public bool IsRunning { get; set; }

        public ServiceItem()
        {
            ServiceName = "";
            DisplayName = "";
            Status = "";
        }
    }

    public class ServiceMetric
    {
        public int TotalServices { get; set; }
        public int RunningCount { get; set; }
        public int StoppedCount { get; set; }
        public int OtherCount { get; set; }
        public List<ServiceItem> CriticalServices { get; set; }

        public ServiceMetric()
        {
            CriticalServices = new List<ServiceItem>();
        }
    }

    public class TaskItem
    {
        public string TaskName { get; set; }
        public string TaskPath { get; set; }
        public string State { get; set; }

        public TaskItem()
        {
            TaskName = "";
            TaskPath = "";
            State = "";
        }
    }
}
