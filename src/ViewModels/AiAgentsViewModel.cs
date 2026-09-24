using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
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
        private readonly ObservableCollection<AiAgentOrphanGroup> _orphanGroups = new();
        private readonly HashSet<(int, DateTime)> _expandedOrphanGroups = new();
        private AiTranscriptReport? _transcriptReport;
        private bool _isScanningTranscripts;
        private bool _isPruningTranscripts;
        private string _transcriptSummaryDisplay = "Almacenamiento IA no escaneado aún";

        private readonly AiTranscriptCleaner _cleaner = new();
        private readonly AiAgentLeftoverScanner _leftoverScanner = new();
        private readonly ObservableCollection<AiLeftoverItem> _leftoverItems = new();
        private CancellationTokenSource? _leftoverScanCts;
        private bool _isScanningLeftovers;
        private string _leftoverSummaryDisplay = "Restos de agentes no escaneados aún";

        public ObservableCollection<AiLeftoverItem> LeftoverItems => _leftoverItems;
        public bool HasLeftovers => _leftoverItems.Count > 0;

        public bool IsScanningLeftovers
        {
            get => _isScanningLeftovers;
            set => SetProperty(ref _isScanningLeftovers, value);
        }

        public string LeftoverSummaryDisplay
        {
            get => _leftoverSummaryDisplay;
            set => SetProperty(ref _leftoverSummaryDisplay, value);
        }

        public AsyncRelayCommand ScanLeftoversCommand { get; }
        public RelayCommand CancelLeftoverScanCommand { get; }
        public AsyncRelayCommand CleanLeftoversCommand { get; }

        public bool HasOrphans => _orphanGroups.Count > 0;
        public int OrphanCount => _orphanGroups.Sum(g => g.ProcessCount);
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

        public ObservableCollection<AiAgentOrphanGroup> OrphanGroups => _orphanGroups;

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
        public AsyncRelayCommand<AiAgentOrphanGroup> KillOrphanGroupCommand { get; }
        public AsyncRelayCommand KillAllOrphansCommand { get; }
        public RelayCommand<AiAgentOrphanGroup> ToggleOrphanGroupCommand { get; }
        public AsyncRelayCommand ScanTranscriptsCommand { get; }
        public AsyncRelayCommand CleanStaleTranscriptsCommand { get; }
        public RelayCommand<AiAgentSession> ToggleSessionExpandedCommand { get; }

        public event Action<string, NotificationType>? ShowToastRequested;
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
                    : string.Format("▼ Ver subprocesos ({0})", session.ChildProcessCount);
            });
            CloseSessionCommand = new AsyncRelayCommand<AiAgentSession>(async session =>
            {
                if (session == null) return;
                ShowProgressRequested?.Invoke(string.Format("Cerrando sesión de {0}...", session.AgentName));
                var res = await Task.Run(() => ProcessManager.RequestGracefulCloseAsync(session.ParentPid, session.AgentName));
                if (res == ProcessManager.ProcessCloseResult.ClosedGracefully || res == ProcessManager.ProcessCloseResult.MinimizedToTray)
                {
                    ShowToastRequested?.Invoke(string.Format(LocalizationManager.Get("ToastProcessClosed"), session.AgentName, res), NotificationType.Success);
                }
                else
                {
                    ShowToastRequested?.Invoke(string.Format(LocalizationManager.Get("ToastProcessCloseFailed"), session.AgentName, session.ParentPid, res), NotificationType.Error);
                }
            });

            KillOrphanCommand = new AsyncRelayCommand<AiAgentMcpServer>(async orphan =>
            {
                if (orphan == null) return;
                await ConfirmAndKillOrphansAsync(new List<AiAgentMcpServer> { orphan });
            });

            KillOrphanGroupCommand = new AsyncRelayCommand<AiAgentOrphanGroup>(async group =>
            {
                if (group == null) return;
                await ConfirmAndKillOrphansAsync(group.Processes.ToList());
            });

            KillAllOrphansCommand = new AsyncRelayCommand(async () =>
            {
                await ConfirmAndKillOrphansAsync(_orphanGroups.SelectMany(g => g.Processes).ToList());
            });

            ToggleOrphanGroupCommand = new RelayCommand<AiAgentOrphanGroup>(group =>
            {
                if (group == null) return;
                group.IsExpanded = !group.IsExpanded;
                var key = (group.RootPid, group.RootStartTime);
                if (group.IsExpanded) _expandedOrphanGroups.Add(key); else _expandedOrphanGroups.Remove(key);
            });

            ScanLeftoversCommand = new AsyncRelayCommand(ScanLeftoversAsync);
            CancelLeftoverScanCommand = new RelayCommand(() => _leftoverScanCts?.Cancel());
            CleanLeftoversCommand = new AsyncRelayCommand(CleanLeftoversAsync);

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
                    ShowToastRequested?.Invoke(string.Format("Escaneo completado: {0} liberables de transcripts IA", report.StaleSizeDisplay), NotificationType.Info);
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
                    ShowToastRequested?.Invoke(result.Message, result.Failures.Count == 0 ? NotificationType.Success : NotificationType.Error);
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
            _sessions.SyncInPlace(metric.Sessions);
            SyncOrphanGroups(metric.OrphanGroups);
        }

        // In-place sync so a refresh does not collapse expanded groups or reset the scroll.
        private void SyncOrphanGroups(List<AiAgentOrphanGroup> target)
        {
            target ??= new List<AiAgentOrphanGroup>();
            for (int i = 0; i < target.Count; i++)
            {
                var item = target[i];
                item.IsExpanded = _expandedOrphanGroups.Contains((item.RootPid, item.RootStartTime));
                if (i >= _orphanGroups.Count)
                {
                    _orphanGroups.Add(item);
                    continue;
                }

                var current = _orphanGroups[i];
                if (current.RootPid != item.RootPid || current.RootStartTime != item.RootStartTime ||
                    current.ProcessCount != item.ProcessCount || current.TotalRamMB != item.TotalRamMB ||
                    current.EndedDisplay != item.EndedDisplay)
                {
                    _orphanGroups[i] = item;
                }
            }

            while (_orphanGroups.Count > target.Count)
            {
                _orphanGroups.RemoveAt(_orphanGroups.Count - 1);
            }

            var liveKeys = new HashSet<(int, DateTime)>(target.Select(g => (g.RootPid, g.RootStartTime)));
            _expandedOrphanGroups.RemoveWhere(k => !liveKeys.Contains(k));
            OnPropertyChanged(nameof(HasOrphans));
            OnPropertyChanged(nameof(OrphanCount));
        }

        // On-demand only (AGENTS rule 11): never wired into the telemetry tick.
        private async Task ScanLeftoversAsync()
        {
            if (IsScanningLeftovers) return;
            IsScanningLeftovers = true;
            _leftoverScanCts = new CancellationTokenSource();
            var token = _leftoverScanCts.Token;
            var progress = new Progress<string>(stage => LeftoverSummaryDisplay = "Escaneando: " + stage);
            try
            {
                int retention = ConfigManager.Current.TranscriptRetentionDays;
                var report = await Task.Run(() => _leftoverScanner.Scan(retention, progress, token));
                _leftoverItems.Clear();
                foreach (var item in report.Items) _leftoverItems.Add(item);
                OnPropertyChanged(nameof(HasLeftovers));
                LeftoverSummaryDisplay = string.Format("{0}{1} elemento(s) • Liberable: {2} ({3} elemento(s))",
                    report.Cancelled ? "Cancelado • " : string.Empty,
                    report.Items.Count,
                    MetricFormatting.FormatBytesAuto(report.DeletableBytes),
                    report.DeletableCount);
            }
            finally
            {
                IsScanningLeftovers = false;
                _leftoverScanCts.Dispose();
                _leftoverScanCts = null;
            }
        }

        private async Task CleanLeftoversAsync()
        {
            var deletable = _leftoverItems.Where(i => i.CanDelete).ToList();
            if (deletable.Count == 0 || IsScanningLeftovers) return;

            var lines = deletable.Take(12).Select(i => string.Format("• [{0}] {1} ({2}) — {3}", i.KindDisplay, i.DisplayPath, i.SizeDisplay, i.Reason)).ToList();
            if (deletable.Count > 12) lines.Add(string.Format(LocalizationManager.Get("ConfirmOrphanKillMore"), deletable.Count - 12));
            string prompt = string.Format(LocalizationManager.Get("ConfirmLeftoverClean"), deletable.Count,
                MetricFormatting.FormatBytesAuto(deletable.Sum(i => i.SizeBytes)), string.Join("\n", lines));
            if (MessageBox.Show(prompt, LocalizationManager.Get("ConfirmLeftoverTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            int retention = ConfigManager.Current.TranscriptRetentionDays;
            var result = await Task.Run(() => _leftoverScanner.Clean(deletable, retention));
            ShowToastRequested?.Invoke(
                string.Format("Restos IA: {0} limpiado(s), {1} liberado(s), {2} fallo(s)", result.Removed, MetricFormatting.FormatBytesAuto(result.BytesFreed), result.Failures.Count),
                result.Failures.Count == 0 ? NotificationType.Success : NotificationType.Warning);
            await ScanLeftoversAsync();
        }

        /// <summary>
        /// Confirmation text for an orphan kill: what matched and why (AGENTS rule 13).
        /// </summary>
        public static string BuildOrphanKillPrompt(IReadOnlyList<AiAgentMcpServer> orphans, int maxListed = 12)
        {
            double totalRam = orphans.Sum(o => o.WorkingSetMB);
            var lines = orphans.Take(maxListed).Select(o => string.Format(
                "• {0} (PID {1}, {2}, {3}) — {4}", o.ProcessName, o.Pid, o.MemoryDisplay, o.AgeDisplay, o.OrphanReason)).ToList();
            if (orphans.Count > maxListed)
            {
                lines.Add(string.Format(LocalizationManager.Get("ConfirmOrphanKillMore"), orphans.Count - maxListed));
            }
            return string.Format(LocalizationManager.Get("ConfirmOrphanKill"), orphans.Count, string.Format("{0:N1} MB", totalRam), string.Join("\n", lines));
        }

        private async Task ConfirmAndKillOrphansAsync(List<AiAgentMcpServer> toKill)
        {
            if (toKill.Count == 0) return;

            var answer = MessageBox.Show(BuildOrphanKillPrompt(toKill), LocalizationManager.Get("ConfirmOrphanKillTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;

            ShowProgressRequested?.Invoke(LocalizationManager.Get("ActionKillingOrphans"));
            int killed = 0;
            int failed = 0;

            // Each kill carries the sampled StartTime, so a PID recycled since the sample aborts
            // instead of terminating a stranger (AGENTS rule 12).
            await Task.Run(() =>
            {
                foreach (var o in toKill)
                {
                    if (ProcessManager.TerminateProcessTree(o.Pid, true, out _, o.StartTime)) killed++;
                    else failed++;
                }
            });

            if (failed == 0)
            {
                ShowToastRequested?.Invoke(string.Format(LocalizationManager.Get("ToastOrphansKilled"), killed), NotificationType.Success);
            }
            else
            {
                ShowToastRequested?.Invoke(string.Format(LocalizationManager.Get("ToastOrphansKilledPartial"), killed, failed), NotificationType.Warning);
            }
        }
    }
}
