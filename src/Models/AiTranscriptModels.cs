using System;
using System.Collections.Generic;

namespace SystemCoreMonitor.Models
{
    public enum AiTranscriptTargetType
    {
        ClaudeCli,
        GeminiAntigravity,
        ClaudeDesktop
    }

    public class AiTranscriptStoreInfo
    {
        public string StoreName { get; set; } = string.Empty;
        public string BasePath { get; set; } = string.Empty;
        public AiTranscriptTargetType TargetType { get; set; }
        public int TotalFileCount { get; set; }
        public long TotalSizeBytes { get; set; }
        public int StaleFileCount { get; set; }
        public long StaleSizeBytes { get; set; }

        public string TotalSizeDisplay => FormatBytes(TotalSizeBytes);
        public string StaleSizeDisplay => FormatBytes(StaleSizeBytes);

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1024L * 1024L * 1024L)
                return string.Format("{0:N2} GB", bytes / (1024.0 * 1024.0 * 1024.0));
            if (bytes >= 1024L * 1024L)
                return string.Format("{0:N1} MB", bytes / (1024.0 * 1024.0));
            if (bytes >= 1024L)
                return string.Format("{0:N0} KB", bytes / 1024.0);
            return string.Format("{0} B", bytes);
        }
    }

    public class AiTranscriptItem
    {
        public string SessionId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string WorkingDirectory { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public AiTranscriptTargetType TargetType { get; set; }
        public DateTime LastModified { get; set; }
        public long FileSizeBytes { get; set; }
        public bool IsStale { get; set; }
        public bool IsActive { get; set; }
        public int? ProcessId { get; set; }

        public string FileSizeDisplay
        {
            get
            {
                if (FileSizeBytes >= 1024L * 1024L)
                    return string.Format("{0:N1} MB", FileSizeBytes / (1024.0 * 1024.0));
                if (FileSizeBytes >= 1024L)
                    return string.Format("{0:N0} KB", FileSizeBytes / 1024.0);
                return string.Format("{0} B", FileSizeBytes);
            }
        }

        public string LastModifiedDisplay => LastModified != DateTime.MinValue ? LastModified.ToString("yyyy-MM-dd HH:mm") : "N/A";
    }

    public class AiTranscriptReport
    {
        public List<AiTranscriptStoreInfo> Stores { get; set; } = new();
        public List<AiTranscriptItem> Items { get; set; } = new();
        public long TotalSizeBytes { get; set; }
        public long StaleSizeBytes { get; set; }
        public int TotalFilesCount { get; set; }
        public int StaleFilesCount { get; set; }
        public bool ClaudeIsRunning { get; set; }
        public bool AntigravityIsRunning { get; set; }

        public string TotalSizeDisplay
        {
            get
            {
                if (TotalSizeBytes >= 1024L * 1024L * 1024L)
                    return string.Format("{0:N2} GB", TotalSizeBytes / (1024.0 * 1024.0 * 1024.0));
                if (TotalSizeBytes >= 1024L * 1024L)
                    return string.Format("{0:N1} MB", TotalSizeBytes / (1024.0 * 1024.0));
                return string.Format("{0:N0} KB", TotalSizeBytes / 1024.0);
            }
        }

        public string StaleSizeDisplay
        {
            get
            {
                if (StaleSizeBytes >= 1024L * 1024L * 1024L)
                    return string.Format("{0:N2} GB", StaleSizeBytes / (1024.0 * 1024.0 * 1024.0));
                if (StaleSizeBytes >= 1024L * 1024L)
                    return string.Format("{0:N1} MB", StaleSizeBytes / (1024.0 * 1024.0));
                return string.Format("{0:N0} KB", StaleSizeBytes / 1024.0);
            }
        }
    }

    public class AiTranscriptCleanupResult
    {
        public int FilesProcessed { get; set; }
        public long BytesFreed { get; set; }
        public int SkippedCount { get; set; }
        public List<string> Failures { get; set; } = new();
        public string Message { get; set; } = string.Empty;
        public bool Skipped { get; set; }

        public string BytesFreedDisplay
        {
            get
            {
                if (BytesFreed >= 1024L * 1024L * 1024L)
                    return string.Format("{0:N2} GB", BytesFreed / (1024.0 * 1024.0 * 1024.0));
                if (BytesFreed >= 1024L * 1024L)
                    return string.Format("{0:N1} MB", BytesFreed / (1024.0 * 1024.0));
                return string.Format("{0:N0} KB", BytesFreed / 1024.0);
            }
        }
    }
}
