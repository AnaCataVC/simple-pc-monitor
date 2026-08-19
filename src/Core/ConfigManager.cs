using System;
using System.IO;

namespace SimplePCMonitor.Core
{
    public class AppConfig
    {
        public int RefreshIntervalSeconds { get; set; }
        public string Theme { get; set; }
        public string ViewMode { get; set; }
        public string Language { get; set; }
        public bool AlwaysOnTop { get; set; }
        public bool AutoPowerScheme { get; set; }
        public bool MinimizeToTray { get; set; }
        public bool CloseToTray { get; set; }
        public bool StartMinimizedToTray { get; set; }
        public bool RunAtStartup { get; set; }

        public AppConfig()
        {
            RefreshIntervalSeconds = 3;
            Theme = "Dark";
            ViewMode = "Full";
            Language = "es";
            AlwaysOnTop = false;
            AutoPowerScheme = false;
            MinimizeToTray = true;
            CloseToTray = true;
            StartMinimizedToTray = false;
            RunAtStartup = false;
        }
    }

    public static class ConfigManager
    {
        private static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SimplePCMonitor"
        );
        private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    var cfg = new AppConfig();
                    if (json.Contains("\"Theme\": \"Light\"")) cfg.Theme = "Light";
                    else if (json.Contains("\"Theme\": \"Neon\"")) cfg.Theme = "Neon";
                    else if (json.Contains("\"Theme\": \"Rose\"")) cfg.Theme = "Rose";
                    else cfg.Theme = "Dark";

                    if (json.Contains("\"RefreshIntervalSeconds\": 5")) cfg.RefreshIntervalSeconds = 5;
                    else if (json.Contains("\"RefreshIntervalSeconds\": 1")) cfg.RefreshIntervalSeconds = 1;
                    else cfg.RefreshIntervalSeconds = 3;

                    if (json.Contains("\"ViewMode\": \"Hero\"")) cfg.ViewMode = "Hero";
                    else if (json.Contains("\"ViewMode\": \"Widget\"")) cfg.ViewMode = "Widget";
                    else cfg.ViewMode = "Full";

                    if (json.Contains("\"Language\": \"en\"")) cfg.Language = "en";
                    else cfg.Language = "es";

                    if (json.Contains("\"AlwaysOnTop\": true")) cfg.AlwaysOnTop = true;
                    if (json.Contains("\"AutoPowerScheme\": true")) cfg.AutoPowerScheme = true;

                    if (json.Contains("\"MinimizeToTray\": false")) cfg.MinimizeToTray = false;
                    else cfg.MinimizeToTray = true;

                    if (json.Contains("\"CloseToTray\": false")) cfg.CloseToTray = false;
                    else cfg.CloseToTray = true;

                    if (json.Contains("\"StartMinimizedToTray\": true")) cfg.StartMinimizedToTray = true;
                    if (json.Contains("\"RunAtStartup\": true")) cfg.RunAtStartup = true;

                    return cfg;
                }
            }
            catch { }

            return new AppConfig();
        }

        public static void Save(AppConfig config)
        {
            try
            {
                if (!Directory.Exists(ConfigDir))
                {
                    Directory.CreateDirectory(ConfigDir);
                }

                string json = "{\n" +
                    string.Format("  \"RefreshIntervalSeconds\": {0},\n", config.RefreshIntervalSeconds) +
                    string.Format("  \"Theme\": \"{0}\",\n", config.Theme) +
                    string.Format("  \"ViewMode\": \"{0}\",\n", config.ViewMode) +
                    string.Format("  \"Language\": \"{0}\",\n", config.Language) +
                    string.Format("  \"AlwaysOnTop\": {0},\n", config.AlwaysOnTop ? "true" : "false") +
                    string.Format("  \"AutoPowerScheme\": {0},\n", config.AutoPowerScheme ? "true" : "false") +
                    string.Format("  \"MinimizeToTray\": {0},\n", config.MinimizeToTray ? "true" : "false") +
                    string.Format("  \"CloseToTray\": {0},\n", config.CloseToTray ? "true" : "false") +
                    string.Format("  \"StartMinimizedToTray\": {0},\n", config.StartMinimizedToTray ? "true" : "false") +
                    string.Format("  \"RunAtStartup\": {0}\n", config.RunAtStartup ? "true" : "false") +
                    "}";

                File.WriteAllText(ConfigPath, json);
            }
            catch { }
        }
    }
}
