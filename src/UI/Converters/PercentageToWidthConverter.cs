using System;
using System.Globalization;
using System.Windows.Data;

namespace SystemCoreMonitor.UI.Converters
{
    public class PercentageToWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 2 &&
                values[0] is double percent &&
                values[1] is double totalWidth &&
                totalWidth > 0)
            {
                double clamped = Math.Clamp(percent, 0.0, 100.0);
                return (clamped / 100.0) * totalWidth;
            }
            return 0.0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}