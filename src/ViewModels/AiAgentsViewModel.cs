using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using SystemCoreMonitor.Core;
using SystemCoreMonitor.Core.Mvvm;
using SystemCoreMonitor.Models;
using SystemCoreMonitor.Modules;

namespace SystemCoreMonitor.ViewModels
{
    public class AiAgentsViewModel : ViewModelBase
    {
        private AiAgentMetric _metric = new();
        private ObservableCollection<AiAgentSession> _sessions = new();
        private ObservableCollection<AiAgentMcpServer> _orphanProcesses = new();
        private AiTranscriptReport? _transcriptReport;
        private bool _isScanningTranscripts;
        private bool _isPruningTranscripts;
        private string _transcriptSummaryDisplay = "Almacenamiento IA no escaneado aún";

        private readonly AiTranscriptCleaner _cleaner = new();

        public bool HasOrphans => _orphanProcesses != null && _orphanProcesses.Count > 0;
        public string CleanButtonText => string.Format("Limpiar (>{0}d)", ConfigManager.Current.TranscriptRetentionDays);

        public void RefreshRetentionDisplay()
        {
            OnPropertyChanged(nameof(CleanButtonText));
        }

        public AiAgentMetric Metric
        {
            get => _metric;
            set => SetProperty(ref _metric, value);
        }

        public ObservableCollection<AiAgentSession> Sessions
        {
            get => _sessions;
            set => SetProperty(ref _sessions, value);
        }

        public ObservableCollection<AiAgentMcpServer> OrphanProcesses
        {
            get => _orphanProcesses;
            set
            {
                if (SetProperty(ref _orphanProcesses, value))
                {
                    OnPropertyChanged(nameof(HasOrphans));
                }
            }
        }

        public AiTranscriptReport? TranscriptReport
        {
            get => _transcriptReport;
            set => SetProperty(ref _transcriptReport, value);
        }

        public bool IsScanningTranscripts
        {
            get => _isScanningTranscripts;
            set => SetProperty(ref _isScanningTranscripts, value);
        }

        public bool IsPruningTranscripts
        {
            get => _isPruningTranscripts;
            set => SetProperty(ref _isPruningTranscripts, value);
        }

        public string TranscriptSummaryDisplay
        {
            get => _transcriptSummaryDisplay;
            set => SetProperty(ref _transcriptSummaryDisplay, value);
        }

        public AsyncRelayCommand<AiAgentSession> CloseSessionCommand { get; }
        public AsyncRelayCommand<AiAgentMcpServer> KillOrphanCommand { get; }
        public AsyncRelayCommand KillAllOrphansCommand { get; }
        public AsyncRelayCommand ScanTranscriptsCommand { get; }
        public AsyncRelayCommand CleanStaleTranscriptsCommand { get; }
        public RelayCommand<AiAgentSession> ToggleSessionExpandedCommand { get; }

        public event Action<string>? ShowToastRequested;
        public event Action<string>? ShowProgressRequested;

