using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Threading;

namespace SimplePCMonitor.Core
{
    public static class CrashLogger
    {
        private static readonly object _logLock = new object();
        private static int _recentCrashCount = 0;
        private static DateTime _lastCrashWindow = DateTime.UtcNow;
        private const long MaxLogFileSizeBytes = 1024 * 1024; // 1 MB

        public static void Initialize()
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                LogException("AppDomain.UnhandledException", e.ExceptionObject as Exception, e.IsTerminating);
            };

            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                LogException("TaskScheduler.UnobservedTaskException", e.Exception, false);
                try { e.SetObserved(); } catch { }
            };
        }

        public static void AttachDispatcher(Dispatcher dispatcher)
        {
            if (dispatcher == null) return;
            dispatcher.UnhandledException += (s, e) =>
            {
                bool isFatal = e.Exception is OutOfMemoryException || e.Exception is StackOverflowException;
                LogException("DispatcherUnhandledException", e.Exception, isFatal);

                if (!isFatal)
                {
                    // Suppress recoverable UI glitch and keep running
                    e.Handled = true;
                }
            };
        }

        public static void LogException(string source, Exception ex, bool isFatal)
        {
            try
            {
                lock (_logLock)
                {
                    // 1. Sliding rate limiting (Max 5 logs per 10s)
                    var now = DateTime.UtcNow;
                    if ((now - _lastCrashWindow).TotalSeconds > 10)
                    {
                        _recentCrashCount = 0;
                        _lastCrashWindow = now;
                    }

                    _recentCrashCount++;
                    if (_recentCrashCount > 5)
                    {
                        return; // Prevent disk thrashing
                    }

                    // 2. Resolve Safe Directory
                    string logDir;
                    try
                    {
                        logDir = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "SimplePCMonitor", "Logs");

                        if (!Directory.Exists(logDir))
                            Directory.CreateDirectory(logDir);
                    }
                    catch
                    {
                        logDir = Path.GetTempPath();
                    }

                    string logPath = Path.Combine(logDir, "crash.log");

                    // 3. File size cap & rotation
                    try
                    {
                        var fi = new FileInfo(logPath);
                        if (fi.Exists && fi.Length > MaxLogFileSizeBytes)
                        {
                            string oldPath = Path.Combine(logDir, "crash.log.old");
                            if (File.Exists(oldPath)) File.Delete(oldPath);
                            File.Move(logPath, oldPath);
                        }
                    }
                    catch { }

                    // 4. Sanitize and build payload
                    var sb = new StringBuilder();
                    sb.AppendLine("================================================================================");
                    sb.AppendFormat("TIMESTAMP: {0:yyyy-MM-dd HH:mm:ss.fff} UTC\n", now);
                    sb.AppendFormat("SOURCE:    {0} (Fatal: {1})\n", source, isFatal);
                    sb.AppendFormat("OS:        {0} ({1})\n", Environment.OSVersion, Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit");
                    string exType = ex != null && ex.GetType() != null ? ex.GetType().FullName : "Unknown";
                    string exMsg = ex != null ? ex.Message : "No message";
                    string exStack = ex != null && ex.StackTrace != null ? ex.StackTrace : "No stack trace available";

                    sb.AppendFormat("EXCEPTION: {0}: {1}\n", exType, exMsg);
                    sb.AppendLine("STACK TRACE:");
                    sb.AppendLine(exStack);
                    sb.AppendLine("================================================================================\n");

                    File.AppendAllText(logPath, sb.ToString(), Encoding.UTF8);
                }
            }
            catch { }
        }
    }
}
