using System;
using System.Collections.ObjectModel;
using SystemCoreMonitor.Core;
using SystemCoreMonitor.Core.Mvvm;

namespace SystemCoreMonitor.ViewModels
{
    public class NavigationItem : ObservableObject
    {
        private bool _isSelected;
        private string _badgeText = string.Empty;

        public string Title { get; init; } = string.Empty;
        public string IconKey { get; init; } = string.Empty;
        public System.Windows.Media.Geometry? IconGeometry => 
            System.Windows.Application.Current?.TryFindResource(IconKey) as System.Windows.Media.Geometry;
        public Type ViewModelType { get; init; } = typeof(DashboardViewModel);

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public string BadgeText
        {
            get => _badgeText;
            set => SetProperty(ref _badgeText, value);
        }
    }

    public class MainViewModel : ViewModelBase
    {
        private ViewModelBase _currentViewModel;
        private string _viewMode = "Full";
        private NavigationItem? _selectedNavItem;

        public DashboardViewModel Dashboard { get; } = new();
        public ProcessesViewModel Processes { get; } = new();
        public AiAgentsViewModel AiAgents { get; } = new();
        public AcceleratorsViewModel Accelerators { get; } = new();
        public StorageViewModel Storage { get; } = new();
        public ServicesViewModel Services { get; } = new();
        public StartupViewModel Startup { get; } = new();
        public SettingsViewModel Settings { get; } = new();

        public ViewModelBase CurrentViewModel
        {
            get => _currentViewModel;
            set => SetProperty(ref _currentViewModel, value);
        }

        public string ViewMode
        {
            get => _viewMode;
            set => SetProperty(ref _viewMode, value);
        }

        public ObservableCollection<NavigationItem> NavigationItems { get; } = new();

        public NavigationItem? SelectedNavItem
        {
            get => _selectedNavItem;
            set
            {
                if (SetProperty(ref _selectedNavItem, value) && value != null)
                {
                    NavigateTo(value.ViewModelType);
                }
            }
        }

        public RelayCommand<Type> NavigateCommand { get; }
        public RelayCommand<string> SetViewModeCommand { get; }

        public MainViewModel()
        {
            _currentViewModel = Dashboard;

            NavigateCommand = new RelayCommand<Type>(type =>
            {
                if (type != null) NavigateTo(type);
            });

            SetViewModeCommand = new RelayCommand<string>(mode =>
            {
                if (!string.IsNullOrEmpty(mode)) ViewMode = mode;
            });

            InitializeNavItems();
        }

        private void InitializeNavItems()
        {
            NavigationItems.Add(new NavigationItem { Title = LocalizationManager.Get("TabOverview"), IconKey = "IconHardware", ViewModelType = typeof(DashboardViewModel), IsSelected = true });
            NavigationItems.Add(new NavigationItem { Title = LocalizationManager.Get("TabProcesses"), IconKey = "IconProcess", ViewModelType = typeof(ProcessesViewModel) });
            NavigationItems.Add(new NavigationItem { Title = LocalizationManager.Get("TabAiAgents"), IconKey = "IconNpu", ViewModelType = typeof(AiAgentsViewModel) });
            NavigationItems.Add(new NavigationItem { Title = LocalizationManager.Get("TabAccelerators"), IconKey = "IconGpu", ViewModelType = typeof(AcceleratorsViewModel) });
            NavigationItems.Add(new NavigationItem { Title = LocalizationManager.Get("TabDrives"), IconKey = "IconDisk", ViewModelType = typeof(StorageViewModel) });
            NavigationItems.Add(new NavigationItem { Title = LocalizationManager.Get("TabServices"), IconKey = "IconService", ViewModelType = typeof(ServicesViewModel) });
            NavigationItems.Add(new NavigationItem { Title = LocalizationManager.Get("TabStartup"), IconKey = "IconStartup", ViewModelType = typeof(StartupViewModel) });
            NavigationItems.Add(new NavigationItem { Title = LocalizationManager.Get("MenuSettings"), IconKey = "IconTheme", ViewModelType = typeof(SettingsViewModel) });

            _selectedNavItem = NavigationItems[0];
        }

        public void NavigateTo(Type viewModelType)
        {
            if (viewModelType == typeof(DashboardViewModel)) CurrentViewModel = Dashboard;
            else if (viewModelType == typeof(ProcessesViewModel)) CurrentViewModel = Processes;
            else if (viewModelType == typeof(AiAgentsViewModel)) CurrentViewModel = AiAgents;
            else if (viewModelType == typeof(AcceleratorsViewModel)) CurrentViewModel = Accelerators;
            else if (viewModelType == typeof(StorageViewModel)) CurrentViewModel = Storage;
            else if (viewModelType == typeof(ServicesViewModel)) CurrentViewModel = Services;
            else if (viewModelType == typeof(StartupViewModel)) CurrentViewModel = Startup;
            else if (viewModelType == typeof(SettingsViewModel)) CurrentViewModel = Settings;

            foreach (var item in NavigationItems)
            {
                item.IsSelected = item.ViewModelType == viewModelType;
            }
        }
    }
}