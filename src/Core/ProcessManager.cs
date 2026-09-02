using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using SimplePCMonitor.Models;

namespace SimplePCMonitor.Core
{
    public static class ProcessManager
    {
        private static readonly HashSet<string> ProtectedProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "system",
            "idle",
            "smss",
            "csrss",
            "wininit",
            "services",
            "lsass",
            "svchost",
            "fontdrvhost",
            "dwm",
            "explorer",
            "sihost",
            "taskhostw",
            "RuntimeBroker",
            "audiodg",
            "spoolsv"
        };

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, [Out] StringBuilder lpExeName, ref int lpdwSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const uint PROCESS_SUSPEND_RESUME = 0x0800;
        private const uint PROCESS_SET_INFORMATION = 0x0200;

        public static bool IsProtected(string processName)
        {
            if (string.IsNullOrEmpty(processName)) return false;
            return ProtectedProcesses.Contains(processName);
        }

        public static bool IsSafeToControl(int pid, string processName)
        {
            if (pid <= 4) return false;
            if (IsProtected(processName)) return false;

            try
            {
                var proc = Process.GetProcessById(pid);
                if (proc.SessionId == 0) return false; // System session guard
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool SuspendProcess(int pid, string processName, out string message)
        {
            message = string.Empty;
            if (!IsSafeToControl(pid, processName))
            {
                message = string.Format("Process '{0}' (PID: {1}) is a protected system service and cannot be suspended.", processName, pid);
                return false;
            }

            IntPtr hProcess = IntPtr.Zero;
            try
            {
                hProcess = OpenProcess(PROCESS_SUSPEND_RESUME, false, pid);
                if (hProcess == IntPtr.Zero)
                {
                    message = string.Format("Access denied opening process '{0}' (requires elevation).", processName);
                    return false;
                }

                int status = NativeMethods.NtSuspendProcess(hProcess);
                if (status == NativeMethods.STATUS_SUCCESS)
                {
                    message = string.Format("Process '{0}' (PID: {1}) suspended successfully.", processName, pid);
                    return true;
                }
                else
                {
                    message = string.Format("Failed to suspend '{0}'. NTSTATUS: 0x{1:X8}", processName, status);
                    return false;
                }
            }
            catch (Exception ex)
            {
                message = string.Format("Error suspending '{0}': {1}", processName, ex.Message);
                return false;
            }
            finally
            {
                if (hProcess != IntPtr.Zero)
                {
                    CloseHandle(hProcess);
                }
            }
        }

        public static bool ResumeProcess(int pid, string processName, out string message)
        {
            message = string.Empty;
            if (pid <= 4)
            {
                message = "Invalid process ID.";
                return false;
            }

            IntPtr hProcess = IntPtr.Zero;
            try
            {
                hProcess = OpenProcess(PROCESS_SUSPEND_RESUME, false, pid);
                if (hProcess == IntPtr.Zero)
                {
                    message = string.Format("Access denied opening process '{0}'.", processName);
                    return false;
                }

                int status = NativeMethods.NtResumeProcess(hProcess);
                if (status == NativeMethods.STATUS_SUCCESS)
                {
                    message = string.Format("Process '{0}' (PID: {1}) resumed successfully.", processName, pid);
                    return true;
                }
                else
                {
                    message = string.Format("Failed to resume '{0}'. NTSTATUS: 0x{1:X8}", processName, status);
                    return false;
                }
            }
            catch (Exception ex)
            {
                message = string.Format("Error resuming '{0}': {1}", processName, ex.Message);
                return false;
            }
            finally
            {
                if (hProcess != IntPtr.Zero)
                {
                    CloseHandle(hProcess);
                }
            }
        }

        private static readonly HashSet<int> _suspendedPids = new HashSet<int>();

        public static bool IsSuspended(int pid)
        {
            lock (_suspendedPids)
            {
                return _suspendedPids.Contains(pid);
            }
        }

        public static bool SuspendProcess(int pid)
        {
            string name = string.Empty;
            try { name = Process.GetProcessById(pid).ProcessName; } catch { }
            string msg;
            bool ok = SuspendProcess(pid, name, out msg);
            if (ok)
            {
                lock (_suspendedPids) { _suspendedPids.Add(pid); }
            }
            return ok;
        }

        public static bool ResumeProcess(int pid)
        {
            string name = string.Empty;
            try { name = Process.GetProcessById(pid).ProcessName; } catch { }
            string msg;
            bool ok = ResumeProcess(pid, name, out msg);
            if (ok)
            {
                lock (_suspendedPids) { _suspendedPids.Remove(pid); }
            }
            return ok;
        }

        public static int ResumeAllSuspended()
        {
            int count = 0;
            List<int> pids;
            lock (_suspendedPids)
            {
                pids = new List<int>(_suspendedPids);
            }

            foreach (var pid in pids)
            {
                if (ResumeProcess(pid))
                {
                    count++;
                }
            }
            return count;
        }

        public static bool SetProcessPriority(int pid, ProcessPriorityClass priority, out string message)
        {
            message = string.Empty;
            try
            {
                var proc = Process.GetProcessById(pid);
                if (IsProtected(proc.ProcessName))
                {
                    message = string.Format("'{0}' is a system-critical process and its priority cannot be altered.", proc.ProcessName);
                    return false;
                }

                proc.PriorityClass = priority;
                message = string.Format("Priority for '{0}' set to {1}.", proc.ProcessName, priority);
                return true;
            }
            catch (Exception ex)
            {
                message = string.Format("Failed to set priority: {0}", ex.Message);
                return false;
            }
        }

        public static bool SetProcessPriority(int pid, ProcessPriorityClass priority)
        {
            string msg;
            return SetProcessPriority(pid, priority, out msg);
        }

        private static readonly object _consoleLock = new object();

        public enum ProcessCloseResult
        {
            ClosedGracefully,
            MinimizedToTray,
            StillRunning,
            ProtectedProcess,
            AccessDenied,
            Failed
        }

        public static async Task<ProcessCloseResult> RequestGracefulCloseAsync(int pid, string processName, int timeoutMs = 2000)
        {
            if (pid <= 4)
            {
                return ProcessCloseResult.ProtectedProcess;
            }

            if (!string.IsNullOrEmpty(processName) && IsProtected(processName))
            {
                return ProcessCloseResult.ProtectedProcess;
            }

            int currentSessionId = -1;
            try { currentSessionId = Process.GetCurrentProcess().SessionId; } catch { }

            Process proc = null;
            try
            {
                proc = Process.GetProcessById(pid);
                if (proc.HasExited)
                {
                    return ProcessCloseResult.ClosedGracefully;
                }

                if (string.IsNullOrEmpty(processName))
                {
                    try { processName = proc.ProcessName; } catch { }
                }

                if (IsProtected(processName))
                {
                    return ProcessCloseResult.ProtectedProcess;
                }

                if (currentSessionId > 0 && proc.SessionId == 0)
                {
                    return ProcessCloseResult.ProtectedProcess;
                }
            }
            catch (ArgumentException)
            {
                return ProcessCloseResult.ClosedGracefully; // Already dead
            }
            catch (Exception)
            {
                return ProcessCloseResult.AccessDenied;
            }

            IntPtr hWnd = IntPtr.Zero;
            try { hWnd = proc.MainWindowHandle; } catch { }

            // Support Windows 11 execution alias wrappers (e.g. Notepad, Terminal, Store app stubs)
            var childWindows = new List<IntPtr>();
            try
            {
                IntPtr hSnap = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.TH32CS_SNAPPROCESS, 0);
                if (hSnap != IntPtr.Zero && hSnap != new IntPtr(-1))
                {
                    try
                    {
                        var pe = new NativeMethods.PROCESSENTRY32();
                        pe.dwSize = (uint)Marshal.SizeOf(typeof(NativeMethods.PROCESSENTRY32));
                        if (NativeMethods.Process32First(hSnap, ref pe))
                        {
                            do
                            {
                                if (pe.th32ParentProcessID == pid)
                                {
                                    try
                                    {
                                        var cp = Process.GetProcessById((int)pe.th32ProcessID);
                                        if (cp.MainWindowHandle != IntPtr.Zero)
                                        {
                                            childWindows.Add(cp.MainWindowHandle);
                                        }
                                    }
                                    catch { }
                                }
                            } while (NativeMethods.Process32Next(hSnap, ref pe));
                        }
                    }
                    finally
                    {
                        NativeMethods.CloseHandle(hSnap);
                    }
                }
            }
            catch { }

            // Phase 1: Dispatch graceful close signal
            if (hWnd != IntPtr.Zero || childWindows.Count > 0)
            {
                try
                {
                    proc.CloseMainWindow();
                    if (hWnd != IntPtr.Zero)
                    {
                        NativeMethods.PostMessage(hWnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    }
                    foreach (var chWnd in childWindows)
                    {
                        NativeMethods.PostMessage(chWnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    }
                }
                catch { }
            }
            else
            {
                // CLI / Console process: Try AttachConsole + GenerateConsoleCtrlEvent under sync lock
                bool signalSent = false;
                lock (_consoleLock)
                {
                    try
                    {
                        if (NativeMethods.AttachConsole((uint)pid))
                        {
                            try
                            {
                                NativeMethods.GenerateConsoleCtrlEvent(NativeMethods.CTRL_C_EVENT, (uint)pid);
                                signalSent = true;
                            }
                            finally
                            {
                                NativeMethods.FreeConsole();
                            }
                        }
                    }
                    catch { }
                }

                if (!signalSent)
                {
                    try { proc.CloseMainWindow(); } catch { }
                }
            }

            // Await non-blocking timeout
            await Task.Delay(timeoutMs).ConfigureAwait(false);

            try
            {
                proc.Refresh();
                if (proc.HasExited)
                {
                    return ProcessCloseResult.ClosedGracefully;
                }

                // If process is still alive, check if its main window disappeared (minimized to System Tray)
                IntPtr postHwnd = IntPtr.Zero;
                try { postHwnd = proc.MainWindowHandle; } catch { }

                if (hWnd != IntPtr.Zero && postHwnd == IntPtr.Zero)
                {
                    return ProcessCloseResult.MinimizedToTray;
                }

                return ProcessCloseResult.StillRunning;
            }
            catch (InvalidOperationException)
            {
                return ProcessCloseResult.ClosedGracefully; // Exited during check
            }
            catch
            {
                return ProcessCloseResult.StillRunning;
            }
        }

        public static bool TerminateProcess(int pid, string processName, out string message)
        {
            message = string.Empty;
            if (IsProtected(processName) || pid <= 4)
            {
                message = string.Format("'{0}' is a protected Windows system process and cannot be terminated.", processName);
                return false;
            }

            try
            {
                var proc = Process.GetProcessById(pid);
                if (proc.SessionId == 0)
                {
                    message = string.Format("'{0}' belongs to System Session 0 and cannot be terminated.", processName);
                    return false;
                }

                proc.Kill();
                proc.WaitForExit(1500);
                message = string.Format("Process '{0}' (PID: {1}) was terminated successfully.", processName, pid);
                return true;
            }
            catch (Exception ex)
            {
                message = string.Format("Failed to terminate '{0}': {1}", processName, ex.Message);
                return false;
            }
        }

        public static bool TerminateProcessTree(int rootPid, bool force, out string message)
        {
            message = string.Empty;
            string rootName = "Process";
            try { rootName = Process.GetProcessById(rootPid).ProcessName; } catch { }

            if (IsProtected(rootName) || rootPid <= 4)
            {
                message = string.Format("'{0}' is a protected system process and its tree cannot be terminated.", rootName);
                return false;
            }

            // Discover tree using Toolhelp32 snapshot
            var parentToChildren = new Dictionary<int, List<int>>();
            IntPtr hSnapshot = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.TH32CS_SNAPPROCESS, 0);
            if (hSnapshot != IntPtr.Zero && hSnapshot != new IntPtr(-1))
            {
                try
                {
                    var pe = new NativeMethods.PROCESSENTRY32();
                    pe.dwSize = (uint)Marshal.SizeOf(typeof(NativeMethods.PROCESSENTRY32));
                    if (NativeMethods.Process32First(hSnapshot, ref pe))
                    {
                        do
                        {
                            int pid = (int)pe.th32ProcessID;
                            int ppid = (int)pe.th32ParentProcessID;
                            if (!parentToChildren.ContainsKey(ppid))
                            {
                                parentToChildren[ppid] = new List<int>();
                            }
                            parentToChildren[ppid].Add(pid);
                        } while (NativeMethods.Process32Next(hSnapshot, ref pe));
                    }
                }
                finally
                {
                    NativeMethods.CloseHandle(hSnapshot);
                }
            }

            var treePids = new List<int>();
            CollectTreeNodes(rootPid, parentToChildren, treePids);

            // Terminate in REVERSE topological order (leaves first, root last)
            int killedCount = 0;
            for (int i = treePids.Count - 1; i >= 0; i--)
            {
                int pid = treePids[i];
                try
                {
                    var p = Process.GetProcessById(pid);
                    if (!IsProtected(p.ProcessName) && p.SessionId != 0 && pid > 4)
                    {
                        p.Kill();
                        p.WaitForExit(500);
                        killedCount++;
                    }
                }
                catch { }
            }

            message = string.Format("Terminated {0} process(es) in tree (Root PID: {1}).", killedCount, rootPid);
            return killedCount > 0;
        }

        private static void CollectTreeNodes(int parentPid, Dictionary<int, List<int>> tree, List<int> result)
        {
            if (!result.Contains(parentPid))
            {
                result.Add(parentPid);
            }

            List<int> children;
            if (tree.TryGetValue(parentPid, out children))
            {
                foreach (int childPid in children)
                {
                    if (!result.Contains(childPid))
                    {
                        CollectTreeNodes(childPid, tree, result);
                    }
                }
            }
        }

        public static string GetProcessExecutablePath(int pid)
        {
            try
            {
                IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (hProcess != IntPtr.Zero)
                {
                    try
                    {
                        var sb = new StringBuilder(1024);
                        int size = sb.Capacity;
                        if (QueryFullProcessImageName(hProcess, 0, sb, ref size))
                        {
                            return sb.ToString();
                        }
                    }
                    finally
                    {
                        CloseHandle(hProcess);
                    }
                }

                // Fallback to standard Process.MainModule
                var p = Process.GetProcessById(pid);
                return p.MainModule != null ? p.MainModule.FileName : null;
            }
            catch
            {
                return null;
            }
        }

        public static string GetProcessCommandLine(int pid)
        {
            if (pid <= 4) return null;

            IntPtr hProcess = OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hProcess == IntPtr.Zero) return null;

            try
            {
                uint retLen = 0;
                int status = NativeMethods.NtQueryInformationProcess(
                    hProcess,
                    NativeMethods.ProcessCommandLineInformation,
                    IntPtr.Zero,
                    0,
                    out retLen
                );

                if (retLen == 0) return null;

                IntPtr buf = Marshal.AllocHGlobal((int)retLen);
                try
                {
                    status = NativeMethods.NtQueryInformationProcess(
                        hProcess,
                        NativeMethods.ProcessCommandLineInformation,
                        buf,
                        retLen,
                        out retLen
                    );

                    if (status == NativeMethods.STATUS_SUCCESS)
                    {
                        var ustr = (NativeMethods.UNICODE_STRING)Marshal.PtrToStructure(buf, typeof(NativeMethods.UNICODE_STRING));
                        if (ustr.Length > 0 && ustr.Buffer != IntPtr.Zero)
                        {
                            // Invariant Boundary Check: ensure Buffer is located within the allocated block
                            long bufStart = buf.ToInt64();
                            long bufEnd = bufStart + retLen;
                            long strStart = ustr.Buffer.ToInt64();
                            long strEnd = strStart + ustr.Length;

                            if (strStart >= bufStart && strEnd <= bufEnd)
                            {
                                return Marshal.PtrToStringUni(ustr.Buffer, ustr.Length / 2);
                            }
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buf);
                }
            }
            catch
            {
                return null;
            }
            finally
            {
                CloseHandle(hProcess);
            }

            return null;
        }

        public static string SanitizeCommandLine(string cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd)) return string.Empty;

            string s = cmd.Trim();

            // 1. Path privacy: mask user profile path
            try
            {
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(userProfile))
                {
                    s = s.Replace(userProfile, "%USERPROFILE%");
                }
            }
            catch { }

