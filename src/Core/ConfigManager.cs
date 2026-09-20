using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SystemCoreMonitor.Core
{
    public class AppConfig
    {
        public int RefreshIntervalSeconds { get; set; } = 3;
        public string Theme { get; set; } = "Dark";
        public string ViewMode { get; set; } = "Full";
        public string Language { get; set; } = "es";
        public bool AlwaysOnTop { get; set; } = false;
        public bool AutoPowerScheme { get; set; } = false;
        public bool MinimizeToTray { get; set; } = true;
        public bool CloseToTray { get; set; } = true;
        public bool StartMinimizedToTray { get; set; } = false;
        public bool RunAtStartup { get; set; } = false;

        /// <summary>Retention period in days for AI CLI session transcripts (Claude, Gemini). Default: 7 days.</summary>
        public int TranscriptRetentionDays { get; set; } = 7;
    }

    [JsonSourceGenerationOptions(
        WriteIndented = true,
        PropertyNamingPolicy = JsonKnownNamingPolicy.Unspecified,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonSerializable(typeof(AppConfig))]
    internal partial class AppConfigJsonContext : JsonSerializerContext { }

    public static class ConfigManager
    {
        private static readonly string LegacyConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SimplePCMonitor"
        );

        private static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SystemCoreMonitor"
        );

        private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

        public static AppConfig Current { get; set; } = Load();

        public static AppConfig Load()
        {
            try
            {
                // One-time migration shim: copy old config if new one doesn't exist yet
                MigrateLegacyConfigIfNeeded();

                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    var cfg = JsonSerializer.Deserialize(json, AppConfigJsonContext.Default.AppConfig);
                    if (cfg != null) return cfg;
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException("ConfigManager.Load", ex, false);
            }

            return new AppConfig();
        }

        public static void Save(AppConfig config)
        {
            try
            {
                if (!Directory.Exists(ConfigDir))
                    Directory.CreateDirectory(ConfigDir);

                string json = JsonSerializer.Serialize(config, AppConfigJsonContext.Default.AppConfig);
                File.WriteAllText(ConfigPath, json);
                Current = config;
            }
            catch (Exception ex)
            {
                CrashLogger.LogException("ConfigManager.Save", ex, false);
            }
        }

        /// <summary>
        /// One-time migration: if the old SimplePCMonitor config exists and the new
        /// SystemCoreMonitor config does not, copy the old file so user settings are preserved.
        /// </summary>
        private static void MigrateLegacyConfigIfNeeded()
        {
            try
            {
                string legacyPath = Path.Combine(LegacyConfigDir, "config.json");
                if (File.Exists(legacyPath) && !File.Exists(ConfigPath))
                {
                    if (!Directory.Exists(ConfigDir))
                        Directory.CreateDirectory(ConfigDir);

                    File.Copy(legacyPath, ConfigPath);
                }
            }
            catch { /* Non-fatal: defaults will be used if migration fails */ }
        }
    }
}