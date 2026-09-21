using System;
using System.Windows;
using SystemCoreMonitor.Core;

namespace SystemCoreMonitor
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            CrashLogger.Initialize();
            CrashLogger.AttachDispatcher(Dispatcher);
            base.OnStartup(e);
        }

        public static void SetTheme(string themeName)
        {
            if (Current != null && Current.Dispatcher != null && !Current.Dispatcher.CheckAccess())
            {
                Current.Dispatcher.Invoke(() => SetTheme(themeName));
                return;
            }

            string themeFile = "PastelDark.xaml";
            if (string.Equals(themeName, "Light", StringComparison.OrdinalIgnoreCase))
            {
                themeFile = "PastelLight.xaml";
            }
            else if (string.Equals(themeName, "Neon", StringComparison.OrdinalIgnoreCase))
            {
                themeFile = "PastelNeon.xaml";
            }
            else if (string.Equals(themeName, "Rose", StringComparison.OrdinalIgnoreCase))
            {
                themeFile = "PastelRose.xaml";
            }

            ResourceDictionary? newDict = null;
            try
            {
                var uri = new Uri($"/SystemCoreMonitor;component/UI/Themes/{themeFile}", UriKind.RelativeOrAbsolute);
                newDict = LoadComponent(uri) as ResourceDictionary;
            }
            catch (Exception ex)
            {
                CrashLogger.LogException("SetTheme", ex, false);
            }

            if (newDict == null) return;

            if (Current != null && Current.Resources != null)
            {
                var merged = Current.Resources.MergedDictionaries;
                int targetIndex = -1;
                for (int i = 0; i < merged.Count; i++)
                {
                    var m = merged[i];
                    if (m.Contains("BgApp") || (m.Source != null && m.Source.OriginalString.Contains("Pastel")))
                    {
                        targetIndex = i;
                        break;
                    }
                }

                if (targetIndex >= 0)
                {
                    merged[targetIndex] = newDict;
                }
                else
                {
                    merged.Add(newDict);
                }
            }
        }
    }
}