            // 2. Secret and token masking
            try
            {
                s = System.Text.RegularExpressions.Regex.Replace(s, @"(?i)(--?(?:api[-_]?key|token|bearer|password|secret)[\s=:""']+)([^\s""'&]+)", "$1[REDACTED]");
                s = System.Text.RegularExpressions.Regex.Replace(s, @"(sk-[a-zA-Z0-9_-]{8,})", "[REDACTED_API_KEY]");
                s = System.Text.RegularExpressions.Regex.Replace(s, @"(gh[pousr]_[a-zA-Z0-9]{20,})", "[REDACTED_GH_TOKEN]");
            }
            catch { }

            return s;
        }

        public static bool OpenProcessLocation(int pid, out string error)
        {
            error = string.Empty;
            try
            {
                string path = GetProcessExecutablePath(pid);
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    var psi = new ProcessStartInfo("explorer.exe", string.Format("/select,\"{0}\"", path))
                    {
                        UseShellExecute = true
                    };
                    Process.Start(psi);
                    return true;
                }
                else
                {
                    error = "Process executable location could not be resolved (access denied or virtual process).";
                    return false;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static void SearchProcessOnline(string processName)
        {
            try
            {
                if (string.IsNullOrEmpty(processName)) return;
                string query = Uri.EscapeDataString(processName + " windows process");
                string url = "https://www.google.com/search?q=" + query;
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { }
        }

        public static void CopyProcessDetailsToClipboard(int pid, string processName, double memMB, double memPct)
        {
            try
            {
                string text = string.Format("Process: {0} | PID: {1} | RAM: {2:N1} MB ({3:N1}%)", processName, pid, memMB, memPct);
                Clipboard.SetText(text);
            }
            catch { }
        }

        [DllImport("kernel32.dll", SetLastError = true, CallingConvention = CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWow64Process([In] IntPtr processHandle, [Out] out bool wow64Process);

        public static ProcessDetailedInfo GetDetailedProcessInfo(int pid, string fallbackName)
        {
            var info = new ProcessDetailedInfo
            {
                Id = pid,
                Name = fallbackName,
                IsProtected = IsProtected(fallbackName)
            };

            // Metadata from cache
            var meta = ProcessMetadataCache.GetMetadata(pid, fallbackName);
            info.FriendlyName = meta.FriendlyName;
            info.CompanyName = meta.CompanyName;
            info.ExecutablePath = meta.ExecutablePath;
            info.FileVersion = !string.IsNullOrEmpty(meta.FileVersion) ? meta.FileVersion : "N/A";

            try
            {
                var p = Process.GetProcessById(pid);
                info.Name = p.ProcessName;
                info.IsProtected = IsProtected(p.ProcessName);

                try { info.WindowTitle = p.MainWindowTitle; } catch { }
                try { info.IsResponding = p.Responding; } catch { }
                try { info.PriorityClass = p.PriorityClass.ToString(); } catch { }
                try { info.ThreadCount = p.Threads.Count; } catch { }
                try { info.HandleCount = p.HandleCount; } catch { }

                // Memory metrics
                try
                {
                    info.WorkingSetMB = Math.Round((double)p.WorkingSet64 / (1024.0 * 1024.0), 1);
                    info.PeakWorkingSetMB = Math.Round((double)p.PeakWorkingSet64 / (1024.0 * 1024.0), 1);
                    info.PrivateMemoryMB = Math.Round((double)p.PrivateMemorySize64 / (1024.0 * 1024.0), 1);
                    info.PagedMemoryMB = Math.Round((double)p.PagedMemorySize64 / (1024.0 * 1024.0), 1);
                    info.VirtualMemoryMB = Math.Round((double)p.VirtualMemorySize64 / (1024.0 * 1024.0), 1);
                }
                catch { }

                // Start time & Uptime
                try
                {
                    DateTime start = p.StartTime;
                    info.StartTimeDisplay = start.ToString("yyyy-MM-dd HH:mm:ss");
                    TimeSpan uptime = DateTime.Now - start;
                    if (uptime.TotalDays >= 1)
                        info.UptimeDisplay = string.Format("{0}d {1}h {2}m {3}s", (int)uptime.TotalDays, uptime.Hours, uptime.Minutes, uptime.Seconds);
                    else if (uptime.TotalHours >= 1)
                        info.UptimeDisplay = string.Format("{0}h {1}m {2}s", uptime.Hours, uptime.Minutes, uptime.Seconds);
                    else
                        info.UptimeDisplay = string.Format("{0}m {1}s", uptime.Minutes, uptime.Seconds);
                }
                catch
                {
                    info.StartTimeDisplay = "System / Protected";
                    info.UptimeDisplay = "N/A";
                }

                // Architecture
                try
                {
                    if (!Environment.Is64BitOperatingSystem)
                    {
                        info.Architecture = "32-bit (x86)";
                    }
                    else
                    {
                        IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                        if (hProcess != IntPtr.Zero)
                        {
                            try
                            {
                                bool isWow64;
                                if (IsWow64Process(hProcess, out isWow64))
                                {
                                    info.Architecture = isWow64 ? "32-bit (x86)" : "64-bit (x64)";
                                }
                            }
                            finally
                            {
                                CloseHandle(hProcess);
                            }
                        }
                    }
                }
                catch { }

                // File version details
                try
                {
                    if (!string.IsNullOrEmpty(info.ExecutablePath) && File.Exists(info.ExecutablePath))
                    {
                        var fvi = FileVersionInfo.GetVersionInfo(info.ExecutablePath);
                        if (!string.IsNullOrEmpty(fvi.FileDescription)) info.Description = fvi.FileDescription;
                        if (!string.IsNullOrEmpty(fvi.ProductVersion)) info.ProductVersion = fvi.ProductVersion;
                        if (!string.IsNullOrEmpty(fvi.LegalCopyright)) info.Copyright = fvi.LegalCopyright;
                        if (!string.IsNullOrEmpty(fvi.FileVersion)) info.FileVersion = fvi.FileVersion;
                    }
                }
                catch { }
            }
            catch { }

            return info;
        }

        public static void CopyDetailedDiagnosticToClipboard(ProcessDetailedInfo info)
        {
            if (info == null) return;
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("=== SIMPLE PC MONITOR PROCESS DIAGNOSTIC ===");
                sb.AppendLine(string.Format("Process Name   : {0}", info.Name));
                sb.AppendLine(string.Format("Friendly Name  : {0}", info.FriendlyName));
                sb.AppendLine(string.Format("PID            : {0}", info.Id));
                sb.AppendLine(string.Format("Publisher      : {0}", info.CompanyName));
                sb.AppendLine(string.Format("Executable Path: {0}", info.ExecutablePath));
                sb.AppendLine(string.Format("Architecture   : {0}", info.Architecture));
                sb.AppendLine(string.Format("File Version   : {0}", info.FileVersion));
                sb.AppendLine(string.Format("Window Title   : {0}", info.WindowTitle));
                sb.AppendLine(string.Format("Status         : {0} (Responding: {1})", info.PriorityClass, info.IsResponding));
                sb.AppendLine(string.Format("Start Time     : {0} (Uptime: {1})", info.StartTimeDisplay, info.UptimeDisplay));
                sb.AppendLine(string.Format("Threads / Hdls : {0} Threads / {1} Handles", info.ThreadCount, info.HandleCount));
                sb.AppendLine("--- MEMORY TELEMETRY ---");
                sb.AppendLine(string.Format("Working Set    : {0:N1} MB", info.WorkingSetMB));
                sb.AppendLine(string.Format("Peak WorkingSet: {0:N1} MB", info.PeakWorkingSetMB));
                sb.AppendLine(string.Format("Private Bytes  : {0:N1} MB", info.PrivateMemoryMB));
                sb.AppendLine(string.Format("Paged Bytes    : {0:N1} MB", info.PagedMemoryMB));
                sb.AppendLine(string.Format("Virtual Memory : {0:N1} MB", info.VirtualMemoryMB));
                sb.AppendLine("============================================");

                Clipboard.SetText(sb.ToString());
            }
            catch { }
        }
    }
}
