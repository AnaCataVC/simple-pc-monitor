using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
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

        public AsyncRelayCommand<ProcessMetric> TerminateProcessCommand { get; }
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

            Processes = new ObservableCollection<ProcessMetric>(query);
        }
    }
}