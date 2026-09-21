using System;
using SystemCoreMonitor.Core;
using SystemCoreMonitor.Core.Mvvm;

namespace SystemCoreMonitor.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private AppConfig _config;

        public AppConfig Config
        {
            get => _config;
            set => SetProperty(ref _config, value);
        }

        public string TitleTheme => LocalizationManager.Get("SettingsThemeTitle");
        public string DescTheme => LocalizationManager.Get("SettingsThemeDesc");
        public string TitleLanguage => LocalizationManager.Get("SettingsLangTitle");
        public string DescLanguage => LocalizationManager.Get("SettingsLangDesc");
        public string TitleInterval => LocalizationManager.Get("SettingsIntervalTitle");
        public string DescInterval => LocalizationManager.Get("SettingsIntervalDesc");
        public string Interval1sText => LocalizationManager.Get("SettingsInterval1s");
        public string Interval2sText => LocalizationManager.Get("SettingsInterval2s");
        public string Interval3sText => LocalizationManager.Get("SettingsInterval3s");
        public string Interval5sText => LocalizationManager.Get("SettingsInterval5s");
        public string Interval10sText => LocalizationManager.Get("SettingsInterval10s");
        public string TitleAccelerators => LocalizationManager.Get("SettingsAccelTitle");
        public string DescAccelerators => LocalizationManager.Get("SettingsAccelDesc");
        public string AccelEnabledText => LocalizationManager.Get("SettingsAccelEnabled");
        public string AccelDisabledText => LocalizationManager.Get("SettingsAccelDisabled");

        public string TitleRetention => LocalizationManager.Get("SettingsRetentionTitle");
        public string DescRetention => LocalizationManager.Get("SettingsRetentionDesc");
        public string Retention3dText => LocalizationManager.Get("SettingsRetention3d");
        public string Retention7dText => LocalizationManager.Get("SettingsRetention7d");
        public string Retention14dText => LocalizationManager.Get("SettingsRetention14d");
        public string Retention30dText => LocalizationManager.Get("SettingsRetention30d");

        public string CurrentTheme => Config.Theme;
        public string CurrentLanguage => Config.Language;
        public int CurrentInterval => Config.RefreshIntervalSeconds;
        public bool IsInterval1s => Config.RefreshIntervalSeconds == 1;
        public bool IsInterval3s => Config.RefreshIntervalSeconds == 3;
        public bool IsInterval5s => Config.RefreshIntervalSeconds == 5;
        public bool IsInterval10s => Config.RefreshIntervalSeconds == 10;
        public bool EnableAcceleratorsMonitoring => Config.EnableAcceleratorsMonitoring;

        public int CurrentRetention => Config.TranscriptRetentionDays;
        public bool IsRetention3d => Config.TranscriptRetentionDays == 3;
        public bool IsRetention7d => Config.TranscriptRetentionDays == 7;
        public bool IsRetention14d => Config.TranscriptRetentionDays == 14;
        public bool IsRetention30d => Config.TranscriptRetentionDays == 30;

        public RelayCommand<string> SetThemeCommand { get; }
        public RelayCommand<string> SetLanguageCommand { get; }
        public RelayCommand<object> SetIntervalCommand { get; }
        public RelayCommand<object> SetRetentionCommand { get; }
        public RelayCommand EnableAcceleratorsCommand { get; }
        public RelayCommand DisableAcceleratorsCommand { get; }

        public event Action<string>? ThemeChanged;
        public event Action<string>? LanguageChanged;
        public event Action<bool>? AcceleratorsMonitoringChanged;
        public event Action<int>? IntervalChanged;
        public event Action<int>? RetentionChanged;

        public SettingsViewModel()
        {
            _config = ConfigManager.Load();
            LocalizationManager.LanguageChanged += () => OnPropertyChanged(string.Empty);

            SetThemeCommand = new RelayCommand<string>(theme =>
            {
                if (string.IsNullOrEmpty(theme)) return;
                Config.Theme = theme;
                ConfigManager.Save(Config);
                ThemeChanged?.Invoke(theme);
                OnPropertyChanged(nameof(Config));
                OnPropertyChanged(nameof(CurrentTheme));
            });

            SetLanguageCommand = new RelayCommand<string>(lang =>
            {
                if (string.IsNullOrEmpty(lang)) return;
                Config.Language = lang;
                ConfigManager.Save(Config);
                LocalizationManager.CurrentLanguage = lang;
                LanguageChanged?.Invoke(lang);
                OnPropertyChanged(nameof(Config));
                OnPropertyChanged(nameof(CurrentLanguage));
            });

            SetIntervalCommand = new RelayCommand<object>(param =>
            {
                int sec = 0;
                if (param is int i) sec = i;
                else if (param != null && int.TryParse(param.ToString(), out int parsed)) sec = parsed;

                if (sec <= 0) return;
                Config.RefreshIntervalSeconds = sec;
                ConfigManager.Save(Config);
                IntervalChanged?.Invoke(sec);
                OnPropertyChanged(nameof(Config));
                OnPropertyChanged(nameof(CurrentInterval));
                OnPropertyChanged(nameof(IsInterval1s));
                OnPropertyChanged(nameof(IsInterval3s));
                OnPropertyChanged(nameof(IsInterval5s));
                OnPropertyChanged(nameof(IsInterval10s));
            });

            SetRetentionCommand = new RelayCommand<object>(param =>
            {
                int days = 0;
                if (param is int d) days = d;
                else if (param != null && int.TryParse(param.ToString(), out int parsed)) days = parsed;

                if (days <= 0) return;
                Config.TranscriptRetentionDays = days;
                ConfigManager.Save(Config);
                RetentionChanged?.Invoke(days);
                OnPropertyChanged(nameof(Config));
                OnPropertyChanged(nameof(CurrentRetention));
                OnPropertyChanged(nameof(IsRetention3d));
                OnPropertyChanged(nameof(IsRetention7d));
                OnPropertyChanged(nameof(IsRetention14d));
                OnPropertyChanged(nameof(IsRetention30d));
            });

            EnableAcceleratorsCommand = new RelayCommand(() => SetAcceleratorsMonitoring(true));
            DisableAcceleratorsCommand = new RelayCommand(() => SetAcceleratorsMonitoring(false));
        }

        private void SetAcceleratorsMonitoring(bool enabled)
        {
            Config.EnableAcceleratorsMonitoring = enabled;
            ConfigManager.Save(Config);
            AcceleratorsMonitoringChanged?.Invoke(enabled);
            OnPropertyChanged(nameof(Config));
            OnPropertyChanged(nameof(EnableAcceleratorsMonitoring));
        }
    }
}