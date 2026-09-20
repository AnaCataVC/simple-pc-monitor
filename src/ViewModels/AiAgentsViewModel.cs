using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using SystemCoreMonitor.Core;
using SystemCoreMonitor.Core.Mvvm;
using SystemCoreMonitor.Models;

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
            set => SetProperty(ref _orphanProcesses, value);
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

        public event Action<string>? ShowToastRequested;

        public AiAgentsViewModel()
        {
            CloseSessionCommand = new AsyncRelayCommand<AiAgentSession>(async session =>
            {
                if (session == null) return;
                var res = await Task.Run(() => ProcessManager.RequestGracefulCloseAsync(session.ParentPid, session.AgentName));
                ShowToastRequested?.Invoke(string.Format(LocalizationManager.Get("ToastProcessClosed"), session.AgentName, res));
            });

            KillOrphanCommand = new AsyncRelayCommand<AiAgentMcpServer>(async orphan =>
            {
                if (orphan == null) return;
                var res = await Task.Run(() => ProcessManager.RequestGracefulCloseAsync(orphan.Pid, orphan.ProcessName));
                ShowToastRequested?.Invoke(string.Format(LocalizationManager.Get("ToastProcessClosed"), orphan.ProcessName, res));
            });

            KillAllOrphansCommand = new AsyncRelayCommand(async () =>
            {
                int killed = 0;
                await Task.Run(() =>
                {
                    foreach (var o in _orphanProcesses)
                    {
                        ProcessManager.RequestGracefulCloseAsync(o.Pid, o.ProcessName);
                        killed++;
                    }
                });
                ShowToastRequested?.Invoke(string.Format(LocalizationManager.Get("ToastOrphansKilled"), killed));
            });

            ScanTranscriptsCommand = new AsyncRelayCommand(async () =>
            {
                if (IsScanningTranscripts) return;
                IsScanningTranscripts = true;
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
        }
    }
}