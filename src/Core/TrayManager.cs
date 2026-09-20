using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace SystemCoreMonitor.Core
{
    public class TrayManager : IDisposable
    {
        private NativeMethods.NOTIFYICONDATA _nid;
        private IntPtr _hwnd;
        private IntPtr _hIcon = IntPtr.Zero;
        private bool _isAdded = false;
        private readonly object _lock = new object();

        public const uint TrayIconId = 1001;

        public void Initialize(IntPtr hwnd, string initialTip)
        {
            lock (_lock)
            {
                _hwnd = hwnd;
                _hIcon = LoadApplicationIcon();

                _nid = new NativeMethods.NOTIFYICONDATA();
                _nid.cbSize = (uint)Marshal.SizeOf(typeof(NativeMethods.NOTIFYICONDATA));
                _nid.hWnd = _hwnd;
                _nid.uID = TrayIconId;
                _nid.uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP;
                _nid.uCallbackMessage = (uint)NativeMethods.WM_TRAYICON;
                _nid.hIcon = _hIcon;
                _nid.szTip = TruncateTip(initialTip ?? "SystemCoreMonitor");

                _isAdded = NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref _nid);
            }
        }

        public void UpdateTooltip(string tip)
        {
            lock (_lock)
            {
                if (!_isAdded || _hwnd == IntPtr.Zero) return;

                _nid.uFlags = NativeMethods.NIF_TIP;
                _nid.szTip = TruncateTip(tip);
                NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref _nid);
            }
        }

        public void ShowBalloon(string title, string message, uint infoFlags = NativeMethods.NIIF_INFO)
        {
            lock (_lock)
            {
                if (!_isAdded || _hwnd == IntPtr.Zero) return;

                _nid.uFlags = NativeMethods.NIF_INFO;
                _nid.szInfoTitle = TruncateString(title, 63);
                _nid.szInfo = TruncateString(message, 255);
                _nid.dwInfoFlags = infoFlags;
                NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref _nid);
            }
        }

        public void Recreate()
        {
            lock (_lock)
            {
                if (_hwnd == IntPtr.Zero) return;

                if (_hIcon == IntPtr.Zero)
                {
                    _hIcon = LoadApplicationIcon();
                }

                _nid.cbSize = (uint)Marshal.SizeOf(typeof(NativeMethods.NOTIFYICONDATA));
                _nid.hWnd = _hwnd;
                _nid.uID = TrayIconId;
                _nid.uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP;
                _nid.uCallbackMessage = (uint)NativeMethods.WM_TRAYICON;
                _nid.hIcon = _hIcon;

                _isAdded = NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref _nid);
            }
        }

        private IntPtr LoadApplicationIcon()
        {
            try
            {
                if (_hIcon != IntPtr.Zero)
                {
                    return _hIcon;
                }

                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string icoPath = Path.Combine(baseDir, "icon.ico");
                if (File.Exists(icoPath))
                {
                    IntPtr h = NativeMethods.LoadImageW(
                        IntPtr.Zero,
                        icoPath,
                        NativeMethods.IMAGE_ICON,
                        0,
                        0,
                        NativeMethods.LR_LOADFROMFILE | NativeMethods.LR_DEFAULTSIZE);
                    if (h != IntPtr.Zero) return h;
                }

                string parentIco = Path.Combine(baseDir, "..", "icon.ico");
                if (File.Exists(parentIco))
                {
                    IntPtr h = NativeMethods.LoadImageW(
                        IntPtr.Zero,
                        parentIco,
                        NativeMethods.IMAGE_ICON,
                        0,
                        0,
                        NativeMethods.LR_LOADFROMFILE | NativeMethods.LR_DEFAULTSIZE);
                    if (h != IntPtr.Zero) return h;
                }

                // Fallback to executable's embedded icon via shell32
                string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                {
                    uint count = NativeMethods.ExtractIconExW(exePath, 0, out _, out IntPtr hSmall, 1);
                    if (count > 0 && hSmall != IntPtr.Zero)
                    {
                        return hSmall;
                    }
                }
            }
            catch { }

            return IntPtr.Zero;
        }

        private static string TruncateTip(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Length > 127 ? text.Substring(0, 127) : text;
        }

        private static string TruncateString(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Length > maxLength ? text.Substring(0, maxLength) : text;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_isAdded)
                {
                    NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref _nid);
                    _isAdded = false;
                }

                if (_hIcon != IntPtr.Zero)
                {
                    try { NativeMethods.DestroyIcon(_hIcon); } catch { }
                    _hIcon = IntPtr.Zero;
                }
            }
        }
    }
}