using System;
using System.Globalization;
using System.Windows.Data;

namespace GoodbyeAhmetWPF.Converters
{
    /// <summary>
    /// Returns the boolean negation of a bool value.
    /// Use for IsEnabled bindings where a "BooleanToVisibilityConverter ConverterParameter=Inverse"
    /// would otherwise (incorrectly) emit a Visibility value into a bool target.
    /// </summary>
    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b) return !b;
            return true;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b) return !b;
            return false;
        }
    }
}
