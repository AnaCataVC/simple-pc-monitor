using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using SimplePCMonitor.Core;
using SimplePCMonitor.Models;
using SimplePCMonitor.Modules;

namespace SimplePCMonitor.UI
{
    public partial class MainWindow : Window
    {
        private readonly CpuCollector _cpu;
        private readonly GpuCollector _gpu;
        private readonly NpuCollector _npu;
        private readonly WindowsAcceleratorEngine _accelEngine;
        private readonly MemoryCollector _mem;
        private readonly DiskCollector _disk;
        private readonly NetworkCollector _net;
        private readonly HardwareCollector _hw;
        private readonly ProcessCollector _proc;
        private readonly ServiceCollector _svc;
        private readonly StartupCollector _startup;
        private readonly TaskCollector _tasks;
        private readonly TrayManager _trayManager;

        private readonly List<double> _cpuHistory;
        private readonly List<double> _netHistory;
        private const int MaxHistoryPoints = 30;

        private readonly PointCollection _cpuLinePoints = new PointCollection();
        private readonly PointCollection _cpuPolyPoints = new PointCollection();
        private readonly PointCollection _netLinePoints = new PointCollection();
        private readonly PointCollection _netPolyPoints = new PointCollection();

        private AppConfig _config;
        private CancellationTokenSource _cts;
        private int _cycleCount;
        private uint _wmTaskbarCreated;
        private HwndSource _hwndSource;
        private bool _isTrayMode;
        private bool _isExiting;
        private WindowState _lastWindowState = WindowState.Normal;
        private System.Windows.Media.Effects.Effect _cachedShadowEffect;

        // Cached telemetry for snapshots
        private CpuMetric _lastCpu;
        private GpuMetric _lastGpu;
        private NpuMetric _lastNpu;
        private MemoryMetric _lastMem;
        private List<DiskMetric> _lastDisks;
        private NetworkMetric _lastNet;
        private HardwareMetric _lastHw;
        private List<ProcessMetric> _lastProcs;
        private ServiceMetric _lastSvc;
        private List<StartupItem> _lastStartup;
        private List<TaskItem> _lastTasks;

        private DispatcherTimer _toastTimer;
        private Rect? _lastFullBounds;
        private DispatcherTimer _searchDebounceTimer;
        private bool _sortByCpu = true;
        private string _procSearchQuery = string.Empty;

        public MainWindow()
        {
            InitializeComponent();

            _cpu = new CpuCollector();
            _gpu = new GpuCollector();
            _npu = new NpuCollector();
            _accelEngine = new WindowsAcceleratorEngine();
            _mem = new MemoryCollector();
            _disk = new DiskCollector();
            _net = new NetworkCollector();
            _hw = new HardwareCollector();
            _proc = new ProcessCollector();
            _svc = new ServiceCollector();
            _startup = new StartupCollector();
            _tasks = new TaskCollector();
            _trayManager = new TrayManager();

            _cpuHistory = new List<double>();
            _netHistory = new List<double>();

            _searchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _searchDebounceTimer.Tick += (s, ev) =>
            {
                _searchDebounceTimer.Stop();
                RefreshProcessListManually();
            };

            _config = ConfigManager.Load();

            ApplyTheme(_config.Theme);
            ApplyLanguage(_config.Language);
            LoadAppIcon();

            TxtInterval.Text = string.Format("{0}s", _config.RefreshIntervalSeconds);
            ApplyViewMode(_config.ViewMode);

            if (_config.AlwaysOnTop)
            {
                Topmost = true;
                PathPin.Fill = (Brush)FindResource("AccentCpu");
                PathWidgetPin.Fill = (Brush)FindResource("AccentCpu");
            }

            string activeScheme;
            PowerPlanManager.GetActiveScheme(out activeScheme);
            UpdatePowerButtonsHighlight(activeScheme);

            if (OuterBorder != null)
            {
                _cachedShadowEffect = OuterBorder.Effect;
            }

            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
            StateChanged += MainWindow_StateChanged;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                var helper = new WindowInteropHelper(this);
                var hwnd = helper.Handle;

                _hwndSource = HwndSource.FromHwnd(hwnd);
                if (_hwndSource != null)
                {
                    _hwndSource.AddHook(HwndMessageHook);
                }

                _wmTaskbarCreated = NativeMethods.RegisterWindowMessage("TaskbarCreated");
                _trayManager.Initialize(hwnd, "Simple PC Monitor");

                // Check command line arguments for start in tray
                string[] args = Environment.GetCommandLineArgs();
                bool startInTray = false;
                for (int i = 1; i < args.Length; i++)
                {
                    if (string.Equals(args[i], "--tray", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(args[i], "--minimized", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(args[i], "-tray", StringComparison.OrdinalIgnoreCase))
                    {
                        startInTray = true;
                        break;
                    }
                }

                if (startInTray || _config.StartMinimizedToTray)
                {
                    HideToTray();
                }
            }
            catch { }
        }

        private IntPtr HwndMessageHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_GETMINMAXINFO)
            {
                WmGetMinMaxInfo(hwnd, lParam);
                handled = true;
            }
            else if (msg == NativeMethods.WM_TRAYICON)
            {
                int eventId = lParam.ToInt32() & 0xFFFF;
                switch (eventId)
                {
                    case NativeMethods.WM_LBUTTONUP:
                        if (_isTrayMode || !IsVisible)
                        {
                            RestoreFromTray();
                        }
                        else
                        {
                            HideToTray();
                        }
                        handled = true;
                        break;

                    case NativeMethods.WM_LBUTTONDBLCLK:
                        RestoreFromTray();
                        handled = true;
                        break;

                    case NativeMethods.WM_RBUTTONUP:
                    case NativeMethods.WM_CONTEXTMENU:
                        OpenTrayContextMenu(hwnd);
                        handled = true;
                        break;
                }
            }
            else if (msg != 0 && (uint)msg == _wmTaskbarCreated)
            {
                _trayManager.Recreate();
                handled = true;
            }

            return IntPtr.Zero;
        }

        private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
        {
            try
            {
                var mmi = (NativeMethods.MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(NativeMethods.MINMAXINFO));
                IntPtr hMonitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);

                if (hMonitor != IntPtr.Zero)
                {
                    var mi = new NativeMethods.MONITORINFO();
                    mi.cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFO));

