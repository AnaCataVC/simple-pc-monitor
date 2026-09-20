using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SystemCoreMonitor.Core;
using SystemCoreMonitor.Core.Mvvm;
using SystemCoreMonitor.Models;

namespace SystemCoreMonitor.ViewModels
{
    public class StartupViewModel : ViewModelBase
    {
        private ObservableCollection<StartupItem> _startupItems = new();

        public ObservableCollection<StartupItem> StartupItems
        {
            get => _startupItems;
            set => SetProperty(ref _startupItems, value);
        }

        public RelayCommand<StartupItem> SearchOnlineCommand { get; }

        public StartupViewModel()
        {
            SearchOnlineCommand = new RelayCommand<StartupItem>(item =>
            {
                if (item != null)
                {
                    ProcessManager.SearchProcessOnline(item.Name, "windows startup program");
                }
            });
        }

        public void Update(List<StartupItem> items)
        {
            StartupItems = new ObservableCollection<StartupItem>(items ?? new());
        }
    }
}