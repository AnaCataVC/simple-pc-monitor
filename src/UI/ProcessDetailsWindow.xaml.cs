using System;
using System.Windows;
using System.Windows.Input;
using SimplePCMonitor.Core;
using SimplePCMonitor.Models;

namespace SimplePCMonitor.UI
{
    public partial class ProcessDetailsWindow : Window
    {
        private readonly int _pid;
        private readonly string _processName;
        private readonly double _ramPercent;
        private ProcessDetailedInfo _info;

        public ProcessDetailsWindow(int pid, string processName, double ramPercent = 0.0)
        {
            InitializeComponent();
            _pid = pid;
            _processName = processName;
            _ramPercent = ramPercent;

            Loaded += (s, e) => LoadProcessDetails();
        }

        public ProcessDetailsWindow(ProcessMetric metric)
            : this(metric != null ? metric.Id : 0, metric != null ? metric.Name : string.Empty, metric != null ? metric.MemoryPercent : 0.0)
        {
        }

        private void LoadProcessDetails()
        {
            _info = ProcessManager.GetDetailedProcessInfo(_pid, _processName);
            if (_info == null) return;

            // Identity
            TxtFriendlyName.Text = !string.IsNullOrEmpty(_info.FriendlyName) ? _info.FriendlyName : _info.Name;
            TxtProcessExeName.Text = _info.Name + ".exe";
            TxtPublisherName.Text = !string.IsNullOrEmpty(_info.CompanyName) ? _info.CompanyName : "Unknown Publisher / Standard Executable";
            TxtPidBadge.Text = string.Format("PID: {0}", _info.Id);
            TxtArchBadge.Text = _info.Architecture;

            if (_info.IsProtected)
            {
                BadgeProtected.Visibility = Visibility.Visible;
                BtnEndProcess.IsEnabled = false;
                BtnEndProcess.Opacity = 0.45;
                BtnEndProcess.ToolTip = "Protected Windows system process cannot be terminated";
            }
            else
            {
                BadgeProtected.Visibility = Visibility.Collapsed;
                BtnEndProcess.IsEnabled = true;
                BtnEndProcess.Opacity = 1.0;
            }

            // Memory KPIs
            TxtWorkingSet.Text = string.Format("{0:N1} MB", _info.WorkingSetMB);
            TxtRamPercent.Text = string.Format("{0:N1}% of Total RAM", _ramPercent > 0 ? _ramPercent : _info.MemoryPercent);
            TxtPeakWorkingSet.Text = string.Format("{0:N1} MB", _info.PeakWorkingSetMB);
            TxtPrivateBytes.Text = string.Format("{0:N1} MB", _info.PrivateMemoryMB);
            TxtThreadsHandles.Text = string.Format("{0} / {1}", _info.ThreadCount, _info.HandleCount);
            TxtPriority.Text = string.Format("{0} Priority", _info.PriorityClass);

            // Execution & Runtime
            TxtStartTime.Text = _info.StartTimeDisplay;
            TxtUptime.Text = _info.UptimeDisplay;
            TxtWindowTitle.Text = !string.IsNullOrWhiteSpace(_info.WindowTitle)
                ? _info.WindowTitle
                : "None (Background Process / Service)";

            TxtResponding.Text = _info.IsResponding
                ? "Active & Responding Normally"
                : "Not Responding / Suspended";

            // File & Binary Metadata
            TxtExePath.Text = !string.IsNullOrEmpty(_info.ExecutablePath)
                ? _info.ExecutablePath
                : "Path not accessible (elevated/system process)";

            TxtFileDescription.Text = !string.IsNullOrWhiteSpace(_info.Description)
                ? _info.Description
                : (!string.IsNullOrWhiteSpace(_info.FriendlyName) ? _info.FriendlyName : "N/A");

            TxtVersion.Text = !string.IsNullOrEmpty(_info.FileVersion) && _info.FileVersion != "N/A"
                ? string.Format("{0} (Product: {1})", _info.FileVersion, _info.ProductVersion)
                : "N/A";

            TxtCopyright.Text = !string.IsNullOrWhiteSpace(_info.Copyright)
                ? _info.Copyright
                : "N/A";
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            string err;
            if (!ProcessManager.OpenProcessLocation(_info.Id, out err))
            {
                MessageBox.Show(err, "Location Error", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnSearchGoogle_Click(object sender, RoutedEventArgs e)
        {
            ProcessManager.SearchProcessOnline(!string.IsNullOrEmpty(_info.FriendlyName) ? _info.FriendlyName : _info.Name);
        }

        private void BtnCopyDiagnostic_Click(object sender, RoutedEventArgs e)
        {
            ProcessManager.CopyDetailedDiagnosticToClipboard(_info);
            MessageBox.Show("Process diagnostic information copied to clipboard!", "Diagnostics Copied", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void BtnEndProcess_Click(object sender, RoutedEventArgs e)
        {
            if (_info == null || _info.IsProtected) return;

            // Phase 1: Graceful close attempt
            var closeStatus = await ProcessManager.RequestGracefulCloseAsync(_info.Id, _info.Name, 2000);

            if (closeStatus == ProcessManager.ProcessCloseResult.ClosedGracefully)
            {
                MessageBox.Show(string.Format("Process '{0}' (PID: {1}) closed gracefully.", _info.Name, _info.Id), "Process Closed", MessageBoxButton.OK, MessageBoxImage.Information);
                Close();
                return;
            }

            if (closeStatus == ProcessManager.ProcessCloseResult.ProtectedProcess)
            {
                MessageBox.Show(string.Format("'{0}' is a system-protected process and cannot be closed.", _info.Name), "Protected Process", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Phase 2: Tray minimization or Unresponsive escalation
            string promptMsg = closeStatus == ProcessManager.ProcessCloseResult.MinimizedToTray
                ? string.Format("Process '{0}' (PID: {1}) closed its window but is still running in the background / System Tray.\n\nDo you want to force terminate (Kill) it?", _info.Name, _info.Id)
                : string.Format("Process '{0}' (PID: {1}) did not respond to the graceful close request.\n\nDo you want to force terminate (Kill) it?", _info.Name, _info.Id);

            var result = MessageBox.Show(promptMsg, "Force Terminate Process", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                string msg;
                bool success = ProcessManager.TerminateProcess(_info.Id, _info.Name, out msg);
                MessageBox.Show(msg, success ? "Process Force Ended" : "Termination Failed", MessageBoxButton.OK, success ? MessageBoxImage.Information : MessageBoxImage.Error);
                if (success)
                {
                    Close();
                }
            }
        }
    }
}
