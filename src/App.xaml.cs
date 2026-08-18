using System;
using System.Windows;

namespace SimplePCMonitor
{
    public partial class App : Application
    {
        public static void SetTheme(string themeName)
        {
            var dict = new ResourceDictionary();
            if (string.Equals(themeName, "Light", StringComparison.OrdinalIgnoreCase))
            {
                dict.Source = new Uri("UI/Themes/PastelLight.xaml", UriKind.Relative);
            }
            else
            {
                dict.Source = new Uri("UI/Themes/PastelDark.xaml", UriKind.Relative);
            }

            if (Current.Resources.MergedDictionaries.Count > 0)
            {
                Current.Resources.MergedDictionaries[0] = dict;
            }
            else
            {
                Current.Resources.MergedDictionaries.Insert(0, dict);
            }
        }
    }
}
