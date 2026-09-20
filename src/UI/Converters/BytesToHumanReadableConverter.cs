using System;
using System.Globalization;
using System.Windows.Data;
using SystemCoreMonitor.Core;

namespace SystemCoreMonitor.UI.Converters
{
    public class BytesToHumanReadableConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is long bytesLong)
                return MetricFormatting.FormatBytesAuto(bytesLong);
            if (value is ulong bytesUlong)
                return MetricFormatting.FormatBytesAuto((long)bytesUlong);
            if (value is double bytesDouble)
                return MetricFormatting.FormatBytesAuto((long)bytesDouble);
            return "0 B";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}