        public AiAgentsViewModel()
        {
            ToggleSessionExpandedCommand = new RelayCommand<AiAgentSession>(session =>
            {
                if (session == null) return;
                AiAgentCollector.ToggleSessionExpanded(session.ParentPid);
                session.IsExpanded = AiAgentCollector.IsSessionExpanded(session.ParentPid);
                session.ExpandToggleText = session.IsExpanded
                    ? string.Format("▲ Ocultar ({0})", session.ChildProcessCount)
                    : string.Format("▼ Ver {0} subprocesos", session.ChildProcessCount);
            });
            CloseSessionCommand = new AsyncRelayCommand<AiAgentSession>(async session =>
            {
                if (session == null) return;
                ShowProgressRequested?.Invoke(string.Format("Cerrando sesión de {0}...", session.AgentName));
                var res = await Task.Run(() => ProcessManager.RequestGracefulCloseAsync(session.ParentPid, session.AgentName));
                ShowToastRequested?.Invoke(string.Format(LocalizationManager.Get("ToastProcessClosed"), session.AgentName, res));
            });

            KillOrphanCommand = new AsyncRelayCommand<AiAgentMcpServer>(async orphan =>
            {
                if (orphan == null) return;
                ShowProgressRequested?.Invoke(string.Format("Cerrando proceso {0} (PID {1})...", orphan.ProcessName, orphan.Pid));
                var res = await Task.Run(() => ProcessManager.RequestGracefulCloseAsync(orphan.Pid, orphan.ProcessName));
                App.Current?.Dispatcher?.Invoke(() =>
                {
                    _orphanProcesses.Remove(orphan);
                    OnPropertyChanged(nameof(HasOrphans));
                });
                ShowToastRequested?.Invoke(string.Format(LocalizationManager.Get("ToastProcessClosed"), orphan.ProcessName, res));
            });

            KillAllOrphansCommand = new AsyncRelayCommand(async () =>
            {
                ShowProgressRequested?.Invoke(LocalizationManager.Get("ActionKillingOrphans"));
                int killed = 0;
                var toKill = _orphanProcesses.ToList();
                await Task.Run(() =>
                {
                    foreach (var o in toKill)
                    {
                        ProcessManager.RequestGracefulCloseAsync(o.Pid, o.ProcessName);
                        killed++;
                    }
                });
                App.Current?.Dispatcher?.Invoke(() =>
                {
                    _orphanProcesses.Clear();
                    OnPropertyChanged(nameof(HasOrphans));
                });
                ShowToastRequested?.Invoke(string.Format(LocalizationManager.Get("ToastOrphansKilled"), killed));
            });

            ScanTranscriptsCommand = new AsyncRelayCommand(async () =>
            {
                if (IsScanningTranscripts) return;
                IsScanningTranscripts = true;
                ShowProgressRequested?.Invoke(LocalizationManager.Get("ActionScanningTranscripts"));
                try
                {
                    int retention = ConfigManager.Current.TranscriptRetentionDays;
                    var report = await Task.Run(() => _cleaner.ScanReport(retention));
                    TranscriptReport = report;
                    TranscriptSummaryDisplay = string.Format(
                        "Total: {0} ({1} archivos) • Liberable (> {2}d): {3} ({4} archivos)",
                        report.TotalSizeDisplay,
                        report.TotalFilesCount,
                        retention,
                        report.StaleSizeDisplay,
                        report.StaleFilesCount);
                    ShowToastRequested?.Invoke(string.Format("Escaneo completado: {0} liberables de transcripts IA", report.StaleSizeDisplay));
                }
                finally
                {
                    IsScanningTranscripts = false;
                }
            });

            CleanStaleTranscriptsCommand = new AsyncRelayCommand(async () =>
            {
                if (IsPruningTranscripts) return;
                IsPruningTranscripts = true;
                ShowProgressRequested?.Invoke(LocalizationManager.Get("ActionPruningTranscripts"));
                try
                {
                    int retention = ConfigManager.Current.TranscriptRetentionDays;
                    var result = await Task.Run(() => _cleaner.CleanStaleTranscripts(retention));
                    ShowToastRequested?.Invoke(result.Message);
                    // Re-scan to update telemetry
                    var report = await Task.Run(() => _cleaner.ScanReport(retention));
                    TranscriptReport = report;
                    TranscriptSummaryDisplay = string.Format(
                        "Total: {0} ({1} archivos) • Liberable (> {2}d): {3}",
                        report.TotalSizeDisplay,
                        report.TotalFilesCount,
                        retention,
                        report.StaleSizeDisplay);
                }
                finally
                {
                    IsPruningTranscripts = false;
                }
            });
        }

        public void Update(AiAgentMetric metric)
        {
            Metric = metric;
            Sessions = new ObservableCollection<AiAgentSession>(metric.Sessions);
            OrphanProcesses = new ObservableCollection<AiAgentMcpServer>(metric.OrphanProcesses);
            OnPropertyChanged(nameof(HasOrphans));
        }
    }
}