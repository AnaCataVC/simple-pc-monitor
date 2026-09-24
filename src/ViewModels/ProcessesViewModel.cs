using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using SystemCoreMonitor.Core;
using SystemCoreMonitor.Core.Mvvm;
using SystemCoreMonitor.Models;

namespace SystemCoreMonitor.ViewModels
{
    public class ProcessesViewModel : ViewModelBase
    {
        private ObservableCollection<ProcessMetric> _processes = new();
        private ProcessMetric? _selectedProcess;
        private string _searchFilter = string.Empty;
        private string _sortColumn = "Cpu";
        private bool _sortDescending = true;

        public ObservableCollection<ProcessMetric> Processes
        {
            get => _processes;
            set => SetProperty(ref _processes, value);
        }

        public ProcessMetric? SelectedProcess
        {
            get => _selectedProcess;
            set => SetProperty(ref _selectedProcess, value);
        }

        public string SearchFilter
        {
            get => _searchFilter;
            set
            {
                if (SetProperty(ref _searchFilter, value))
                {
                    ApplyFilter();
                }
            }
        }

        private readonly ObservableCollection<RunawayProcess> _runawayProcesses = new();

        public ObservableCollection<RunawayProcess> RunawayProcesses => _runawayProcesses;
        public bool HasRunaway => _runawayProcesses.Count > 0;
        public string RunawayTitle => string.Format(LocalizationManager.Get("CardRunawayTitle"), _runawayProcesses.Count);
        public string RunawaySubtitle => LocalizationManager.Get("CardRunawaySubtitle");
        public string RunawayEndLabel => LocalizationManager.Get("ButtonRunawayEnd");

        public AsyncRelayCommand<ProcessMetric> TerminateProcessCommand { get; }
        public AsyncRelayCommand<RunawayProcess> EndRunawayCommand { get; }
        public RelayCommand<ProcessMetric> SearchOnlineCommand { get; }
        public RelayCommand<string> SortCommand { get; }

        public event Action<string, NotificationType>? ShowToastRequested;

        private List<ProcessMetric> _allProcesses = new();

        public ProcessesViewModel()
        {
            TerminateProcessCommand = new AsyncRelayCommand<ProcessMetric>(async item =>
            {
                if (item == null) return;
                var result = await Task.Run(() => ProcessManager.RequestGracefulCloseAsync(item.Id, item.Name));
                if (result == ProcessManager.ProcessCloseResult.ClosedGracefully || result == ProcessManager.ProcessCloseResult.MinimizedToTray)
                {
                    ShowToastRequested?.Invoke(
                        string.Format(LocalizationManager.Get("ToastProcessClosed"), item.Name, result),
                        NotificationType.Success);
                }
                else
                {
                    ShowToastRequested?.Invoke(
                        string.Format(LocalizationManager.Get("ToastProcessCloseFailed"), item.Name, item.Id, result),
                        NotificationType.Error);
                }
            });

            EndRunawayCommand = new AsyncRelayCommand<RunawayProcess>(async item =>
            {
                if (item == null) return;

                // Detection is a filter for human attention, never a verdict (AGENTS rule 13).
                string prompt = string.Format(LocalizationManager.Get("ConfirmRunawayKill"), item.Name, item.Pid, item.Reason, item.CommandLine);
                var answer = MessageBox.Show(prompt, LocalizationManager.Get("ConfirmRunawayTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (answer != MessageBoxResult.Yes) return;

                // The sampled StartTime aborts the kill if the PID was recycled since the sample (AGENTS rule 12).
                string message = string.Empty;
                bool killed = await Task.Run(() => ProcessManager.TerminateProcessTree(item.Pid, true, out message, item.StartTime));
                if (killed)
                {
                    _runawayProcesses.Remove(item);
                    NotifyRunawayChanged();
                    ShowToastRequested?.Invoke(string.Format(LocalizationManager.Get("ToastRunawayKilled"), item.Name, item.Pid), NotificationType.Success);
                }
                else
                {
                    ShowToastRequested?.Invoke(string.Format(LocalizationManager.Get("ToastRunawayKillFailed"), item.Name, item.Pid, message), NotificationType.Error);
                }
            });

            SearchOnlineCommand = new RelayCommand<ProcessMetric>(item =>
            {
                if (item != null) ProcessManager.SearchProcessOnline(item.Name);
            });

            SortCommand = new RelayCommand<string>(col =>
            {
                if (col == _sortColumn) _sortDescending = !_sortDescending;
                else { _sortColumn = col ?? "Cpu"; _sortDescending = true; }
                ApplyFilter();
            });
        }

        public void Update(List<ProcessMetric> processes)
        {
            _allProcesses = processes ?? new();
            ApplyFilter();
        }

        public void UpdateRunaway(List<RunawayProcess> runaway)
        {
            var target = runaway ?? new List<RunawayProcess>();
            for (int i = 0; i < target.Count; i++)
            {
                var item = target[i];
                if (i >= _runawayProcesses.Count)
                {
                    _runawayProcesses.Add(item);
                    continue;
                }

                var current = _runawayProcesses[i];
                if (current.Pid != item.Pid || current.StartTime != item.StartTime ||
                    current.CpuPercent != item.CpuPercent || current.Reason != item.Reason || current.AgeDisplay != item.AgeDisplay)
                {
                    _runawayProcesses[i] = item;
                }
            }

            while (_runawayProcesses.Count > target.Count)
            {
                _runawayProcesses.RemoveAt(_runawayProcesses.Count - 1);
            }
            NotifyRunawayChanged();
        }

        private void NotifyRunawayChanged()
        {
            OnPropertyChanged(nameof(HasRunaway));
            OnPropertyChanged(nameof(RunawayTitle));
        }

        private void ApplyFilter()
        {
            var query = _allProcesses.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(_searchFilter))
            {
                query = query.Where(p =>
                    p.Name.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(p.FriendlyName) && p.FriendlyName.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase)) ||
                    p.Id.ToString().Contains(_searchFilter));
            }

            query = _sortColumn switch
            {
                "Ram" => _sortDescending ? query.OrderByDescending(p => p.MemoryMB) : query.OrderBy(p => p.MemoryMB),
                "Name" => _sortDescending ? query.OrderByDescending(p => p.Name) : query.OrderBy(p => p.Name),
                "Pid" => _sortDescending ? query.OrderByDescending(p => p.Id) : query.OrderBy(p => p.Id),
                _ => _sortDescending ? query.OrderByDescending(p => p.CpuPercent) : query.OrderBy(p => p.CpuPercent)
            };

            var targetList = query.ToList();
            _processes.SyncInPlace(targetList, (a, b) =>
                a.Id == b.Id && a.CpuPercent == b.CpuPercent && a.MemoryMB == b.MemoryMB && a.IsResponding == b.IsResponding);
        }
    }
}