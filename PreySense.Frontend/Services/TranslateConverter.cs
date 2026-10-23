using System;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;

namespace PreySense.Frontend.Services
{
    public sealed class TranslateConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string text)
                return LocalizationService.Translate(text);
            if (value is ComboBoxItem item && item.Content is string content)
                return LocalizationService.Translate(content);
            return value;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value;
    }
}
