using System;
using System.IO;

namespace SimplePCMonitor.Core
{
    public class AppConfig
    {
        public int RefreshIntervalSeconds { get; set; }
        public string Theme { get; set; }
        public string ViewMode { get; set; }

        public AppConfig()
        {
            RefreshIntervalSeconds = 3;
            Theme = "Dark";
            ViewMode = "Analytics";
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
                    if (json.Contains("\"RefreshIntervalSeconds\": 5")) cfg.RefreshIntervalSeconds = 5;
                    if (json.Contains("\"ViewMode\": \"Hero\"")) cfg.ViewMode = "Hero";
                    if (json.Contains("\"ViewMode\": \"Widget\"")) cfg.ViewMode = "Widget";
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
                    string.Format("  \"ViewMode\": \"{0}\"\n", config.ViewMode) +
                    "}";

                File.WriteAllText(ConfigPath, json);
            }
            catch { }
        }
    }
}
