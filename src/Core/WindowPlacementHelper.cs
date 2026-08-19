using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace SimplePCMonitor.Core
{
    public static class WindowPlacementHelper
    {
        public static Rect GetMonitorWorkAreaDips(Window window)
        {
            try
            {
                var helper = new WindowInteropHelper(window);
                IntPtr hwnd = helper.EnsureHandle();

                IntPtr hMonitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
                if (hMonitor != IntPtr.Zero)
                {
                    var mi = new NativeMethods.MONITORINFO();
                    mi.cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFO));

                    if (NativeMethods.GetMonitorInfo(hMonitor, ref mi))
                    {
                        var source = PresentationSource.FromVisual(window);
                        if (source != null && source.CompositionTarget != null)
                        {
                            Matrix transform = source.CompositionTarget.TransformFromDevice;
                            Point topLeft = transform.Transform(new Point(mi.rcWork.Left, mi.rcWork.Top));
                            Point bottomRight = transform.Transform(new Point(mi.rcWork.Right, mi.rcWork.Bottom));
                            return new Rect(topLeft, bottomRight);
                        }

                        // Fallback 1:1 if composition target not attached yet
                        return new Rect(mi.rcWork.Left, mi.rcWork.Top,
                                        mi.rcWork.Right - mi.rcWork.Left,
                                        mi.rcWork.Bottom - mi.rcWork.Top);
                    }
                }
            }
            catch { }

            // Fallback to primary screen work area
            return SystemParameters.WorkArea;
        }

        public static void SnapToBottomRight(Window window, double margin = 8.0)
        {
            if (window == null) return;

            Rect workArea = GetMonitorWorkAreaDips(window);
            window.Left = workArea.Right - window.Width - margin;
            window.Top = workArea.Bottom - window.Height - margin;
        }

        public static void ClampWindowToMonitor(Window window, double targetWidth, double targetHeight, Rect? preferredBounds = null)
        {
            if (window == null) return;

            Rect workArea = GetMonitorWorkAreaDips(window);
            double targetLeft;
            double targetTop;

            if (preferredBounds.HasValue && preferredBounds.Value.Width > 0)
            {
                targetLeft = preferredBounds.Value.Left;
                targetTop = preferredBounds.Value.Top;
            }
            else
            {
                // Default to centering in the active monitor's work area
                targetLeft = workArea.Left + (workArea.Width - targetWidth) / 2.0;
                targetTop = workArea.Top + (workArea.Height - targetHeight) / 2.0;
            }

            // Clamping bounds to ensure visibility on monitor
            if (targetLeft + targetWidth > workArea.Right)
            {
                targetLeft = workArea.Right - targetWidth;
            }
            if (targetLeft < workArea.Left)
            {
                targetLeft = workArea.Left;
            }

            if (targetTop + targetHeight > workArea.Bottom)
            {
                targetTop = workArea.Bottom - targetHeight;
            }
            if (targetTop < workArea.Top)
            {
                targetTop = workArea.Top;
            }

            window.Left = targetLeft;
            window.Top = targetTop;
        }
    }
}
