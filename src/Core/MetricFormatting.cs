using System;

namespace SimplePCMonitor.Core
{
    public static class MetricFormatting
    {
        public static double BytesToGB(double bytes, int decimals = 1)
        {
            return Math.Round(bytes / (1024.0 * 1024.0 * 1024.0), decimals);
        }

        public static string FormatBytesAuto(long bytes)
        {
            if (bytes >= 1024L * 1024L * 1024L)
                return string.Format("{0:N2} GB", (double)bytes / (1024.0 * 1024.0 * 1024.0));
            if (bytes >= 1024L * 1024L)
                return string.Format("{0:N1} MB", (double)bytes / (1024.0 * 1024.0));
            if (bytes >= 1024L)
                return string.Format("{0:N0} KB", (double)bytes / 1024.0);
            return string.Format("{0} Bytes", bytes);
        }

        public static string ClassifyStatus(double percent, double warnThreshold = 80.0, double critThreshold = 90.0)
        {
            return percent >= critThreshold ? "Crit" : (percent >= warnThreshold ? "Warn" : "Ok");
        }
    }
}
