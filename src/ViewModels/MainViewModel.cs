using System;
using System.Collections.ObjectModel;
using SystemCoreMonitor.Core;
using SystemCoreMonitor.Core.Mvvm;

namespace SystemCoreMonitor.ViewModels
{
    public class NavigationItem : ObservableObject
    {
        private string _title = string.Empty;
        private bool _isSelected;
        private string _badgeText = string.Empty;

        public string LocalizationKey { get; init; } = string.Empty;
        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }
        public string IconKey { get; init; } = string.Empty;
        public System.Windows.Media.Geometry? IconGeometry => 
            System.Windows.Application.Current?.TryFindResource(IconKey) as System.Windows.Media.Geometry;
        public Type ViewModelType { get; init; } = typeof(DashboardViewModel);

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        private bool _isVisible = true;
        public bool IsVisible
        {
            get => _isVisible;
            set => SetProperty(ref _isVisible, value);
        }

        public string BadgeText
        {
            get => _badgeText;
            set => SetProperty(ref _badgeText, value);
        }

        public void RefreshTitle()
        {
            if (!string.IsNullOrEmpty(LocalizationKey))
            {
                Title = LocalizationManager.Get(LocalizationKey);
            }
        }
    }

    public class MainViewModel : ViewModelBase
    {
        private ViewModelBase _currentViewModel;
        private string _viewMode = "Full";
        private NavigationItem? _selectedNavItem;
        private NavigationItem? _acceleratorsNavItem;

        public DashboardViewModel Dashboard { get; } = new();
        public ProcessesViewModel Processes { get; } = new();
        public AiAgentsViewModel AiAgents { get; } = new();
        public AcceleratorsViewModel Accelerators { get; } = new();
        public StorageViewModel Storage { get; } = new();
        public ServicesViewModel Services { get; } = new();
        public StartupViewModel Startup { get; } = new();
        public SettingsViewModel Settings { get; } = new();

        public string WindowModesHeader => LocalizationManager.Get("MenuViewsHeader");
        public string ViewModeWidgetText => LocalizationManager.Get("ViewWidget");
        public string HwSummaryBadgeText => LocalizationManager.Get("HwSummaryBadge");

        public ObservableCollection<NavigationItem> NavigationItems { get; } = new();

        public NavigationItem? SelectedNavItem
        {
            get => _selectedNavItem;
            set => SetProperty(ref _selectedNavItem, value);
        }

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
            SetAcceleratorsVisibility(ConfigManager.Current.EnableAcceleratorsMonitoring);
            Settings.AcceleratorsMonitoringChanged += SetAcceleratorsVisibility;
            LocalizationManager.LanguageChanged += RefreshLocalization;
        }

        private void RefreshLocalization()
        {
            foreach (var item in NavigationItems)
            {
                item.RefreshTitle();
            }
            _acceleratorsNavItem?.RefreshTitle();
            OnPropertyChanged(nameof(WindowModesHeader));
            OnPropertyChanged(nameof(ViewModeWidgetText));
            OnPropertyChanged(nameof(HwSummaryBadgeText));
        }

        private void InitializeNavItems()
        {
            NavigationItems.Add(new NavigationItem { LocalizationKey = "TabOverview", Title = LocalizationManager.Get("TabOverview"), IconKey = "IconHardware", ViewModelType = typeof(DashboardViewModel), IsSelected = true });
            NavigationItems.Add(new NavigationItem { LocalizationKey = "TabProcesses", Title = LocalizationManager.Get("TabProcesses"), IconKey = "IconProcess", ViewModelType = typeof(ProcessesViewModel) });
            NavigationItems.Add(new NavigationItem { LocalizationKey = "TabAiAgents", Title = LocalizationManager.Get("TabAiAgents"), IconKey = "IconBot", ViewModelType = typeof(AiAgentsViewModel) });
            
            _acceleratorsNavItem = new NavigationItem { LocalizationKey = "TabAccelerators", Title = LocalizationManager.Get("TabAccelerators"), IconKey = "IconGpu", ViewModelType = typeof(AcceleratorsViewModel) };
            if (ConfigManager.Current.EnableAcceleratorsMonitoring)
            {
                NavigationItems.Add(_acceleratorsNavItem);
            }

            NavigationItems.Add(new NavigationItem { LocalizationKey = "TabDrives", Title = LocalizationManager.Get("TabDrives"), IconKey = "IconDisk", ViewModelType = typeof(StorageViewModel) });
            NavigationItems.Add(new NavigationItem { LocalizationKey = "TabServices", Title = LocalizationManager.Get("TabServices"), IconKey = "IconService", ViewModelType = typeof(ServicesViewModel) });
            NavigationItems.Add(new NavigationItem { LocalizationKey = "TabStartup", Title = LocalizationManager.Get("TabStartup"), IconKey = "IconStartup", ViewModelType = typeof(StartupViewModel) });
            NavigationItems.Add(new NavigationItem { LocalizationKey = "MenuSettings", Title = LocalizationManager.Get("MenuSettings"), IconKey = "IconSettings", ViewModelType = typeof(SettingsViewModel) });

            _selectedNavItem = NavigationItems[0];
        }

        public void SetAcceleratorsVisibility(bool visible)
        {
            void Apply()
            {
                if (_acceleratorsNavItem == null) return;

                _acceleratorsNavItem.IsVisible = visible;

                if (visible)
                {
                    if (!NavigationItems.Contains(_acceleratorsNavItem))
                    {
                        int targetIndex = -1;
                        for (int i = 0; i < NavigationItems.Count; i++)
                        {
                            if (NavigationItems[i].ViewModelType == typeof(AiAgentsViewModel))
                            {
                                targetIndex = i + 1;
                                break;
                            }
                        }

                        if (targetIndex >= 0 && targetIndex <= NavigationItems.Count)
                        {
                            NavigationItems.Insert(targetIndex, _acceleratorsNavItem);
                        }
                        else
                        {
                            int settingsIndex = -1;
                            for (int i = 0; i < NavigationItems.Count; i++)
                            {
                                if (NavigationItems[i].ViewModelType == typeof(SettingsViewModel))
                                {
                                    settingsIndex = i;
                                    break;
                                }
                            }
                            if (settingsIndex >= 0)
                                NavigationItems.Insert(settingsIndex, _acceleratorsNavItem);
                            else
                                NavigationItems.Add(_acceleratorsNavItem);
                        }
                    }
                }
                else
                {
                    if (NavigationItems.Contains(_acceleratorsNavItem))
                    {
                        NavigationItems.Remove(_acceleratorsNavItem);
                    }

                    if (CurrentViewModel == Accelerators)
                    {
                        NavigateTo(typeof(DashboardViewModel));
                    }
                }
            }

            if (System.Windows.Application.Current?.Dispatcher != null && 
                !System.Windows.Application.Current.Dispatcher.CheckAccess())
            {
                System.Windows.Application.Current.Dispatcher.Invoke(Apply);
            }
            else
            {
                Apply();
            }
        }

        public void NavigateTo(Type viewModelType)
        {
            if (viewModelType == typeof(AcceleratorsViewModel) && (_acceleratorsNavItem?.IsVisible != true || !ConfigManager.Current.EnableAcceleratorsMonitoring))
            {
                NavigateTo(typeof(DashboardViewModel));
                return;
            }

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