using System.Collections.Generic;

namespace SystemCoreMonitor.Models
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

        /// <summary>
        /// True for cloud/virtual mounts that Windows reports as fixed drives.
        /// Their capacity mirrors the host volume, so they are excluded from
        /// aggregate storage math and rendered without a usage bar.
        /// </summary>
        public bool IsVirtual { get; set; }
        public string KindLabel { get; set; }

        public DiskMetric()
        {
            Name = "";
            VolumeLabel = "";
            DriveFormat = "";
            Status = "Ok";
            KindLabel = "";
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
        public double CpuPercent { get; set; }
        public string CpuDisplay { get; set; }
        public double MemoryMB { get; set; }
        public string MemoryDisplay { get; set; }
        public double MemoryPercent { get; set; }
        public int Threads { get; set; }
        public bool IsProtected { get; set; }
        public bool IsHeavyConsumer { get; set; }
        public bool IsResponding { get; set; }
        public string PriorityClass { get; set; }

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
            CpuDisplay = "0.0%";
            MemoryDisplay = "";
            IsResponding = true;
            PriorityClass = "Normal";
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

    public class FolderSizeEntry
    {
        public string Name { get; set; }
        public string FullPath { get; set; }
        public long SizeBytes { get; set; }
        public string SizeDisplay { get; set; }
        public double PercentOfLargest { get; set; }

        /// <summary>True when part of this subtree could not be measured (access denied, path too long).</summary>
        public bool IsPartial { get; set; }

        public FolderSizeEntry()
        {
            Name = "";
            FullPath = "";
            SizeDisplay = "0 Bytes";
        }
    }

    public class SkippedEntry
    {
        public string Path { get; set; }

        /// <summary>"ReparsePoint" | "AccessDenied" | "PathTooLong" | "IOError"</summary>
        public string Reason { get; set; }

        public SkippedEntry()
        {
            Path = "";
            Reason = "";
        }
    }

    public class FolderSizeScanResult
    {
        public string RootPath { get; set; }
        public List<FolderSizeEntry> TopEntries { get; set; }
        public long TotalScannedBytes { get; set; }
        public int SkippedCount { get; set; }
        public List<SkippedEntry> SkippedEntries { get; set; }
        public bool WasCancelled { get; set; }
        public long ElapsedMilliseconds { get; set; }

        public FolderSizeScanResult()
        {
            RootPath = "";
            TopEntries = new List<FolderSizeEntry>();
            SkippedEntries = new List<SkippedEntry>();
        }
    }

    public class FolderScanProgress
    {
        public string CurrentPath { get; set; }
        public long BytesScannedSoFar { get; set; }
        public int DirectoriesVisited { get; set; }

        public FolderScanProgress()
        {
            CurrentPath = "";
        }
    }

    public class BloatFinding
    {
        public string Title { get; set; }
        public string Path { get; set; }
        public long SizeBytes { get; set; }
        public string SizeDisplay { get; set; }

        /// <summary>"Crit" | "Warn" | "Info"</summary>
        public string Severity { get; set; }
        public string Category { get; set; }

        /// <summary>"SafeDelete" | "LaunchTool" | "Informational"</summary>
        public string ActionKind { get; set; }
        public string ActionHint { get; set; }

        /// <summary>Localized button caption, resolved by the detector.</summary>
        public string ActionLabel { get; set; }

        public bool HasAction
        {
            get { return !string.Equals(ActionKind, "Informational", System.StringComparison.OrdinalIgnoreCase); }
        }

        public BloatFinding()
        {
            Title = "";
            Path = "";
            SizeDisplay = "0 Bytes";
            Severity = "Info";
            Category = "";
            ActionKind = "Informational";
            ActionHint = "";
            ActionLabel = "";
        }
    }
}
