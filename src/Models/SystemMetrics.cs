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
        public long LatencyMs { get; set; }
        public string PingDisplay { get; set; }

        public NetworkMetric()
        {
            DownloadDisplay = "0 KB/s";
            UploadDisplay = "0 KB/s";
            AdapterName = "Disconnected";
            IPv4Address = "N/A";
            LatencyMs = 0;
            PingDisplay = "-- ms";
        }
    }

    public class HardwareMetric
    {
        public string ComputerName { get; set; }
        public string OsName { get; set; }
        public string OsBuild { get; set; }
        public string CpuModel { get; set; }
        public string GpuModel { get; set; }
        public string NpuModel { get; set; }
        public bool HasBattery { get; set; }
        public int BatteryPercent { get; set; }
        public bool IsCharging { get; set; }
        public string PowerSource { get; set; }
        public string UptimeDisplay { get; set; }
        public string ActivePowerScheme { get; set; }

        public HardwareMetric()
        {
            ComputerName = "";
            OsName = "";
            OsBuild = "";
            CpuModel = "";
            GpuModel = "";
            NpuModel = "";
            BatteryPercent = 100;
            PowerSource = "AC Power";
            UptimeDisplay = "0d 0h 0m";
            ActivePowerScheme = "Balanced";
        }
    }

    public class GpuEngineBreakdown
    {
        public double Engine3DPercent { get; set; }
        public double ComputePercent { get; set; }
        public double VideoDecodePercent { get; set; }
        public double VideoProcessingPercent { get; set; }
        public double CopyPercent { get; set; }
    }

    public class GpuMetric
    {
        public string Name { get; set; }
        public string Vendor { get; set; }
        public string LuidString { get; set; }
        public bool IsDiscrete { get; set; }
        public bool IsPresent { get; set; }
        public double LoadPercent { get; set; }
        public GpuEngineBreakdown Engines { get; set; }
        public double DedicatedVramUsedMB { get; set; }
        public double DedicatedVramTotalMB { get; set; }
        public double DedicatedVramPercent { get; set; }
        public double SharedVramUsedMB { get; set; }
        public double SharedVramTotalMB { get; set; }
        public string Status { get; set; }

        public GpuMetric()
        {
            Name = "Graphics Adapter (GPU)";
            Vendor = "Unknown";
            LuidString = "";
            Engines = new GpuEngineBreakdown();
            Status = "Ok";
            IsPresent = true;
        }
    }

    public class NpuMetric
    {
        public string Name { get; set; }
        public string Vendor { get; set; }
        public string LuidString { get; set; }
        public bool IsPresent { get; set; }
        public string DeviceClass { get; set; }
        public double LoadPercent { get; set; }
        public string Status { get; set; }

        public NpuMetric()
        {
            Name = "NPU (Neural Processing Unit)";
            Vendor = "N/A";
            LuidString = "";
            IsPresent = false;
            DeviceClass = "ComputeAccelerator";
            Status = "Not Detected";
        }
    }

    public class ProcessMetric
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string FriendlyName { get; set; }
        public string CompanyName { get; set; }
        public string ExecutablePath { get; set; }
        public string WindowTitle { get; set; }
        public double MemoryMB { get; set; }
        public string MemoryDisplay { get; set; }
        public double MemoryPercent { get; set; }
        public int Threads { get; set; }
        public bool IsProtected { get; set; }
        public bool IsHeavyConsumer { get; set; }

        public string DisplayTitle
        {
            get { return !string.IsNullOrEmpty(FriendlyName) ? FriendlyName : Name; }
        }

        public string SubtitleText
        {
            get
            {
                if (!string.IsNullOrEmpty(CompanyName))
                {
                    return string.Format("{0} • {1}", Name, CompanyName);
                }
                return Name;
            }
        }

        public ProcessMetric()
        {
            Name = "";
            FriendlyName = "";
            CompanyName = "";
            ExecutablePath = "";
            WindowTitle = "";
            MemoryDisplay = "";
        }
    }

    public class ProcessDetailedInfo
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string FriendlyName { get; set; }
        public string CompanyName { get; set; }
        public string ExecutablePath { get; set; }
        public string WindowTitle { get; set; }
        public string FileVersion { get; set; }
        public string ProductVersion { get; set; }
        public string Description { get; set; }
        public string Copyright { get; set; }
        public string Architecture { get; set; }
        public string StartTimeDisplay { get; set; }
        public string UptimeDisplay { get; set; }
        public double WorkingSetMB { get; set; }
        public double PeakWorkingSetMB { get; set; }
        public double PrivateMemoryMB { get; set; }
        public double PagedMemoryMB { get; set; }
        public double VirtualMemoryMB { get; set; }
        public double MemoryPercent { get; set; }
        public int ThreadCount { get; set; }
        public int HandleCount { get; set; }
        public string PriorityClass { get; set; }
        public bool IsProtected { get; set; }
        public bool IsResponding { get; set; }

        public ProcessDetailedInfo()
        {
            Name = "";
            FriendlyName = "";
            CompanyName = "";
            ExecutablePath = "";
            WindowTitle = "";
            FileVersion = "N/A";
            ProductVersion = "N/A";
            Description = "";
            Copyright = "";
            Architecture = "64-bit";
            StartTimeDisplay = "N/A";
            UptimeDisplay = "N/A";
            PriorityClass = "Normal";
            IsResponding = true;
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

    public class StartupItem
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string Publisher { get; set; }
        public string Command { get; set; }
        public string ExecutablePath { get; set; }
        public string Location { get; set; }
        public string Status { get; set; }

        public StartupItem()
        {
            Name = "";
            DisplayName = "";
            Publisher = "";
            Command = "";
            ExecutablePath = "";
            Location = "";
            Status = "Enabled";
        }
    }
}