                    if (NativeMethods.GetMonitorInfo(hMonitor, ref mi))
                    {
                        var rcWork = mi.rcWork;
                        var rcMonitor = mi.rcMonitor;

                        mmi.ptMaxPosition.X = Math.Abs(rcWork.Left - rcMonitor.Left);
                        mmi.ptMaxPosition.Y = Math.Abs(rcWork.Top - rcMonitor.Top);
                        mmi.ptMaxSize.X = Math.Abs(rcWork.Right - rcWork.Left);
                        mmi.ptMaxSize.Y = Math.Abs(rcWork.Bottom - rcWork.Top);
                        mmi.ptMaxTrackSize.X = mmi.ptMaxSize.X;
                        mmi.ptMaxTrackSize.Y = mmi.ptMaxSize.Y;
                        mmi.ptMinTrackSize.X = 380;
                        mmi.ptMinTrackSize.Y = 88;
                    }
                }

                Marshal.StructureToPtr(mmi, lParam, true);
            }
            catch { }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            StartTelemetryLoop();
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
            }
            if (_accelEngine != null)
            {
                _accelEngine.Dispose();
            }
            if (_hwndSource != null)
            {
                try { _hwndSource.RemoveHook(HwndMessageHook); } catch { }
                _hwndSource = null;
            }
            if (_trayManager != null)
            {
                _trayManager.Dispose();
            }
        }

        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized && _config.MinimizeToTray)
            {
                HideToTray();
                return;
            }
            else if (WindowState != WindowState.Minimized)
            {
                _lastWindowState = WindowState;
            }

            if (WindowState == WindowState.Maximized)
            {
                if (PathMaximize != null)
                {
                    PathMaximize.Data = TryFindResource("IconRestore") as Geometry ?? (Geometry)FindResource("IconRestore");
                }
                if (BtnMaximize != null)
                {
                    BtnMaximize.ToolTip = LocalizationManager.Get("BtnRestore", "Restaurar");
                }
                if (OuterBorder != null)
                {
                    OuterBorder.Margin = new Thickness(0);
                    OuterBorder.CornerRadius = new CornerRadius(0);
                    OuterBorder.BorderThickness = new Thickness(0);
                    OuterBorder.Effect = null;
                }
            }
            else if (WindowState == WindowState.Normal)
            {
                if (PathMaximize != null)
                {
                    PathMaximize.Data = TryFindResource("IconMaximize") as Geometry ?? (Geometry)FindResource("IconMaximize");
                }
                if (BtnMaximize != null)
                {
                    BtnMaximize.ToolTip = LocalizationManager.Get("BtnMaximize", "Maximizar");
                }
                if (OuterBorder != null)
                {
                    OuterBorder.Margin = new Thickness(8);
                    OuterBorder.CornerRadius = new CornerRadius(14);
                    OuterBorder.BorderThickness = new Thickness(1);
                    OuterBorder.Effect = _cachedShadowEffect;
                }
            }
        }

        private void LoadAppIcon()
        {
            try
            {
                string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.png");
                if (File.Exists(iconPath))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(iconPath, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();

                    ImgAppLogo.Source = bitmap;
                    ImgWidgetLogo.Source = bitmap;
                    Icon = bitmap;
                }
                else
                {
                    var uri = new Uri("pack://application:,,,/icon.png", UriKind.Absolute);
                    var bitmap = new BitmapImage(uri);
                    ImgAppLogo.Source = bitmap;
                    ImgWidgetLogo.Source = bitmap;
                    Icon = bitmap;
                }
            }
            catch { }
        }

        // =========================================================================
        // BACKGROUND TELEMETRY LOOP
        // =========================================================================

        private void StartTelemetryLoop()
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            Task.Run(async () =>
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            if (_isTrayMode)
                            {
                                // Lightweight pulse: CPU and RAM only (< 0.01ms CPU time)
                                var cpuLite = _cpu.Sample();
                                var memLite = _mem.Sample();

                                string tip = string.Format("Simple PC Monitor\nCPU: {0:F0}% | RAM: {1:F0}%",
                                    cpuLite.LoadPercent, memLite != null ? memLite.LoadPercent : 0.0);
                                _trayManager.UpdateTooltip(tip);

                                // Sleep 5 seconds in background mode to save CPU/battery
                                await Task.Delay(5000, token).ConfigureAwait(false);
                                continue;
                            }

                            var cpu = _cpu.Sample();
                            var engineLoads = _accelEngine.SampleAllEngines();
                            var gpu = _gpu.Sample(engineLoads);
                            var npu = _npu.Sample(engineLoads, gpu.LuidString);
                            var mem = _mem.Sample();
                            var disks = _disk.Sample();
                            var net = _net.Sample();
                            var hw = _hw.Sample();

                            List<ProcessMetric> procs = null;
                            ServiceMetric svc = null;
                            List<TaskItem> tasks = null;
                            List<StartupItem> startup = null;

                            // Always sample processes to ensure responsive CPU% & real-time search
                            procs = _proc.Sample(15, mem != null ? mem.TotalGB : 16.0, _sortByCpu, _procSearchQuery);

                            if (_cycleCount % 3 == 0)
                            {
                                svc = _svc.Sample();
                                tasks = _tasks.Sample();
                                startup = _startup.Sample();
                            }
                            _cycleCount++;

                            string activeTip = string.Format("Simple PC Monitor\nCPU: {0:F0}% | RAM: {1:F0}% | GPU: {2:F0}%",
                                cpu.LoadPercent, mem != null ? mem.LoadPercent : 0.0, gpu.LoadPercent);
                            _trayManager.UpdateTooltip(activeTip);

                            await Dispatcher.InvokeAsync(() =>
                            {
                                UpdateUI(cpu, gpu, npu, mem, disks, net, hw, procs, svc, tasks, startup);
                            }, DispatcherPriority.Background);
                        }
                        catch { }

                        int intervalMs = Math.Max(1000, _config.RefreshIntervalSeconds * 1000);
                        await Task.Delay(intervalMs, token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) { }
            }, token);
        }

        // =========================================================================
        // UI BINDING & UPDATE
        // =========================================================================

        private void UpdateUI(
            CpuMetric cpu,
            GpuMetric gpu,
            NpuMetric npu,
            MemoryMetric mem,
            List<DiskMetric> disks,
            NetworkMetric net,
            HardwareMetric hw,
            List<ProcessMetric> procs,
            ServiceMetric svc,
            List<TaskItem> tasks,
            List<StartupItem> startup)
        {
            _lastCpu = cpu;
            _lastGpu = gpu;
            _lastNpu = npu;
            _lastMem = mem;
            _lastDisks = disks;
            _lastNet = net;
            _lastHw = hw;
            if (procs != null) _lastProcs = procs;
            if (svc != null) _lastSvc = svc;
            if (tasks != null) _lastTasks = tasks;
            if (startup != null) _lastStartup = startup;

            // 1. CPU
            if (cpu != null)
            {
                TxtCpuVal.Text = string.Format("{0:N1}%", cpu.LoadPercent);
                ProgressCpu.Value = Math.Max(0, Math.Min(100, cpu.LoadPercent));
                TxtCpuCores.Text = string.Format("{0} Cores", cpu.ProcessorCount);
                TxtCpuCoresBadge.Text = string.Format("{0} Cores", cpu.ProcessorCount);
                TxtCpuSub.Text = hw != null && !string.IsNullOrEmpty(hw.CpuModel) ? hw.CpuModel : "Direct Win32 Kernel";

                // Widget CPU
                TxtWidgetCpu.Text = string.Format("{0:N0}%", cpu.LoadPercent);
                ProgressWidgetCpu.Value = Math.Max(0, Math.Min(100, cpu.LoadPercent));

                // Sparkline
                _cpuHistory.Add(cpu.LoadPercent);
                if (_cpuHistory.Count > MaxHistoryPoints) _cpuHistory.RemoveAt(0);
                double cpuPeak = _cpuHistory.Count > 0 ? _cpuHistory.Max() : 0.0;
                TxtCpuLivePeak.Text = string.Format("{0}: {1:N0}%", LocalizationManager.Get("PeakLabel"), cpuPeak);
                RenderSparkline(LineCpuStroke, PolyCpuArea, _cpuHistory, CanvasCpuGraph, 100.0, _cpuLinePoints, _cpuPolyPoints);
            }

            // 2. GPU
            if (gpu != null && gpu.IsPresent)
            {
                TxtGpuVal.Text = string.Format("{0:N1}%", gpu.LoadPercent);
                ProgressGpu.Value = Math.Max(0, Math.Min(100, gpu.LoadPercent));
                TxtGpuSub.Text = !string.IsNullOrEmpty(gpu.Name) ? gpu.Name : "Graphics Adapter";
                TxtGpuTypeBadge.Text = gpu.IsDiscrete ? LocalizationManager.Get("GpuDiscreteBadge") : LocalizationManager.Get("GpuDirect3DBadge");

                // Accelerators Tab GPU
                TxtGpuTabLoad.Text = string.Format("{0:N1}%", gpu.LoadPercent);
                TxtGpuTabName.Text = gpu.Name;
                TxtGpu3D.Text = string.Format("{0:N1}%", gpu.Engines.Engine3DPercent);
                TxtGpuCompute.Text = string.Format("{0:N1}%", gpu.Engines.ComputePercent);
                TxtGpuVideo.Text = string.Format("{0:N1}%", gpu.Engines.VideoDecodePercent);
                TxtGpuCopy.Text = string.Format("{0:N1}%", gpu.Engines.CopyPercent);
                TxtGpuVram.Text = string.Format("{0:N0} MB / {1:N0} MB", gpu.DedicatedVramUsedMB, gpu.DedicatedVramTotalMB);
                ProgressGpuVram.Value = gpu.DedicatedVramTotalMB > 0 ? Math.Min(100, (gpu.DedicatedVramUsedMB / gpu.DedicatedVramTotalMB) * 100.0) : 0;

                // Widget GPU
                TxtWidgetGpu.Text = string.Format("{0:N0}%", gpu.LoadPercent);
                ProgressWidgetGpu.Value = Math.Max(0, Math.Min(100, gpu.LoadPercent));
            }

            // 3. NPU (AI)
            if (npu != null && npu.IsPresent)
            {
                CardNpu.Visibility = Visibility.Visible;
                TxtNpuVal.Text = string.Format("{0:N1}%", npu.LoadPercent);
                ProgressNpu.Value = Math.Max(0, Math.Min(100, npu.LoadPercent));
                TxtNpuStatusBadge.Text = npu.LoadPercent > 0.5 ? LocalizationManager.Get("NpuActiveBadge") : LocalizationManager.Get("NpuIdleBadge");
                TxtNpuSub.Text = !string.IsNullOrEmpty(npu.Name) ? npu.Name : "Intel AI Boost";

                // Accelerators Tab NPU
                TxtNpuTabStatus.Text = string.Format("{0} ({1:N1}%)", npu.LoadPercent > 0.5 ? LocalizationManager.Get("NpuActiveBadge") : LocalizationManager.Get("NpuIdleBadge"), npu.LoadPercent);
                TxtNpuTabName.Text = npu.Name;
                TxtNpuTabLoad.Text = string.Format("{0:N1}%", npu.LoadPercent);
                ProgressNpuTab.Value = Math.Max(0, Math.Min(100, npu.LoadPercent));
            }
            else
            {
                TxtNpuVal.Text = "0.0%";
                ProgressNpu.Value = 0;
                TxtNpuStatusBadge.Text = "N/A";
                TxtNpuSub.Text = LocalizationManager.Get("NpuNotDetected");
                TxtNpuTabStatus.Text = LocalizationManager.Get("NpuNotDetected");
                TxtNpuTabLoad.Text = "0.0%";
                ProgressNpuTab.Value = 0;
            }

            // 4. RAM
            if (mem != null)
            {
                TxtRamVal.Text = string.Format("{0:N0}%", mem.LoadPercent);
                ProgressRam.Value = Math.Max(0, Math.Min(100, mem.LoadPercent));
                TxtRamTotalBadge.Text = string.Format("{0:N0} GB", mem.TotalGB);
                TxtRamSub.Text = string.Format(LocalizationManager.Get("RamUsedLabel"), mem.UsedGB, mem.TotalGB);

                // Widget RAM
                TxtWidgetRam.Text = string.Format("{0:N0}%", mem.LoadPercent);
                ProgressWidgetRam.Value = Math.Max(0, Math.Min(100, mem.LoadPercent));
            }

            // 5. DISK
            if (disks != null && disks.Count > 0)
            {
                var primaryDisk = disks[0];
                TxtDiskVal.Text = string.Format("{0:N0}%", primaryDisk.PercentUsed);
                ProgressDisk.Value = Math.Max(0, Math.Min(100, primaryDisk.PercentUsed));
                TxtDiskTotalBadge.Text = primaryDisk.Name;
                TxtDiskSub.Text = string.Format(LocalizationManager.Get("DiskFreeLabel"), primaryDisk.FreeGB);

                ListDrivesFull.ItemsSource = disks;
            }

            // 6. NETWORK
            if (net != null)
            {
                TxtNetVal.Text = net.DownloadDisplay;
                TxtNetPingBadge.Text = net.PingDisplay;
                TxtNetSub.Text = string.Format("↓ {0}  ↑ {1}", net.DownloadDisplay, net.UploadDisplay);

                // Widget Net
                TxtWidgetNet.Text = net.DownloadDisplay;
                double netProgress = Math.Min(100, (net.DownloadSpeedKbps / 5000.0) * 100.0);
                ProgressWidgetNet.Value = Math.Max(0, netProgress);

                // Sparkline
                _netHistory.Add(net.DownloadSpeedKbps);
                if (_netHistory.Count > MaxHistoryPoints) _netHistory.RemoveAt(0);
                double netPeak = _netHistory.Count > 0 ? _netHistory.Max() : 0.0;
                TxtNetLivePeak.Text = string.Format("{0}: {1:N0} KB/s", LocalizationManager.Get("PeakLabel"), netPeak);
                RenderSparkline(LineNetStroke, PolyNetArea, _netHistory, CanvasNetGraph, Math.Max(500.0, netPeak * 1.1), _netLinePoints, _netPolyPoints);
            }

            // 7. HARDWARE OVERVIEW & TITLEBAR BADGE
            if (hw != null)
            {
                TxtUptime.Text = string.Format("{0}: {1}", LocalizationManager.Get("UptimeLabel"), hw.UptimeDisplay);

                if (TxtHwSummaryBadge != null)
                {
                    string cpuShort = !string.IsNullOrEmpty(hw.CpuModel) ? hw.CpuModel : "CPU";
                    if (cpuShort.Length > 20) cpuShort = cpuShort.Substring(0, 18) + "..";
                    TxtHwSummaryBadge.Text = string.Format("💻 {0} • {1:N0} GB", cpuShort, mem != null ? mem.TotalGB : 16.0);
                }

                if (BorderHwSummary != null)
                {
                    string activeScheme = "Balanced";
                    PowerPlanManager.GetActiveScheme(out activeScheme);

                    BorderHwSummary.ToolTip = string.Format(
                        "🔧 Especificaciones del Equipo:\n• CPU: {0}\n• GPU: {1}\n• SO: {2}\n• RAM: {3:N1} GB\n• Alimentación: {4}\n• Plan Activo: {5}",
                        hw.CpuModel,
                        !string.IsNullOrEmpty(hw.GpuModel) ? hw.GpuModel : (gpu != null ? gpu.Name : "GPU"),
                        hw.OsName,
                        mem != null ? mem.TotalGB : 16.0,
                        hw.PowerSource,
                        activeScheme
                    );
                }
            }

            // 8. PROCESSES, SERVICES, TASKS, STARTUP (periodic)
            if (procs != null)
            {
                ListProcesses.ItemsSource = procs;

                // Inspect unresponsive processes for alert banner
                var hungProcesses = procs.FindAll(p => !p.IsResponding);
                if (hungProcesses.Count > 0)
                {
                    BorderUnresponsiveAlert.Visibility = Visibility.Visible;
                    TxtUnresponsiveAlert.Text = string.Format(
                        LocalizationManager.CurrentLanguage == "es" ? "⚠️ {0} Proceso Colgado" : "⚠️ {0} Hung Process",
                        hungProcesses.Count
                    );
                }
                else
                {
                    BorderUnresponsiveAlert.Visibility = Visibility.Collapsed;
                }
            }
            if (svc != null && svc.CriticalServices != null)
            {
                ListServices.ItemsSource = svc.CriticalServices;
            }
            if (tasks != null)
            {
                ListTasks.ItemsSource = tasks;
            }
            if (startup != null)
            {
                ListStartup.ItemsSource = startup;
            }
        }

        // =========================================================================
        // TABBED NAVIGATION CONTROLLER
        // =========================================================================

        private void TabBtnProcesses_Click(object sender, RoutedEventArgs e)
        {
            ShowTab(ViewProcesses, TabBtnProcesses);
        }

        private void TabBtnAccelerators_Click(object sender, RoutedEventArgs e)
        {
            ShowTab(ViewAccelerators, TabBtnAccelerators);
        }

        private void TabBtnServices_Click(object sender, RoutedEventArgs e)
        {
            ShowTab(ViewServices, TabBtnServices);
        }

        private void TabBtnTasks_Click(object sender, RoutedEventArgs e)
        {
            ShowTab(ViewTasks, TabBtnTasks);
        }

        private void TabBtnStartup_Click(object sender, RoutedEventArgs e)
        {
            ShowTab(ViewStartup, TabBtnStartup);
        }

        private void TabBtnDrives_Click(object sender, RoutedEventArgs e)
        {
            ShowTab(ViewDrives, TabBtnDrives);
        }

        private void ShowTab(Grid tabView, Button activeBtn)
        {
            if (ViewProcesses != null) ViewProcesses.Visibility = Visibility.Collapsed;
            if (ViewAccelerators != null) ViewAccelerators.Visibility = Visibility.Collapsed;
            if (ViewServices != null) ViewServices.Visibility = Visibility.Collapsed;
            if (ViewTasks != null) ViewTasks.Visibility = Visibility.Collapsed;
            if (ViewStartup != null) ViewStartup.Visibility = Visibility.Collapsed;
            if (ViewDrives != null) ViewDrives.Visibility = Visibility.Collapsed;

            if (tabView != null) tabView.Visibility = Visibility.Visible;
            UpdateActiveTabHighlight(activeBtn);
        }

        private void UpdateActiveTabHighlight(Button activeBtn)
        {
            var defaultStyle = (Style)FindResource("TabHeaderButtonStyle");
            var activeStyle = (Style)FindResource("ActiveTabHeaderButtonStyle");

            if (TabBtnProcesses != null) TabBtnProcesses.Style = defaultStyle;
            if (TabBtnAccelerators != null) TabBtnAccelerators.Style = defaultStyle;
            if (TabBtnServices != null) TabBtnServices.Style = defaultStyle;
            if (TabBtnTasks != null) TabBtnTasks.Style = defaultStyle;
            if (TabBtnStartup != null) TabBtnStartup.Style = defaultStyle;
            if (TabBtnDrives != null) TabBtnDrives.Style = defaultStyle;

            if (activeBtn != null)
            {
                activeBtn.Style = activeStyle;
            }
        }

        // =========================================================================
        // ZERO-ALLOCATION SPARKLINE GRAPH RENDERING
        // =========================================================================

        private void RenderSparkline(
            Polyline line,
            Polygon poly,
            List<double> history,
            Canvas canvas,
            double maxVal,
            PointCollection linePts,
            PointCollection polyPts)
        {
            if (canvas == null || history == null || history.Count < 2) return;

            double width = canvas.ActualWidth > 10 ? canvas.ActualWidth : canvas.Width;
            double height = canvas.ActualHeight > 10 ? canvas.ActualHeight : canvas.Height;
            if (double.IsNaN(width) || width <= 0) width = 340.0;
            if (double.IsNaN(height) || height <= 0) height = 50.0;

            int count = history.Count;
            double stepX = width / Math.Max(1.0, count - 1);
            double effectiveMax = Math.Max(1.0, maxVal);

            linePts.Clear();
            polyPts.Clear();

            polyPts.Add(new Point(0, height));
            for (int i = 0; i < count; i++)
            {
                double normalized = Math.Max(0.0, Math.Min(1.0, history[i] / effectiveMax));
                double x = i * stepX;
                double y = height - (normalized * (height - 8.0)) - 4.0;
                var pt = new Point(Math.Round(x, 1), Math.Round(y, 1));
                linePts.Add(pt);
                polyPts.Add(pt);
            }
            polyPts.Add(new Point(width, height));

            line.Points = linePts;
            poly.Points = polyPts;
        }

        // =========================================================================
        // VIEW MODE SWITCHER & DYNAMIC WINDOW SIZING
        // =========================================================================

        private void BtnToggleView_Click(object sender, RoutedEventArgs e)
        {
            MenuViewModes.PlacementTarget = BtnToggleView;
            MenuViewModes.IsOpen = true;
        }

        private void MenuViewFull_Click(object sender, RoutedEventArgs e)
        {
            _config.ViewMode = "Full";
            ApplyViewMode(_config.ViewMode);
            ConfigManager.Save(_config);
            ShowToast(LocalizationManager.Get("MenuFullDesc"));
        }

        private void MenuViewHero_Click(object sender, RoutedEventArgs e)
        {
            _config.ViewMode = "Hero";
            ApplyViewMode(_config.ViewMode);
            ConfigManager.Save(_config);
            ShowToast(LocalizationManager.Get("MenuHeroDesc"));
        }

        private void MenuViewWidget_Click(object sender, RoutedEventArgs e)
        {
            _config.ViewMode = "Widget";
            ApplyViewMode(_config.ViewMode);
            ConfigManager.Save(_config);
            ShowToast(LocalizationManager.Get("MenuWidgetDesc"));
        }

        private void Widget_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && e.ChangedButton == MouseButton.Left)
            {
                MenuViewFull_Click(sender, e);
            }
        }

        private void MenuWidgetSnap_Click(object sender, RoutedEventArgs e)
        {
            WindowPlacementHelper.SnapToBottomRight(this);
            ShowToast(LocalizationManager.Get("ToastWidgetDocked"));
        }

        private void ApplyViewMode(string mode)
        {
            if (WindowState == WindowState.Maximized && (mode == "Hero" || mode == "Widget"))
            {
                WindowState = WindowState.Normal;
            }

            if (mode == "Widget")
            {
                // Remember Full/Hero bounds before shrinking
                if (ContainerMainView != null && ContainerMainView.Visibility == Visibility.Visible && Width > 460)
                {
                    _lastFullBounds = new Rect(Left, Top, Width, Height);
                }

                TxtViewMode.Text = LocalizationManager.Get("ViewWidget");

                // 1. Relax minimum constraints first
                MinWidth = 320;
                MinHeight = 80;
                MaxWidth = 460;
                MaxHeight = 120;

                // 2. Adjust visibility
                ContainerMainView.Visibility = Visibility.Collapsed;
                ContainerWidgetView.Visibility = Visibility.Visible;

                // 3. Set exact physical dimensions
                Width = 380;
                Height = 88;
                ResizeMode = ResizeMode.NoResize;

                // 4. Snap to bottom-right of current screen
                WindowPlacementHelper.SnapToBottomRight(this);
            }
            else if (mode == "Hero")
            {
                TxtViewMode.Text = LocalizationManager.Get("ViewHero");

                // 1. Set constraints
                MinWidth = 760;
                MinHeight = 320;
                MaxWidth = double.PositiveInfinity;
                MaxHeight = double.PositiveInfinity;

                // 2. Adjust visibility
                ContainerMainView.Visibility = Visibility.Visible;
                ContainerWidgetView.Visibility = Visibility.Collapsed;
                ContainerRibbon.Visibility = Visibility.Visible;
                ContainerHeroGrid.Visibility = Visibility.Visible;
                ContainerLiveWaves.Visibility = Visibility.Visible;
                ContainerDeepDive.Visibility = Visibility.Collapsed;
                ContainerFooter.Visibility = Visibility.Collapsed;

                // 3. Set exact physical dimensions and safe monitor clamp
                WindowPlacementHelper.ClampWindowToMonitor(this, 840, 360, _lastFullBounds);
                Width = 840;
                Height = 360;
                ResizeMode = ResizeMode.CanResize;
            }
            else
            {
                TxtViewMode.Text = LocalizationManager.Get("ViewFull");

                // 1. Set constraints
                MinWidth = 920;
                MinHeight = 620;
                MaxWidth = double.PositiveInfinity;
                MaxHeight = double.PositiveInfinity;

                // 2. Adjust visibility
                ContainerMainView.Visibility = Visibility.Visible;
                ContainerWidgetView.Visibility = Visibility.Collapsed;
                ContainerRibbon.Visibility = Visibility.Visible;
                ContainerHeroGrid.Visibility = Visibility.Visible;
                ContainerLiveWaves.Visibility = Visibility.Visible;
                ContainerDeepDive.Visibility = Visibility.Visible;
                ContainerFooter.Visibility = Visibility.Visible;
// 3. Set exact physical dimensions and safe monitor clamp
                WindowPlacementHelper.ClampWindowToMonitor(this, 1040, 720, _lastFullBounds);
                Width = 1040;
                Height = 720;
                ResizeMode = ResizeMode.CanResize;
            }
        }

        // =========================================================================
        // LANGUAGE LOCALIZATION CONTROLLER (ES / EN)
        // =========================================================================

        private void BtnToggleLang_Click(object sender, RoutedEventArgs e)
        {
            MenuLanguages.PlacementTarget = BtnToggleLang;
            MenuLanguages.IsOpen = true;
        }

        private void MenuLangEs_Click(object sender, RoutedEventArgs e)
        {
            SetLanguage("es");
        }

        private void MenuLangEn_Click(object sender, RoutedEventArgs e)
        {
            SetLanguage("en");
        }

        private void SetLanguage(string lang)
        {
            _config.Language = lang;
            ApplyLanguage(lang);
            ConfigManager.Save(_config);
            ShowToast(lang == "es" ? "Idioma: Español" : "Language: English");
        }

        private void ApplyLanguage(string lang)
        {
            if (string.IsNullOrEmpty(lang)) lang = "es";
            _config.Language = lang;
            LocalizationManager.CurrentLanguage = lang;

            if (TxtCurrentLang != null) TxtCurrentLang.Text = lang.ToUpper();

            // Ribbon & Actions
            if (TxtBtnTurboMode != null) TxtBtnTurboMode.Text = LocalizationManager.Get("TurboMode", "Modo Turbo");
            if (BtnTurboMode != null) BtnTurboMode.ToolTip = LocalizationManager.Get("TurboModeTooltip", "1-Clic: Activa Modo Turbo (Alto Rendimiento + Purga de RAM)");
            if (TxtBtnOptimize != null) TxtBtnOptimize.Text = LocalizationManager.Get("TrimRam");
            if (BtnOptimize != null) BtnOptimize.ToolTip = LocalizationManager.Get("TrimRamTooltip");
            if (TxtBtnCleanTemp != null) TxtBtnCleanTemp.Text = LocalizationManager.Get("CleanDeep", "Limpieza Profunda");
            if (BtnCleanTemp != null) BtnCleanTemp.ToolTip = LocalizationManager.Get("CleanDeepTooltip", "Limpieza profunda de temporales, Windows Update y caché de navegadores");
            if (TxtBtnFlushDns != null) TxtBtnFlushDns.Text = LocalizationManager.Get("FlushDns", "Vaciar DNS");
            if (BtnFlushDns != null) BtnFlushDns.ToolTip = LocalizationManager.Get("FlushDnsTooltip", "Vaciar la caché del servicio de resolución DNS de Windows");
            if (TxtBtnSnapshot != null) TxtBtnSnapshot.Text = LocalizationManager.Get("Snapshot");
            if (BtnSnapshotTop != null) BtnSnapshotTop.ToolTip = LocalizationManager.Get("SnapshotTooltip");

            // Power Plans
            if (BtnPlanSaver != null) { BtnPlanSaver.Content = LocalizationManager.Get("PowerPlanSaver"); BtnPlanSaver.ToolTip = LocalizationManager.Get("PowerPlanSaverTooltip"); }
            if (BtnPlanBalanced != null) { BtnPlanBalanced.Content = LocalizationManager.Get("PowerPlanBalanced"); BtnPlanBalanced.ToolTip = LocalizationManager.Get("PowerPlanBalancedTooltip"); }
            if (BtnPlanHighPerf != null) { BtnPlanHighPerf.Content = LocalizationManager.Get("PowerPlanHighPerf"); BtnPlanHighPerf.ToolTip = LocalizationManager.Get("PowerPlanHighPerfTooltip"); }

            // Tools Menu
            if (TxtBtnTools != null) TxtBtnTools.Text = LocalizationManager.Get("Tools") + " ▾";
            if (BtnToolsMenu != null) BtnToolsMenu.ToolTip = LocalizationManager.Get("ToolsTooltip");
            if (MenuToolsTaskMgr != null) MenuToolsTaskMgr.Header = LocalizationManager.Get("ToolTaskMgr");
            if (MenuToolsResMon != null) MenuToolsResMon.Header = LocalizationManager.Get("ToolResMon");
            if (MenuToolsStorageSense != null) MenuToolsStorageSense.Header = LocalizationManager.Get("ToolStorageSense");
            if (MenuToolsServices != null) MenuToolsServices.Header = LocalizationManager.Get("ToolServices");

            // View Modes
            if (BtnToggleView != null) BtnToggleView.ToolTip = LocalizationManager.Get("ViewModeTooltip");
            if (MenuViewFullItem != null) MenuViewFullItem.Header = LocalizationManager.Get("MenuFullDesc");
            if (MenuViewHeroItem != null) MenuViewHeroItem.Header = LocalizationManager.Get("MenuHeroDesc");
            if (MenuViewWidgetItem != null) MenuViewWidgetItem.Header = LocalizationManager.Get("MenuWidgetDesc");
            if (TxtViewMode != null)
            {
                if (_config.ViewMode == "Widget") TxtViewMode.Text = LocalizationManager.Get("ViewWidget");
                else if (_config.ViewMode == "Hero") TxtViewMode.Text = LocalizationManager.Get("ViewHero");
                else TxtViewMode.Text = LocalizationManager.Get("ViewFull");
            }

            // Themes Menu
            if (BtnToggleTheme != null) BtnToggleTheme.ToolTip = LocalizationManager.Get("ThemeTooltip");
            if (MenuThemeDarkItem != null) MenuThemeDarkItem.Header = LocalizationManager.Get("ThemeDark");
            if (MenuThemeLightItem != null) MenuThemeLightItem.Header = LocalizationManager.Get("ThemeLight");
            if (MenuThemeNeonItem != null) MenuThemeNeonItem.Header = LocalizationManager.Get("ThemeNeon");
            if (MenuThemeRoseItem != null) MenuThemeRoseItem.Header = LocalizationManager.Get("ThemeRose");

            // Language, Interval & Pin
            if (BtnToggleLang != null) BtnToggleLang.ToolTip = LocalizationManager.Get("LangTooltip");
            if (BtnToggleInterval != null) BtnToggleInterval.ToolTip = LocalizationManager.Get("IntervalTooltip");
            if (BtnPinTop != null) BtnPinTop.ToolTip = Topmost ? LocalizationManager.Get("Unpin") : LocalizationManager.Get("PinAlwaysOnTop");
            if (BorderUptime != null) BorderUptime.ToolTip = LocalizationManager.Get("UptimeTooltip");

            // Bento Cards
            if (TxtCardCpuTitle != null) TxtCardCpuTitle.Text = LocalizationManager.Get("CardCpuTitle");
            if (TxtCardGpuTitle != null) TxtCardGpuTitle.Text = LocalizationManager.Get("CardGpuTitle");
            if (TxtCardNpuTitle != null) TxtCardNpuTitle.Text = LocalizationManager.Get("CardNpuTitle");
            if (TxtCardRamTitle != null) TxtCardRamTitle.Text = LocalizationManager.Get("CardRamTitle");
            if (TxtCardDiskTitle != null) TxtCardDiskTitle.Text = LocalizationManager.Get("CardDiskTitle");
            if (TxtCardNetTitle != null) TxtCardNetTitle.Text = LocalizationManager.Get("CardNetTitle");

            // Live Wave Sparklines
            if (TxtWaveCpuTitle != null) TxtWaveCpuTitle.Text = LocalizationManager.Get("WaveCpuTitle");
            if (TxtWaveNetTitle != null) TxtWaveNetTitle.Text = LocalizationManager.Get("WaveNetTitle");

            // Deep Dive Tabs
            if (TabBtnProcesses != null) TabBtnProcesses.Content = LocalizationManager.Get("TabProcesses");
            if (TabBtnAccelerators != null) TabBtnAccelerators.Content = LocalizationManager.Get("TabAccelerators");
            if (TabBtnServices != null) TabBtnServices.Content = LocalizationManager.Get("TabServices");
            if (TabBtnTasks != null) TabBtnTasks.Content = LocalizationManager.Get("TabTasks");
            if (TabBtnStartup != null) TabBtnStartup.Content = LocalizationManager.Get("TabStartup");
            if (TabBtnDrives != null) TabBtnDrives.Content = LocalizationManager.Get("TabDrives", "💾 Discos & Almacenamiento");

            // Table & Column Headers
            if (TxtColPid != null) TxtColPid.Text = LocalizationManager.Get("ColPid", "PID");
            if (TxtColApp != null) TxtColApp.Text = LocalizationManager.Get("ColApp", "APLICACIÓN");
            if (TxtColCpu != null) TxtColCpu.Text = LocalizationManager.Get("ColCpuPercent", "CPU %");
            if (TxtColWorkingSet != null) TxtColWorkingSet.Text = LocalizationManager.Get("ColWorkingSet", "MEMORIA");
            if (TxtColRamPercent != null) TxtColRamPercent.Text = LocalizationManager.Get("ColRamPercent", "% RAM");
            if (TxtColState != null) TxtColState.Text = LocalizationManager.Get("ColStatus", "ESTADO");
            if (TxtColActions != null) TxtColActions.Text = LocalizationManager.Get("ColActions", "ACCIONES");

            if (BtnSortCpu != null) BtnSortCpu.Content = LocalizationManager.Get("SortByCpu", "⚡ CPU %");
            if (BtnSortRam != null) BtnSortRam.Content = LocalizationManager.Get("SortByRam", "🧠 RAM MB");
            if (BtnResumeAll != null) BtnResumeAll.Content = LocalizationManager.Get("ResumeAll", "▶ Reanudar Todos");
            if (TxtSearchPlaceholder != null) TxtSearchPlaceholder.Text = LocalizationManager.Get("SearchProcesses", "🔍 Buscar proceso por nombre o PID...");

            if (TxtColServiceName != null) TxtColServiceName.Text = LocalizationManager.Get("ColServiceName", "SERVICIO DE WINDOWS");
            if (TxtColServiceStatus != null) TxtColServiceStatus.Text = LocalizationManager.Get("ColServiceStatus", "ESTADO");

            if (TxtColTaskName != null) TxtColTaskName.Text = LocalizationManager.Get("ColTaskName", "TAREA PROGRAMADA");
            if (TxtColTaskState != null) TxtColTaskState.Text = LocalizationManager.Get("ColTaskState", "ESTADO");

            if (TxtColStartupApp != null) TxtColStartupApp.Text = LocalizationManager.Get("ColStartupApp", "APLICACIÓN / PROGRAMA");
            if (TxtColStartupLocation != null) TxtColStartupLocation.Text = LocalizationManager.Get("ColStartupLocation", "ORIGEN");
            if (TxtColStartupStatus != null) TxtColStartupStatus.Text = LocalizationManager.Get("ColStartupStatus", "ESTADO");
            if (TxtColStartupActions != null) TxtColStartupActions.Text = LocalizationManager.Get("ColStartupActions", "ACCIONES");

            // Accelerators Tab
            if (TxtGpuDeckTitle != null) TxtGpuDeckTitle.Text = LocalizationManager.Get("GpuDeckTitle");
            if (TxtGpu3DTitle != null) TxtGpu3DTitle.Text = LocalizationManager.Get("Gpu3DRendering");
            if (TxtGpuComputeTitle != null) TxtGpuComputeTitle.Text = LocalizationManager.Get("GpuComputeML");
            if (TxtGpuVideoTitle != null) TxtGpuVideoTitle.Text = LocalizationManager.Get("GpuVideoDecode");
            if (TxtGpuCopyTitle != null) TxtGpuCopyTitle.Text = LocalizationManager.Get("GpuCopyEngine");
            if (TxtGpuVramTitle != null) TxtGpuVramTitle.Text = LocalizationManager.Get("GpuVramTitle");
            if (TxtNpuDeckTitle != null) TxtNpuDeckTitle.Text = LocalizationManager.Get("NpuDeckTitle");
            if (TxtNpuDeckDesc != null) TxtNpuDeckDesc.Text = LocalizationManager.Get("NpuDeckDesc");
            if (TxtNpuComputeTitle != null) TxtNpuComputeTitle.Text = LocalizationManager.Get("NpuComputeUtilization");

            // Widget View
            if (MenuWidgetRestore != null) MenuWidgetRestore.Header = LocalizationManager.Get("WidgetRestore");
            if (MenuWidgetHero != null) MenuWidgetHero.Header = LocalizationManager.Get("WidgetSwitchHero");
            if (MenuWidgetSnap != null) MenuWidgetSnap.Header = LocalizationManager.Get("WidgetSnapBottomRight");
            if (MenuWidgetPin != null) MenuWidgetPin.Header = LocalizationManager.Get("WidgetPinAlways");
            if (MenuWidgetClose != null) MenuWidgetClose.Header = LocalizationManager.Get("WidgetClose");
            if (ContainerWidgetView != null) ContainerWidgetView.ToolTip = LocalizationManager.Get("WidgetTooltip");
            if (BtnWidgetExpand != null) BtnWidgetExpand.ToolTip = LocalizationManager.Get("WidgetRestore");
            if (BtnWidgetPin != null) BtnWidgetPin.ToolTip = LocalizationManager.Get("PinAlwaysOnTop");
            if (BtnWidgetClose != null) BtnWidgetClose.ToolTip = LocalizationManager.Get("Close");
        }

        // =========================================================================
        // INTERACTIVE BENTO CARD CLICKS
        // =========================================================================

        private void CardCpu_Click(object sender, MouseButtonEventArgs e)
        {
            ShowTab(ViewProcesses, TabBtnProcesses);
            _sortByCpu = true;
            UpdateSortButtonsHighlight();
            ApplyProcessSortingFast();
            ShowToast("⚡ Filtrando procesos por mayor uso de CPU");
        }

        private void CardGpu_Click(object sender, MouseButtonEventArgs e)
        {
            ShowTab(ViewAccelerators, TabBtnAccelerators);
            ShowToast("⚡ Panel de Aceleradores Gráficos y Motores 3D");
        }

        private void CardNpu_Click(object sender, MouseButtonEventArgs e)
        {
            ShowTab(ViewAccelerators, TabBtnAccelerators);
            ShowToast("⚡ Diagnóstico del Motor Neural de IA (NPU)");
        }

        private void CardRam_Click(object sender, MouseButtonEventArgs e)
        {
            ShowTab(ViewProcesses, TabBtnProcesses);
            _sortByCpu = false;
            UpdateSortButtonsHighlight();
            ApplyProcessSortingFast();
            ShowToast("🧠 Filtrando procesos por mayor uso de memoria RAM");
        }

        private void CardDisk_Click(object sender, MouseButtonEventArgs e)
        {
            ShowTab(ViewDrives, TabBtnDrives);
            ShowToast("💾 Unidades de Almacenamiento y Limpieza");
        }

        private void CardNet_Click(object sender, MouseButtonEventArgs e)
        {
            ShowToast(string.Format("🌐 Red: Ping {0} | Descarga: {1}", _lastNet != null ? _lastNet.PingDisplay : "--", _lastNet != null ? _lastNet.DownloadDisplay : "--"));
        }

        // =========================================================================
        // QUICK ACTIONS (TURBO, FLUSH DNS, TRIM RAM, CLEAN TEMP, SNAPSHOT)
        // =========================================================================

        private void BtnTurboMode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 1. Switch to High Performance power scheme
                PowerPlanManager.SetScheme(PowerSchemeMode.HighPerformance);
                UpdatePowerButtonsHighlight("High Performance");

                // 2. Trim memory working sets
                int trimmedCount;
                double freedMB = MemoryOptimizer.OptimizeWorkingSet(out trimmedCount);

                ShowToast(string.Format("🚀 Modo Turbo Activado! (Alto Rendimiento + {0:N0} MB RAM liberada en {1} apps)", freedMB, trimmedCount));
            }
            catch (Exception ex)
            {
                ShowToast("Error en Modo Turbo: " + ex.Message);
            }
        }

        private void BtnFlushDns_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                bool ok = NetworkCollector.FlushDnsCache();
                if (ok)
                {
                    ShowToast(LocalizationManager.Get("ToastDnsFlushed", "🌐 Caché de resolución DNS de Windows vaciada exitosamente!"));
                }
                else
                {
                    ShowToast("Error al vaciar DNS");
                }
            }
            catch (Exception ex)
            {
                ShowToast("Error al vaciar DNS: " + ex.Message);
            }
        }

        private void BtnRescueUnresponsive_Click(object sender, RoutedEventArgs e)
        {
            if (_lastProcs == null) return;
            var hung = _lastProcs.FindAll(p => !p.IsResponding);
            if (hung.Count == 0)
            {
                BorderUnresponsiveAlert.Visibility = Visibility.Collapsed;
                ShowToast("Todos los procesos están respondiendo normalmente.");
                return;
            }

            var hungProc = hung[0];
            var result = MessageBox.Show(
                string.Format("El proceso '{0}' (PID: {1}) no responde a eventos del sistema operativo.\n\n¿Deseas forzar su cierre de forma segura?", hungProc.Name, hungProc.Id),
                "Rescatar Proceso Colgado",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );

            if (result == MessageBoxResult.Yes)
            {
                KillProcess(hungProc.Id, hungProc.Name);
            }
        }

        private void BtnOptimize_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int trimmedCount;
                double freedMB = MemoryOptimizer.OptimizeWorkingSet(out trimmedCount);
                ShowToast(string.Format("⚡ RAM Optimizada! ({0:N0} MB liberados en {1} procesos)", freedMB, trimmedCount));
            }
            catch (Exception ex)
            {
                ShowToast("Error trimming RAM: " + ex.Message);
            }
        }

        private async void BtnCleanTemp_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ShowToast("🧹 Ejecutando limpieza profunda de temporales y cachés...");
                var res = await Task.Run(() => SafeTempCleaner.CleanDeepStorage());

                string msg = string.Format(
                    "🧹 Limpieza Completada: {0} ({1} archivos eliminados)",
                    res.HumanSize,
                    res.FilesDeleted
                );
                if (res.SkippedReasons.Count > 0)
                {
                    msg += string.Format(" • {0} bloqueados protegidos", res.SkippedReasons.Count);
                }
                ShowToast(msg);
            }
            catch (Exception ex)
            {
                ShowToast("Error en limpieza profunda: " + ex.Message);
            }
        }

        private void BtnSnapshot_Click(object sender, RoutedEventArgs e)
        {
            bool ok = SnapshotExporter.CopySnapshotToClipboard(
                _lastCpu,
                _lastGpu,
                _lastNpu,
                _lastMem,
                _lastDisks,
                _lastNet,
                _lastHw,
                _lastProcs,
                _lastSvc
            );

            if (ok)
            {
                ShowToast("📸 Snapshot de diagnóstico copiado al portapapeles en Markdown!");
            }
            else
            {
                ShowToast("Failed to copy snapshot");
            }
        }

        private void ShowToast(string message)
        {
            if (TxtStatusNotification != null)
            {
                TxtStatusNotification.Text = message;
                TxtStatusNotification.Foreground = (Brush)FindResource("AccentCpu");
            }

            if (_toastTimer == null)
            {
                _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                _toastTimer.Tick += (s, ev) =>
                {
                    _toastTimer.Stop();
                    if (TxtStatusNotification != null)
                    {
                        TxtStatusNotification.Text = "⚡ Telemetría Win32 Activa • 0% CPU Overhead";
                        TxtStatusNotification.Foreground = (Brush)FindResource("TextSecondary");
                    }
                };
            }
            _toastTimer.Stop();
            _toastTimer.Start();
        }

        // =========================================================================
        // POWER PLAN CONTROLLER
        // =========================================================================

        private void BtnPlanSaver_Click(object sender, RoutedEventArgs e)
        {
            SetPowerPlan(PowerSchemeMode.PowerSaver, "Power Saver");
        }

        private void BtnPlanBalanced_Click(object sender, RoutedEventArgs e)
        {
            SetPowerPlan(PowerSchemeMode.Balanced, "Balanced");
        }

        private void BtnPlanHighPerf_Click(object sender, RoutedEventArgs e)
        {
            SetPowerPlan(PowerSchemeMode.HighPerformance, "High Performance");
        }

        private void SetPowerPlan(PowerSchemeMode mode, string name)
        {
            try
            {
                if (PowerPlanManager.SetScheme(mode))
                {
                    UpdatePowerButtonsHighlight(name);
                    ShowToast("⚡ Plan de Energía: " + name);
                }
            }
            catch (Exception ex)
            {
                ShowToast("Error switching plan: " + ex.Message);
            }
        }

        private void UpdatePowerButtonsHighlight(string scheme)
        {
            var activeBrush = (Brush)FindResource("BgControlActive");
            var normalBrush = Brushes.Transparent;

            BtnPlanSaver.Background = scheme.IndexOf("Saver", StringComparison.OrdinalIgnoreCase) >= 0 ? activeBrush : normalBrush;
            BtnPlanBalanced.Background = scheme.IndexOf("Balanced", StringComparison.OrdinalIgnoreCase) >= 0 ? activeBrush : normalBrush;
            BtnPlanHighPerf.Background = (scheme.IndexOf("High", StringComparison.OrdinalIgnoreCase) >= 0 || scheme.IndexOf("Ultimate", StringComparison.OrdinalIgnoreCase) >= 0) ? activeBrush : normalBrush;
        }

        // =========================================================================
        // TOOLS & UTILITY LAUNCHERS
        // =========================================================================

        private void BtnToolsMenu_Click(object sender, RoutedEventArgs e)
        {
            MenuWindowsTools.PlacementTarget = BtnToolsMenu;
            MenuWindowsTools.IsOpen = true;
        }

        private void BtnTaskMgr_Click(object sender, RoutedEventArgs e)
        {
            try { Process.Start("taskmgr.exe"); } catch { }
        }

        private void BtnResMon_Click(object sender, RoutedEventArgs e)
        {
            try { Process.Start("resmon.exe"); } catch { }
        }

        private void BtnPCMgr_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("ms-settings:storagesense") { UseShellExecute = true });
            }
            catch
            {
                try { Process.Start("cleanmgr.exe"); } catch { }
            }
        }

        private void MenuOpenServices_Click(object sender, RoutedEventArgs e)
        {
            try { Process.Start("services.msc"); } catch { }
        }

        // =========================================================================
        // THEME CONTROLLER (DARK / LIGHT / NEON / ROSE)
        // =========================================================================

        private void BtnToggleTheme_Click(object sender, RoutedEventArgs e)
        {
            MenuThemes.PlacementTarget = BtnToggleTheme;
            MenuThemes.IsOpen = true;
        }

        private void MenuThemeDark_Click(object sender, RoutedEventArgs e)
        {
            SetTheme("Dark");
        }

        private void MenuThemeLight_Click(object sender, RoutedEventArgs e)
        {
            SetTheme("Light");
        }

        private void MenuThemeNeon_Click(object sender, RoutedEventArgs e)
        {
            SetTheme("Neon");
        }

        private void MenuThemeRose_Click(object sender, RoutedEventArgs e)
        {
            SetTheme("Rose");
        }

        private void SetTheme(string themeName)
        {
            _config.Theme = themeName;
            ApplyTheme(themeName);
            ConfigManager.Save(_config);
            ShowToast("🎨 Tema: " + themeName);
        }

        private void ApplyTheme(string themeName)
        {
            try
            {
                App.SetTheme(themeName);
                if (TxtCurrentTheme != null)
                {
                    TxtCurrentTheme.Text = themeName;
                }
                UpdateSortButtonsHighlight();
            }
            catch { }
        }

        private void BtnToggleInterval_Click(object sender, RoutedEventArgs e)
        {
            _config.RefreshIntervalSeconds = _config.RefreshIntervalSeconds == 3 ? 5 : 3;
            TxtInterval.Text = string.Format("{0}s", _config.RefreshIntervalSeconds);
            ConfigManager.Save(_config);
            ShowToast(string.Format("Interval: {0}s", _config.RefreshIntervalSeconds));
        }

        // =========================================================================
        // WINDOW CAPTION CONTROLS & PINNING
        // =========================================================================

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                e.Handled = true;
                BtnMaximize_Click(sender, e);
                return;
            }

            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try { DragMove(); } catch { }
            }
        }

        private void BtnPinTop_Click(object sender, RoutedEventArgs e)
        {
            Topmost = !Topmost;
            _config.AlwaysOnTop = Topmost;
            ConfigManager.Save(_config);

            var pinBrush = Topmost ? (Brush)FindResource("AccentCpu") : (Brush)FindResource("TextMuted");
            PathPin.Fill = pinBrush;
            PathWidgetPin.Fill = pinBrush;

            ShowToast(Topmost ? LocalizationManager.Get("ToastPinned") : LocalizationManager.Get("ToastUnpinned"));
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            if (_config.MinimizeToTray)
            {
                HideToTray();
            }
            else
            {
                WindowState = WindowState.Minimized;
            }
        }

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
            }
            else
            {
                WindowState = WindowState.Maximized;
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            if (_config.CloseToTray && !_isExiting)
            {
                HideToTray();
            }
            else
            {
                _isExiting = true;
                Close();
            }
        }

        // =========================================================================
        // TRAY & SETTINGS MANAGEMENT
        // =========================================================================

        private void HideToTray()
        {
            _isTrayMode = true;
            Hide();
            _trayManager.ShowBalloon("Simple PC Monitor", "Ejecutándose en segundo plano en la bandeja del sistema.", 2000);
        }

        private void RestoreFromTray()
        {
            _isTrayMode = false;
            Show();
            WindowState = _lastWindowState == WindowState.Minimized ? WindowState.Normal : _lastWindowState;
            Activate();
        }

        private void OpenTrayContextMenu(IntPtr hwnd)
        {
            var menu = FindResource("TrayContextMenu") as ContextMenu;
            if (menu != null)
            {
                NativeMethods.SetForegroundWindow(hwnd);
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                menu.IsOpen = true;
            }
        }

        private void TrayMenuRestore_Click(object sender, RoutedEventArgs e)
        {
            RestoreFromTray();
        }

        private void TrayMenuExit_Click(object sender, RoutedEventArgs e)
        {
            _isExiting = true;
            Close();
        }

        private void TrayMenuMinToTray_Click(object sender, RoutedEventArgs e)
        {
            var mi = sender as MenuItem;
            if (mi != null)
            {
                _config.MinimizeToTray = mi.IsChecked;
                ConfigManager.Save(_config);
                UpdateSettingsMenuItemsState();
                ShowToast(mi.IsChecked ? "Minimizar a la bandeja activado" : "Minimizar a la bandeja desactivado");
            }
        }

        private void TrayMenuCloseToTray_Click(object sender, RoutedEventArgs e)
        {
            var mi = sender as MenuItem;
            if (mi != null)
            {
                _config.CloseToTray = mi.IsChecked;
                ConfigManager.Save(_config);
                UpdateSettingsMenuItemsState();
                ShowToast(mi.IsChecked ? "Cerrar a la bandeja activado" : "Cerrar a la bandeja desactivado");
            }
        }

        private void TrayMenuRunAtStartup_Click(object sender, RoutedEventArgs e)
        {
            var mi = sender as MenuItem;
            if (mi != null)
            {
                bool newState = mi.IsChecked;
                StartupHelper.SetRunAtStartup(newState, true);
                _config.RunAtStartup = newState;
                ConfigManager.Save(_config);
                UpdateSettingsMenuItemsState();
                ShowToast(newState ? "🚀 Inicio con Windows activado" : "Inicio automático desactivado");
            }
        }

        private void BtnSettingsMenu_Click(object sender, RoutedEventArgs e)
        {
            UpdateSettingsMenuItemsState();
            MenuAppSettings.PlacementTarget = BtnSettingsMenu;
            MenuAppSettings.IsOpen = true;
        }

        private void UpdateSettingsMenuItemsState()
        {
            if (MenuSettingMinToTray != null) MenuSettingMinToTray.IsChecked = _config.MinimizeToTray;
            if (MenuSettingCloseToTray != null) MenuSettingCloseToTray.IsChecked = _config.CloseToTray;
            if (MenuSettingStartup != null) MenuSettingStartup.IsChecked = StartupHelper.IsRunAtStartupEnabled();
            if (MenuSettingAlwaysOnTop != null) MenuSettingAlwaysOnTop.IsChecked = Topmost;
        }

        // =========================================================================
        // PROCESS SEARCH, SORTING & ACTION CONTROLLERS
        // =========================================================================

        private void TxtSearchProcess_TextChanged(object sender, TextChangedEventArgs e)
        {
            _procSearchQuery = TxtSearchProcess.Text != null ? TxtSearchProcess.Text.Trim() : string.Empty;
            if (TxtSearchPlaceholder != null)
            {
                TxtSearchPlaceholder.Visibility = string.IsNullOrEmpty(_procSearchQuery) ? Visibility.Visible : Visibility.Collapsed;
            }

            if (_searchDebounceTimer != null)
            {
                _searchDebounceTimer.Stop();
                _searchDebounceTimer.Start();
            }
        }

        private void BtnSortCpu_Click(object sender, RoutedEventArgs e)
        {
            _sortByCpu = true;
            UpdateSortButtonsHighlight();
            ApplyProcessSortingFast();
            ShowToast("⚡ Ordenado por mayor uso de CPU");
        }

        private void BtnSortRam_Click(object sender, RoutedEventArgs e)
        {
            _sortByCpu = false;
            UpdateSortButtonsHighlight();
            ApplyProcessSortingFast();
            ShowToast("🧠 Ordenado por mayor uso de RAM");
        }

        private void UpdateSortButtonsHighlight()
        {
            try
            {
                var activeStyle = TryFindResource("ActivePillActionButtonStyle") as Style;
                var defaultStyle = TryFindResource("PillActionButtonStyle") as Style;

                if (BtnSortCpu != null) BtnSortCpu.Style = _sortByCpu ? (activeStyle ?? defaultStyle) : defaultStyle;
                if (BtnSortRam != null) BtnSortRam.Style = !_sortByCpu ? (activeStyle ?? defaultStyle) : defaultStyle;
            }
            catch { }
        }

        private void ApplyProcessSortingFast()
        {
            try
            {
                if (_lastProcs == null || _lastProcs.Count == 0)
                {
                    RefreshProcessListManually();
                    return;
                }

                var query = _lastProcs.AsEnumerable();
                if (!string.IsNullOrEmpty(_procSearchQuery))
                {
                    query = query.Where(x =>
                        x.Name.IndexOf(_procSearchQuery, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (x.FriendlyName != null && x.FriendlyName.IndexOf(_procSearchQuery, StringComparison.OrdinalIgnoreCase) >= 0) ||
                        x.Id.ToString().Contains(_procSearchQuery)
                    );
                }

                var sorted = _sortByCpu
                    ? query.OrderByDescending(x => x.CpuPercent).ThenByDescending(x => x.MemoryMB).ToList()
                    : query.OrderByDescending(x => x.MemoryMB).ThenByDescending(x => x.CpuPercent).ToList();

                ListProcesses.ItemsSource = sorted;
            }
            catch
            {
                RefreshProcessListManually();
            }
        }

        private void RefreshProcessListManually()
        {
            try
            {
                var procs = _proc.Sample(15, _lastMem != null ? _lastMem.TotalGB : 16.0, _sortByCpu, _procSearchQuery);
                _lastProcs = procs;
                ListProcesses.ItemsSource = procs;
            }
            catch { }
        }

        private void BtnResumeAll_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int count = ProcessManager.ResumeAllSuspended();
                ShowToast(string.Format("▶ Se reanudaron {0} procesos suspendidos", count));
                RefreshProcessListManually();
            }
            catch (Exception ex)
            {
                ShowToast("Error al reanudar procesos: " + ex.Message);
            }
        }

        private void BtnProcessSuspendToggle_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var proc = btn != null ? btn.Tag as ProcessMetric : null;
            if (proc == null) return;

            if (ProcessManager.IsSuspended(proc.Id))
            {
                bool ok = ProcessManager.ResumeProcess(proc.Id);
                ShowToast(ok ? string.Format("▶ Reanudado: {0}", proc.Name) : "No se pudo reanudar");
            }
            else
            {
                bool ok = ProcessManager.SuspendProcess(proc.Id);
                ShowToast(ok ? string.Format("⏸ Suspendido: {0}", proc.Name) : "No se puede suspender este proceso del sistema");
            }
            RefreshProcessListManually();
        }

        private void MenuProcessSuspend_Click(object sender, RoutedEventArgs e)
        {
            var mi = sender as MenuItem;
            var proc = mi != null ? mi.Tag as ProcessMetric : null;
            if (proc != null)
            {
                bool ok = ProcessManager.SuspendProcess(proc.Id);
                ShowToast(ok ? string.Format("⏸ Suspendido: {0}", proc.Name) : "No se puede suspender este proceso protegido");
                RefreshProcessListManually();
            }
        }

        private void MenuProcessResume_Click(object sender, RoutedEventArgs e)
        {
            var mi = sender as MenuItem;
            var proc = mi != null ? mi.Tag as ProcessMetric : null;
            if (proc != null)
            {
                bool ok = ProcessManager.ResumeProcess(proc.Id);
                ShowToast(ok ? string.Format("▶ Reanudado: {0}", proc.Name) : "No se pudo reanudar");
                RefreshProcessListManually();
            }
        }

        private void MenuProcessPriority_Click(object sender, RoutedEventArgs e)
        {
            var mi = sender as MenuItem;
            if (mi == null) return;

            string priorityName = mi.Tag as string;
            var contextMenu = mi.Parent as MenuItem;
            var rootMenu = mi.DataContext as ProcessMetric;
            if (rootMenu == null)
            {
                var context = mi.CommandParameter as ProcessMetric;
                rootMenu = context;
            }

            if (string.IsNullOrEmpty(priorityName)) return;

            ProcessPriorityClass priorityClass = ProcessPriorityClass.Normal;
            switch (priorityName)
            {
                case "RealTime": priorityClass = ProcessPriorityClass.RealTime; break;
                case "High": priorityClass = ProcessPriorityClass.High; break;
                case "AboveNormal": priorityClass = ProcessPriorityClass.AboveNormal; break;
                case "Normal": priorityClass = ProcessPriorityClass.Normal; break;
                case "BelowNormal": priorityClass = ProcessPriorityClass.BelowNormal; break;
                case "Idle": priorityClass = ProcessPriorityClass.Idle; break;
            }

            var item = sender as FrameworkElement;
            var targetProc = item != null ? item.DataContext as ProcessMetric : null;
            if (targetProc != null)
            {
                bool ok = ProcessManager.SetProcessPriority(targetProc.Id, priorityClass);
                ShowToast(ok ? string.Format("⚡ Prioridad de {0} cambiada a {1}", targetProc.Name, priorityName) : "No se pudo cambiar prioridad (acceso denegado)");
                RefreshProcessListManually();
            }
        }

        private void ProcessRow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                var border = sender as Border;
                if (border != null)
                {
                    var proc = border.Tag as ProcessMetric;
                    if (proc != null)
                    {
                        OpenProcessDetails(proc);
                    }
                }
            }
        }

        private void OpenProcessDetails(ProcessMetric metric)
        {
            if (metric == null) return;
            try
            {
                var win = new ProcessDetailsWindow(metric);
                win.Owner = this;
                win.ShowDialog();
            }
            catch (Exception ex)
            {
                ShowToast("Error abriendo detalles: " + ex.Message);
            }
        }

        private void BtnProcessDetails_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var proc = btn != null ? btn.Tag as ProcessMetric : null;
            if (proc != null) OpenProcessDetails(proc);
        }

        private void MenuProcessDetails_Click(object sender, RoutedEventArgs e)
        {
            var mi = sender as MenuItem;
            var proc = mi != null ? mi.Tag as ProcessMetric : null;
            if (proc != null) OpenProcessDetails(proc);
        }

        private void BtnProcessKill_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var proc = btn != null ? btn.Tag as ProcessMetric : null;
            if (proc != null) KillProcess(proc.Id, proc.Name);
        }

        private void MenuProcessKill_Click(object sender, RoutedEventArgs e)
        {
            var mi = sender as MenuItem;
            var proc = mi != null ? mi.Tag as ProcessMetric : null;
            if (proc != null) KillProcess(proc.Id, proc.Name);
        }

        private void MenuProcessSearch_Click(object sender, RoutedEventArgs e)
        {
            var mi = sender as MenuItem;
            var proc = mi != null ? mi.Tag as ProcessMetric : null;
            if (proc != null)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = string.Format("https://www.google.com/search?q={0}+process+windows", Uri.EscapeDataString(proc.Name)),
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }

        private void KillProcess(int pid, string name)
        {
            var result = MessageBox.Show(
                string.Format("¿Estás seguro de que deseas finalizar el proceso '{0}' (PID: {1})?", name, pid),
                "Finalizar Proceso",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    var p = Process.GetProcessById(pid);
                    p.Kill();
                    ShowToast("🔴 Proceso finalizado: " + name);
                    RefreshProcessListManually();
                }
                catch (Exception ex)
                {
                    ShowToast("No se pudo finalizar el proceso: " + ex.Message);
                }
            }
        }

        private void MenuProcessOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var item = sender as MenuItem;
            if (item != null)
            {
                var proc = item.Tag as ProcessMetric;
                if (proc != null && !string.IsNullOrEmpty(proc.ExecutablePath))
                {
                    try
                    {
                        Process.Start("explorer.exe", string.Format("/select,\"{0}\"", proc.ExecutablePath));
                    }
                    catch { }
                }
            }
        }

        // =========================================================================
        // DRIVES TAB ACTIONS
        // =========================================================================

        private void BtnDriveOpen_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null)
            {
                var disk = btn.Tag as DiskMetric;
                if (disk != null && !string.IsNullOrEmpty(disk.Name))
                {
                    try
                    {
                        Process.Start("explorer.exe", disk.Name);
                    }
                    catch { }
                }
            }
        }

        private void BtnDriveClean_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("ms-settings:storagesense") { UseShellExecute = true });
            }
            catch
            {
                try { Process.Start("cleanmgr.exe"); } catch { }
            }
        }

        // =========================================================================
        // SERVICES TAB ACTIONS
        // =========================================================================

        private void BtnServiceStart_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var svcItem = btn != null ? btn.Tag as ServiceItem : null;
            StartServiceAction(svcItem);
        }

        private void MenuServiceStart_Click(object sender, RoutedEventArgs e)
        {
            var mi = sender as MenuItem;
            var svcItem = mi != null ? mi.Tag as ServiceItem : null;
            StartServiceAction(svcItem);
        }

        private void StartServiceAction(ServiceItem svcItem)
        {
            if (svcItem == null) return;
            try
            {
                using (var sc = new ServiceController(svcItem.ServiceName))
                {
                    if (sc.Status != ServiceControllerStatus.Running && sc.Status != ServiceControllerStatus.StartPending)
                    {
                        sc.Start();
                        ShowToast(string.Format(LocalizationManager.Get("ToastServiceStarted"), svcItem.DisplayName));
                    }
                }
            }
            catch (Exception ex)
            {
                ShowToast("Error: " + ex.Message);
            }
        }

        private void BtnServiceStop_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var svcItem = btn != null ? btn.Tag as ServiceItem : null;
            StopServiceAction(svcItem);
        }

        private void MenuServiceStop_Click(object sender, RoutedEventArgs e)
        {
            var mi = sender as MenuItem;
            var svcItem = mi != null ? mi.Tag as ServiceItem : null;
            StopServiceAction(svcItem);
        }

        private void StopServiceAction(ServiceItem svcItem)
        {
            if (svcItem == null) return;
            try
            {
                using (var sc = new ServiceController(svcItem.ServiceName))
                {
                    if (sc.Status != ServiceControllerStatus.Stopped && sc.Status != ServiceControllerStatus.StopPending)
                    {
                        sc.Stop();
                        ShowToast(string.Format(LocalizationManager.Get("ToastServiceStopped"), svcItem.DisplayName));
                    }
                }
            }
            catch (Exception ex)
            {
                ShowToast("Error: " + ex.Message);
            }
        }

        private void BtnServiceRestart_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var svcItem = btn != null ? btn.Tag as ServiceItem : null;
            RestartServiceAction(svcItem);
        }

        private void MenuServiceRestart_Click(object sender, RoutedEventArgs e)
        {
            var mi = sender as MenuItem;
            var svcItem = mi != null ? mi.Tag as ServiceItem : null;
            RestartServiceAction(svcItem);
        }

        private void RestartServiceAction(ServiceItem svcItem)
        {
            if (svcItem == null) return;
            try
            {
                using (var sc = new ServiceController(svcItem.ServiceName))
                {
                    if (sc.Status == ServiceControllerStatus.Running)
                    {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(5));
                    }
                    sc.Start();
                    ShowToast(string.Format(LocalizationManager.Get("ToastServiceStarted"), svcItem.DisplayName));
                }
            }
            catch (Exception ex)
            {
                ShowToast("Error: " + ex.Message);
            }
        }

        private void MenuServiceSearch_Click(object sender, RoutedEventArgs e)
        {
            var mi = sender as MenuItem;
            var svcItem = mi != null ? mi.Tag as ServiceItem : null;
            if (svcItem == null) return;
            try
            {
                string url = "https://www.google.com/search?q=" + Uri.EscapeDataString(svcItem.ServiceName + " windows service");
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { }
        }

        private void MenuServiceCopy_Click(object sender, RoutedEventArgs e)
        {
            var mi = sender as MenuItem;
            var svcItem = mi != null ? mi.Tag as ServiceItem : null;
            if (svcItem != null)
            {
                try
                {
                    Clipboard.SetText(svcItem.ServiceName);
                    ShowToast("📋 " + svcItem.ServiceName);
                }
                catch { }
            }
        }

        // =========================================================================
        // TASKS TAB ACTIONS
        // =========================================================================

        private void MenuTaskRun_Click(object sender, RoutedEventArgs e)
        {
            var mi = sender as MenuItem;
            var task = mi != null ? mi.Tag as TaskItem : null;
            if (task == null) return;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = string.Format("/run /tn \"{0}\"", task.TaskPath),
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                ShowToast(string.Format(LocalizationManager.Get("ToastTaskExecuted"), task.TaskName));
            }
            catch (Exception ex)
            {
                ShowToast("Error: " + ex.Message);
            }
        }

        private void MenuTaskEnd_Click(object sender, RoutedEventArgs e)
        {
            var mi = sender as MenuItem;
            var task = mi != null ? mi.Tag as TaskItem : null;
            if (task == null) return;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = string.Format("/end /tn \"{0}\"", task.TaskPath),
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                ShowToast("⏹️ Tarea finalizada: " + task.TaskName);
            }
            catch (Exception ex)
            {
                ShowToast("Error: " + ex.Message);
            }
        }

        private void MenuOpenTaskSchd_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("taskschd.msc") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ShowToast("Error: " + ex.Message);
            }
        }

        private void MenuTaskSearch_Click(object sender, RoutedEventArgs e)
        {
            var mi = sender as MenuItem;
            var task = mi != null ? mi.Tag as TaskItem : null;
            if (task == null) return;
            try
            {
                string url = "https://www.google.com/search?q=" + Uri.EscapeDataString(task.TaskName + " windows scheduled task");
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { }
        }

        private void MenuTaskCopy_Click(object sender, RoutedEventArgs e)
        {
            var mi = sender as MenuItem;
            var task = mi != null ? mi.Tag as TaskItem : null;
            if (task != null)
            {
                try
                {
                    Clipboard.SetText(task.TaskPath);
                    ShowToast("📋 " + task.TaskPath);
                }
                catch { }
            }
        }

        // =========================================================================
        // STARTUP APPS TAB ACTIONS
        // =========================================================================

        private void BtnStartupFolder_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as FrameworkElement;
            var item = btn != null ? btn.Tag as StartupItem : null;
            OpenStartupItemLocation(item);
        }

        private void MenuStartupOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var mi = sender as MenuItem;
            var item = mi != null ? mi.Tag as StartupItem : null;
            OpenStartupItemLocation(item);
        }

        private void OpenStartupItemLocation(StartupItem item)
        {
            if (item == null) return;
            try
            {
                string target = item.ExecutablePath;
                if (!string.IsNullOrEmpty(target) && File.Exists(target))
                {
                    Process.Start("explorer.exe", string.Format("/select,\"{0}\"", target));
                }
                else if (!string.IsNullOrEmpty(target) && Directory.Exists(target))
                {
                    Process.Start("explorer.exe", string.Format("\"{0}\"", target));
                }
                else
                {
                    string folder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                    Process.Start("explorer.exe", folder);
                }
            }
            catch (Exception ex)
            {
                ShowToast("Error: " + ex.Message);
            }
        }

        private void BtnStartupSearch_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as FrameworkElement;
            var item = btn != null ? btn.Tag as StartupItem : null;
            SearchStartupItemOnline(item);
        }

        private void MenuStartupSearch_Click(object sender, RoutedEventArgs e)
        {
            var mi = sender as MenuItem;
            var item = mi != null ? mi.Tag as StartupItem : null;
            SearchStartupItemOnline(item);
        }

        private void SearchStartupItemOnline(StartupItem item)
        {
            if (item == null) return;
            try
            {
                string query = !string.IsNullOrEmpty(item.DisplayName) ? item.DisplayName : item.Name;
                string url = "https://www.google.com/search?q=" + Uri.EscapeDataString(query + " startup application windows");
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { }
        }

        private void MenuStartupCopyCmd_Click(object sender, RoutedEventArgs e)
        {
            var mi = sender as MenuItem;
            var item = mi != null ? mi.Tag as StartupItem : null;
            if (item != null)
            {
                try
                {
                    string textToCopy = !string.IsNullOrEmpty(item.ExecutablePath) ? item.ExecutablePath : item.Command;
                    Clipboard.SetText(textToCopy);
                    ShowToast(LocalizationManager.Get("ToastStartupCopied"));
                }
                catch { }
            }
        }

        private void MenuStartupSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true });
            }
            catch
            {
                ToolLauncher.StartTaskManager();
            }
        }
    }
}
