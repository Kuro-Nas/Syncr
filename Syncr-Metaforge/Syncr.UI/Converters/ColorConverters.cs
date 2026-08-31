using Avalonia.Data.Converters;
using Avalonia.Media;
using SkiaSharp;
using System;
using System.Globalization;

namespace Syncr.UI.Converters
{
    public class SKColorToBrushConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is SKColor skColor)
            {
                return new SolidColorBrush(Color.FromArgb(skColor.Alpha, skColor.Red, skColor.Green, skColor.Blue));
            }
            return Brushes.Gray;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class IntToColorConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int count && count > 0)
            {
                return Brushes.OrangeRed;
            }
            return Brushes.Gray;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class StringToColorConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            string status = value?.ToString() ?? "";
            string target = parameter?.ToString() ?? "Online";

            if (status.Equals(target, StringComparison.OrdinalIgnoreCase))
                return Brushes.LimeGreen;
            
            if (status.Contains("Error", StringComparison.OrdinalIgnoreCase) || status.Equals("Offline", StringComparison.OrdinalIgnoreCase))
                return Brushes.OrangeRed;

            return Brushes.Gray;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class HexToBrushConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string hex && !string.IsNullOrEmpty(hex))
            {
                try
                {
                    return SolidColorBrush.Parse(hex);
                }
                catch
                {
                    return Brushes.Cyan;
                }
            }
            return Brushes.Cyan;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
