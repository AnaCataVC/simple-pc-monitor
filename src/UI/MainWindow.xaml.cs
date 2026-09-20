using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SystemCoreMonitor.Core;
using SystemCoreMonitor.Models;
using SystemCoreMonitor.Modules;
using SystemCoreMonitor.ViewModels;

namespace SystemCoreMonitor.UI
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private readonly CpuCollector _cpu = new();
        private readonly GpuCollector _gpu = new();
        private readonly NpuCollector _npu = new();
        private readonly WindowsAcceleratorEngine _accelEngine = new();
        private readonly MemoryCollector _mem = new();
        private readonly DiskCollector _disk = new();
        private readonly NetworkCollector _net = new();
        private readonly HardwareCollector _hw = new();
        private readonly ProcessCollector _proc = new();
        private readonly AiAgentCollector _ai = new();
        private readonly ServiceCollector _svc = new();
        private readonly StartupCollector _startup = new();

        private readonly DispatcherTimer _timer = new();
        private readonly TrayManager _trayManager = new();
        private AppConfig _config = new();

        private bool _isClosing = false;
        private bool _isSampling = false;
        private bool _isPinnedTop = false;

        public MainWindow()
        {
            InitializeComponent();

            _viewModel = new MainViewModel();
            DataContext = _viewModel;

            _config = ConfigManager.Load();
            App.SetTheme(_config.Theme);
            LocalizationManager.CurrentLanguage = _config.Language;

            WireViewModelEvents();

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;

            _timer.Interval = TimeSpan.FromSeconds(Math.Max(1, _config.RefreshIntervalSeconds));
            _timer.Tick += async (s, e) => await SampleTelemetryTickAsync();
        }

        private void WireViewModelEvents()
        {
            _viewModel.Dashboard.ShowToastRequested += ShowToast;
            _viewModel.Processes.ShowToastRequested += ShowToast;
            _viewModel.AiAgents.ShowToastRequested += ShowToast;
            _viewModel.Storage.ShowToastRequested += ShowToast;
            _viewModel.Services.ShowToastRequested += ShowToast;

            _viewModel.Settings.ThemeChanged += theme => App.SetTheme(theme);
            _viewModel.Settings.LanguageChanged += lang =>
            {
                LocalizationManager.CurrentLanguage = lang;
                ShowToast(lang == "es" ? "Idioma cambiado a Español" : "Language set to English");
            };
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            InitHwndHooks();
            InitAppBranding();
            InitTray();

            _timer.Start();
            _ = SampleTelemetryTickAsync();
        }

        private void InitHwndHooks()
        {
            var helper = new WindowInteropHelper(this);
            var source = HwndSource.FromHwnd(helper.Handle);
            source?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_GETMINMAXINFO)
            {
                WmGetMinMaxInfo(hwnd, lParam);
                handled = true;
            }
            return IntPtr.Zero;
        }

        private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
        {
            var mmi = Marshal.PtrToStructure<NativeMethods.MINMAXINFO>(lParam);
            IntPtr monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero)
            {
                var mi = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
                if (NativeMethods.GetMonitorInfo(monitor, ref mi))
                {
                    mmi.ptMaxPosition.X = Math.Abs(mi.rcWork.Left - mi.rcMonitor.Left);
                    mmi.ptMaxPosition.Y = Math.Abs(mi.rcWork.Top - mi.rcMonitor.Top);
                    mmi.ptMaxSize.X = Math.Abs(mi.rcWork.Right - mi.rcWork.Left);
                    mmi.ptMaxSize.Y = Math.Abs(mi.rcWork.Bottom - mi.rcWork.Top);
                }
            }
            Marshal.StructureToPtr(mmi, lParam, true);
        }

        private void InitAppBranding()
        {
            try
            {
                string pngPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.png");
                if (File.Exists(pngPath))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(pngPath, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    ImgAppLogo.Source = bmp;
                    ImgWidgetLogo.Source = bmp;
                }
            }
            catch { }

            var hw = _hw.Sample();
            TxtHwSummaryBadge.Text = $"{hw.CpuModel} • {hw.OsName}";
            TxtCpuCoresBadge.Text = $"{Environment.ProcessorCount} Cores";
        }

        private void InitTray()
        {
            var helper = new WindowInteropHelper(this);
            _trayManager.Initialize(helper.Handle, "System Core Monitor");
        }

        private async Task SampleTelemetryTickAsync()
        {
            if (_isSampling) return;
            _isSampling = true;

            try
            {
                var cpu = await Task.Run(() => _cpu.Sample());
                var mem = await Task.Run(() => _mem.Sample());
                var disk = await Task.Run(() => _disk.Sample());
                var net = await Task.Run(() => _net.Sample());
                var engineLoads = await Task.Run(() => _accelEngine.SampleAllEngines());
                var gpu = await Task.Run(() => _gpu.Sample(engineLoads));
                var npu = await Task.Run(() => _npu.Sample(engineLoads, gpu?.LuidString ?? ""));
                var procs = await Task.Run(() => _proc.Sample(50, mem?.TotalGB ?? 16.0));
                var ai = await Task.Run(() => _ai.Sample());
                var svc = await Task.Run(() => _svc.Sample());
                var startup = await Task.Run(() => _startup.Sample());
                var bloat = await Task.Run(() => BloatDetector.Scan());

                // Dispatch to ViewModels
                _viewModel.Dashboard.Update(cpu, mem, disk, net, gpu, npu);
                _viewModel.Processes.Update(procs);
                _viewModel.AiAgents.Update(ai);
                _viewModel.Accelerators.Update(gpu, npu);
                _viewModel.Storage.Update(disk, bloat);
                _viewModel.Services.Update(svc);
                _viewModel.Startup.Update(startup);

                // Update Widget UI
                TxtWidgetCpu.Text = $"{cpu.LoadPercent:F0}%";
                ProgressWidgetCpu.Value = cpu.LoadPercent;
                TxtWidgetRam.Text = $"{mem.LoadPercent:F0}%";
                ProgressWidgetRam.Value = mem.LoadPercent;
                TxtWidgetGpu.Text = $"{gpu.LoadPercent:F0}%";
                ProgressWidgetGpu.Value = gpu.LoadPercent;
                TxtWidgetNet.Text = net.DownloadDisplay;

                // Uptime
                var hw = _hw.Sample();
                TxtUptime.Text = $"Uptime: {hw.UptimeDisplay}";

                _trayManager.UpdateTooltip($"System Core Monitor\nCPU: {cpu.LoadPercent:F0}% | RAM: {mem.LoadPercent:F0}% | GPU: {gpu.LoadPercent:F0}%");
            }
            catch (Exception ex)
            {
                CrashLogger.LogException("MainWindow.SampleTelemetryTickAsync", ex, false);
            }
            finally
            {
                _isSampling = false;
            }
        }

        public void ShowToast(string message)
        {
            Dispatcher.Invoke(() =>
            {
                TxtStatusNotification.Text = $"ℹ️ {message}";
            });
        }

        #region Window Caption Controls & View Modes

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                BtnMaximize_Click(sender, e);
            }
            else
            {
                DragMove();
            }
        }

        private void Widget_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                MenuViewFull_Click(sender, e);
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            if (_config.MinimizeToTray)
            {
                Hide();
            }
            else
            {
                WindowState = WindowState.Minimized;
            }
        }

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            if (_config.CloseToTray)
            {
                Hide();
            }
            else
            {
                _isClosing = true;
                Close();
            }
        }

        private void BtnPinTop_Click(object sender, RoutedEventArgs e)
        {
            _isPinnedTop = !_isPinnedTop;
            Topmost = _isPinnedTop;
            ShowToast(_isPinnedTop ? "Ventana fijada al frente" : "Ventana desfijada");
        }

        public void MenuViewFull_Click(object sender, RoutedEventArgs e)
        {
            ContainerMainView.Visibility = Visibility.Visible;
            ContainerWidgetView.Visibility = Visibility.Collapsed;
            Width = 1200;
            Height = 760;
            MinWidth = 960;
            MinHeight = 600;
        }

        public void MenuViewHero_Click(object sender, RoutedEventArgs e)
        {
            MenuViewFull_Click(sender, e);
            _viewModel.NavigateTo(typeof(DashboardViewModel));
            Width = 1000;
            Height = 520;
        }

        public void MenuViewWidget_Click(object sender, RoutedEventArgs e)
        {
            ContainerMainView.Visibility = Visibility.Collapsed;
            ContainerWidgetView.Visibility = Visibility.Visible;
            Width = 340;
            Height = 90;
            MinWidth = 280;
            MinHeight = 70;
        }

        private void MenuWidgetSnap_Click(object sender, RoutedEventArgs e)
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - Width - 20;
            Top = workArea.Bottom - Height - 20;
        }

        #endregion

        #region Tray Context Menu Handlers

        private void TrayMenuRestore_Click(object sender, RoutedEventArgs e)
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void BtnPlanSaver_Click(object sender, RoutedEventArgs e) => PowerPlanManager.SetScheme(PowerSchemeMode.PowerSaver);
        private void BtnPlanBalanced_Click(object sender, RoutedEventArgs e) => PowerPlanManager.SetScheme(PowerSchemeMode.Balanced);
        private void BtnPlanHighPerf_Click(object sender, RoutedEventArgs e) => PowerPlanManager.SetScheme(PowerSchemeMode.HighPerformance);
        private void BtnOptimize_Click(object sender, RoutedEventArgs e) => Task.Run(() => MemoryOptimizer.OptimizeWorkingSet(out _));
        private void BtnCleanTemp_Click(object sender, RoutedEventArgs e) => Task.Run(() => SafeTempCleaner.CleanDeepStorage(false));
        private void TrayMenuMinToTray_Click(object sender, RoutedEventArgs e) => _config.MinimizeToTray = !_config.MinimizeToTray;
        private void TrayMenuCloseToTray_Click(object sender, RoutedEventArgs e) => _config.CloseToTray = !_config.CloseToTray;
        private void TrayMenuRunAtStartup_Click(object sender, RoutedEventArgs e) => _config.RunAtStartup = !_config.RunAtStartup;
        private void BtnTaskMgr_Click(object sender, RoutedEventArgs e) => ToolLauncher.StartTaskManager();
        private void BtnResMon_Click(object sender, RoutedEventArgs e) => ToolLauncher.StartResourceMonitor();
        private void MenuOpenServices_Click(object sender, RoutedEventArgs e) => ToolLauncher.StartServicesConsole();

        private void TrayMenuExit_Click(object sender, RoutedEventArgs e)
        {
            _isClosing = true;
            Close();
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_isClosing && _config.CloseToTray)
            {
                e.Cancel = true;
                Hide();
                return;
            }

            _timer.Stop();
            _trayManager.Dispose();
            _accelEngine.Dispose();
        }

        #endregion
    }
}